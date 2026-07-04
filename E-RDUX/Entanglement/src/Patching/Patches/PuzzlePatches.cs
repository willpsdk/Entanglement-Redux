using System;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

using StressLevelZero.Interaction;
using StressLevelZero.Pool;

using Entanglement.Network;
using Entanglement.Extensions;

namespace Entanglement.Objects
{
    /// <summary>
    /// Bridges story mode interactions across the network.
    /// Buttons and key receivers are event based, everything jointed (doors, levers, valves) syncs physically.
    /// </summary>
    public static class SceneEventSync {
        // Deduplicates events that happen both physically (a rep pressing the button) and over the network
        static Dictionary<long, float> lastEventTimes = new Dictionary<long, float>();

        public const float debounceWindow = 0.4f;

        public static long EventKey(int instanceId, SceneEventType type) => ((long)instanceId << 8) | (byte)type;

        // Returns false when this event already fired within the debounce window
        public static bool MarkEvent(long key) {
            if (lastEventTimes.TryGetValue(key, out float last) && Time.time - last < debounceWindow)
                return false;

            lastEventTimes[key] = Time.time;
            return true;
        }

        public static void OnSceneCleanup() {
            lastEventTimes.Clear();
            ButtonTogglePatch.pressedStates.Clear();
            PullDevicePatch.pulledStates.Clear();
        }

        // Walks up the hierarchy to the poolee root, pooled objects share a spawn id on every client
        public static PooleeSyncable FindPooleeSyncable(Transform transform) {
            for (Transform current = transform; current; current = current.parent) {
                PooleeSyncable poolee = PooleeSyncable._Cache.Get(current.gameObject);
                if (poolee)
                    return poolee;
            }

            return null;
        }

        public static void SendEvent(SceneEventType type, Transform transform, ushort arg = 0) {
            if (!SteamIntegration.hasLobby || Node.activeNode == null)
                return;

            SceneEventMessageData data = new SceneEventMessageData() {
                eventType = type,
                arg = arg,
                objectPath = transform.GetFullPath()
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, data);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        public static void ApplyRemoteEvent(SceneEventType type, ushort arg, string objectPath) {
            // NPC events address by poolee spawn id first, the scene path is only a fallback
            if (type == SceneEventType.NpcDeath || type == SceneEventType.NpcDespawn) {
                ApplyNpcEvent(type, arg, objectPath);
                return;
            }

            Transform target = objectPath.GetFromFullPath();
            if (!target)
                return;

            try {
                switch (type) {
                    case SceneEventType.ButtonPress:
                    case SceneEventType.ButtonDepress: {
                        ButtonToggle button = target.GetComponent<ButtonToggle>();
                        if (!button) return;

                        if (!MarkEvent(EventKey(button.GetInstanceID(), type)))
                            return;

                        if (type == SceneEventType.ButtonPress) {
                            button.onPress?.Invoke();

                            if (!button._hasBeenPressed) {
                                button.onPressOneShot?.Invoke();
                                button._hasBeenPressed = true;
                            }
                        }
                        else
                            button.onDepress?.Invoke();

                        break;
                    }
                    case SceneEventType.KeyLock:
                    case SceneEventType.KeyUnlock: {
                        KeyReciever reciever = target.GetComponent<KeyReciever>();
                        if (!reciever) return;

                        if (!MarkEvent(EventKey(reciever.GetInstanceID(), type)))
                            return;

                        if (type == SceneEventType.KeyLock)
                            reciever.onUnlock?.Invoke();
                        else
                            reciever.onLock?.Invoke();

                        break;
                    }
                    case SceneEventType.PullDevicePull: {
                        StressLevelZero.Interaction.PullDevice device = target.GetComponent<StressLevelZero.Interaction.PullDevice>();
                        if (!device) return;

                        if (!MarkEvent(EventKey(device.GetInstanceID(), type)))
                            return;

                        // Everyone runs the pull so lids/sounds play; the item spawn dedupes through the
                        // existing pool flow (host-authoritative spawn, local client spawns get deactivated)
                        device.OnHandlePull?.Invoke();

                        break;
                    }
                    case SceneEventType.MonoMatInsert: {
                        Control_MonoMat monoMat = target.GetComponent<Control_MonoMat>();
                        if (!monoMat) return;

                        // The inserted magazine is synced (it was held), so the machine can consume the same one here
                        if (!ObjectSync.TryGetSyncable(arg, out Syncable syncable) || !(syncable is TransformSyncable))
                            return;

                        TransformSyncable magSync = syncable.Cast<TransformSyncable>();
                        StressLevelZero.Props.Weapons.Magazine magazine = magSync.GetComponentInChildren<StressLevelZero.Props.Weapons.Magazine>(true);
                        if (!magazine) return;

                        Patching.MonoMatInsertPatch.isRemoteInsert = true;
                        try { monoMat.InsertMagazine(magazine); }
                        finally { Patching.MonoMatInsertPatch.isRemoteInsert = false; }

                        break;
                    }
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn($"Failed to apply scene event {type} at {objectPath}: {e.Message}");
            }
        }

        private static void ApplyNpcEvent(SceneEventType type, ushort pooleeId, string objectPath) {
            Transform root = null;

            if (pooleeId != 0 && PooleeSyncable._PooleeLookup.TryGetValue(pooleeId, out PooleeSyncable poolee) && poolee)
                root = poolee.transform;
            else if (!string.IsNullOrEmpty(objectPath))
                root = objectPath.GetFromFullPath(); // Scene-placed NPCs aren't pooled, resolve them by path

            if (!root)
                return;

            try {
                switch (type) {
                    case SceneEventType.NpcDeath: {
                        StressLevelZero.AI.AIBrain brain = root.GetComponentInChildren<StressLevelZero.AI.AIBrain>(true);
                        if (!brain || brain.isDead) return;

                        Patching.AIBrainDeathPatch.isRemoteDeath = true;
                        try {
                            brain.OnDeath();

                            if (brain.puppetMaster)
                                brain.puppetMaster.Kill();
                        }
                        finally { Patching.AIBrainDeathPatch.isRemoteDeath = false; }

                        break;
                    }
                    case SceneEventType.NpcDespawn: {
                        Poolee rootPoolee = root.GetComponent<Poolee>();
                        if (!rootPoolee || !root.gameObject.activeInHierarchy) return;

                        Patching.AIBrainDespawnPatch.isRemoteDespawn = true;
                        try { rootPoolee.Despawn(); }
                        finally { Patching.AIBrainDespawnPatch.isRemoteDespawn = false; }

                        break;
                    }
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn($"Failed to apply NPC event {type} for poolee {pooleeId}: {e.Message}");
            }
        }
    }
}

namespace Entanglement.Patching
{
    using Entanglement.Objects;

    // Buttons are pressed physically, so state transitions are detected after the game's own Update ran
    [HarmonyPatch(typeof(ButtonToggle), "Update")]
    public static class ButtonTogglePatch
    {
        public static Dictionary<int, bool> pressedStates = new Dictionary<int, bool>();

        public static void Postfix(ButtonToggle __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            bool pressed = __instance._isPressed;
            int id = __instance.GetInstanceID();

            if (!pressedStates.TryGetValue(id, out bool wasPressed)) {
                pressedStates[id] = pressed;
                return;
            }

            if (pressed == wasPressed)
                return;

            pressedStates[id] = pressed;

            SceneEventType type = pressed ? SceneEventType.ButtonPress : SceneEventType.ButtonDepress;

            if (SceneEventSync.MarkEvent(SceneEventSync.EventKey(id, type)))
                SceneEventSync.SendEvent(type, __instance.transform);
        }
    }

    // Fires when a key is fully seated and locked into its receiver
    [HarmonyPatch(typeof(KeyReciever), "OnMagazineLocked")]
    public static class KeyLockPatch
    {
        public static void Postfix(KeyReciever __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            if (SceneEventSync.MarkEvent(SceneEventSync.EventKey(__instance.GetInstanceID(), SceneEventType.KeyLock)))
                SceneEventSync.SendEvent(SceneEventType.KeyLock, __instance.transform);
        }
    }

    [HarmonyPatch(typeof(KeyReciever), "OnMagazineUnlocked")]
    public static class KeyUnlockPatch
    {
        public static void Postfix(KeyReciever __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            if (SceneEventSync.MarkEvent(SceneEventSync.EventKey(__instance.GetInstanceID(), SceneEventType.KeyUnlock)))
                SceneEventSync.SendEvent(SceneEventType.KeyUnlock, __instance.transform);
        }
    }

    // Pull box handles fire physically only on the puller's client, detect the pull and replicate it
    [HarmonyPatch(typeof(PullDevice), "OnGripAttachedUpdate")]
    public static class PullDevicePatch
    {
        public static Dictionary<int, bool> pulledStates = new Dictionary<int, bool>();

        public static void Postfix(PullDevice __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            bool pulled = __instance._isPulled;
            int id = __instance.GetInstanceID();

            if (!pulledStates.TryGetValue(id, out bool wasPulled)) {
                pulledStates[id] = pulled;
                return;
            }

            if (pulled == wasPulled)
                return;

            pulledStates[id] = pulled;

            // The handle springing back is local-only, just the pull matters
            if (!pulled)
                return;

            if (SceneEventSync.MarkEvent(SceneEventSync.EventKey(id, SceneEventType.PullDevicePull)))
                SceneEventSync.SendEvent(SceneEventType.PullDevicePull, __instance.transform);
        }
    }

    // NPC deaths converge on whichever simulation killed it first, everyone else applies the same death
    [HarmonyPatch(typeof(StressLevelZero.AI.AIBrain), "OnDeath")]
    public static class AIBrainDeathPatch
    {
        public static bool isRemoteDeath = false;

        public static void Postfix(StressLevelZero.AI.AIBrain __instance) {
            if (!SteamIntegration.hasLobby || isRemoteDeath)
                return;

            if (!SceneEventSync.MarkEvent(SceneEventSync.EventKey(__instance.GetInstanceID(), SceneEventType.NpcDeath)))
                return;

            PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(__instance.transform);

            SceneEventSync.SendEvent(SceneEventType.NpcDeath, __instance.transform, poolee ? poolee.id : (ushort)0);
        }
    }

    // Keeps corpses from lingering on clients after the host's poolee despawns
    [HarmonyPatch(typeof(StressLevelZero.AI.AIBrain), "OnDespawn")]
    public static class AIBrainDespawnPatch
    {
        public static bool isRemoteDespawn = false;

        public static void Postfix(StressLevelZero.AI.AIBrain __instance) {
            if (!SteamIntegration.hasLobby || isRemoteDespawn)
                return;

            PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(__instance.transform);
            if (!poolee) // Despawning is a pooled concept, nothing to do for scene NPCs
                return;

            if (SceneEventSync.MarkEvent(SceneEventSync.EventKey(__instance.GetInstanceID(), SceneEventType.NpcDespawn)))
                SceneEventSync.SendEvent(SceneEventType.NpcDespawn, __instance.transform, poolee.id);
        }
    }

    // MonoMat vending machines: replicate magazine deposits so balance, unlock state and change match everywhere.
    // Only the physical inserter's client fires this, so no debounce is needed - just the remote-apply guard.
    [HarmonyPatch(typeof(Control_MonoMat), "InsertMagazine")]
    public static class MonoMatInsertPatch
    {
        public static bool isRemoteInsert = false;

        public static void Postfix(Control_MonoMat __instance, StressLevelZero.Props.Weapons.Magazine magazine) {
            if (!SteamIntegration.hasLobby || isRemoteInsert)
                return;

            ushort magId = 0;
            if (magazine) {
                TransformSyncable magSync = TransformSyncable.cache.Get(magazine.gameObject);
                if (magSync && magSync.isValid)
                    magId = magSync.objectId;
            }

            if (magId == 0)
                EntangleLogger.Warn("A magazine was inserted into a MonoMat but wasn't synced, other players won't see the deposit!");

            SceneEventSync.SendEvent(SceneEventType.MonoMatInsert, __instance.transform, magId);
        }
    }
}
