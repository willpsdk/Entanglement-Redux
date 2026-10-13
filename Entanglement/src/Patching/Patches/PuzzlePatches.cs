using System;
using System.Collections;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

using StressLevelZero.Interaction;
using StressLevelZero.Pool;

using Entanglement.Network;
using Entanglement.Extensions;

using MelonLoader;

namespace Entanglement.Objects
{
    public static class SceneEventSync {
        /// <summary>Logs scene events as they are sent and applied. Off unless enabled in Debug.</summary>
        public static bool traceEnabled;

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

        // Scene events fired this level, in order, replayed to late joiners (host only)
        static List<SceneEventMessageData> firedEvents = new List<SceneEventMessageData>();

        public static void RecordForLateJoin(SceneEventType type, ushort arg, string objectPath) {
            if (!Node.isServer)
                return;

            if (type == SceneEventType.TurretFire || type == SceneEventType.TurretState)
                return;

            if ((type == SceneEventType.NpcDeath || type == SceneEventType.NpcDespawn)
                && objectPath != null && objectPath.StartsWith("#"))
                return;

            if (type == SceneEventType.NpcFizzle || type == SceneEventType.NpcConfig
                || type == SceneEventType.MonoMatInsert || type == SceneEventType.MonoMatBullets
                || type == SceneEventType.ClawExtend)
                return;

            if (objectPath != null && objectPath.StartsWith("~"))
                return;

            firedEvents.Add(new SceneEventMessageData() { eventType = type, arg = arg, objectPath = objectPath });
        }

        public static int ReplayEventsTo(long userId) {
            int sent = 0;

            foreach (SceneEventMessageData recorded in firedEvents) {
                SceneEventMessageData data = new SceneEventMessageData() {
                    eventType = recorded.eventType,
                    arg = recorded.arg,
                    objectPath = recorded.objectPath,
                    replay = true,
                };
                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, data);
                if (message == null)
                    continue;

                Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
                sent++;
            }

            return sent;
        }

        // ---- Which level an event belongs to ------------------------------------------------------

        static string taggedScene;
        static ushort taggedValue;

        /// <summary>A short hash of the level we are in, carried by every scene event.</summary>
        public static ushort CurrentSceneTag() {
            string name;
            try { name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
            catch { return 0; }
            if (string.IsNullOrEmpty(name))
                return 0;
            if (name == taggedScene)
                return taggedValue;

            // FNV-1a, so every machine gets the same number for the same name.
            uint hash = 2166136261;
            string lower = name.ToLowerInvariant();
            for (int i = 0; i < lower.Length; i++) {
                hash ^= lower[i];
                hash *= 16777619;
            }
            ushort value = (ushort)(hash ^ (hash >> 16));
            if (value == 0)
                value = 1;

            taggedScene = name;
            taggedValue = value;
            return value;
        }

        static float sceneReadyTime = -100f;

        public static void OnSceneReady() {
            sceneReadyTime = Time.realtimeSinceStartup;
        }

        public static bool Settling => Time.realtimeSinceStartup - sceneReadyTime < 2f;

        /// <summary>Seconds since this level finished loading.</summary>
        public static float SinceReady => Time.realtimeSinceStartup - sceneReadyTime;

        static bool Loading => SceneLoader.loading;

        static bool UnaddressablePoolCopy(Transform transform) {
            try {
                for (Transform current = transform; current; current = current.parent) {
                    if (current.name == "Pool Manager")
                        return true;

                    PooleeSyncable synced = PooleeSyncable._Cache.Get(current.gameObject);
                    if (synced)
                        return synced.id == 0 && !synced.pathAddressed;

                    if (Poolee.Cache.Get(current.gameObject))
                        return true;
                }
            }
            catch { }
            return false;
        }

        public static void OnSceneCleanup() {
            lastEventTimes.Clear();
            firedEvents.Clear();
            Patching.ButtonTogglePatch.pressedStates.Clear();
            Patching.ButtonTogglePatch.ClearTurretButtons();
            Patching.ButtonTogglePatch.ClearRemoteHolds();
            Patching.PullDevicePatch.pulledStates.Clear();
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

        public static string BuildEventPath(Transform transform) {
            string fullPath = transform.GetFullPath();

            try {
                PooleeSyncable poolee = FindPooleeSyncable(transform);
                // A turret each machine spawned itself: by name and place (TurretSync.BindByPlace).
                if (poolee != null && !string.IsNullOrEmpty(poolee.placeKey)) {
                    string placeRoot = poolee.transform.GetFullPath();
                    if (fullPath == placeRoot)
                        return poolee.placeKey;
                    if (fullPath.StartsWith(placeRoot + "/"))
                        return poolee.placeKey + fullPath.Substring(placeRoot.Length);
                }
                if (poolee == null || poolee.id == 0 || poolee.pathAddressed)
                    return fullPath;

                string rootPath = poolee.transform.GetFullPath();
                if (fullPath == rootPath)
                    return "#" + poolee.id;
                if (fullPath.StartsWith(rootPath + "/"))
                    return "#" + poolee.id + fullPath.Substring(rootPath.Length);
            }
            catch { }

            return fullPath;
        }

        public static void SendEventPath(SceneEventType type, string objectPath, ushort arg, bool record = true) {
            if (!SteamIntegration.hasLobby || Node.activeNode == null || Loading)
                return;

            SceneEventMessageData data = new SceneEventMessageData() {
                eventType = type,
                arg = arg,
                objectPath = objectPath,
                sceneTag = CurrentSceneTag(),
            };
            if (record)
                RecordForLateJoin(type, arg, objectPath);

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, data);
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        public static void SendEvent(SceneEventType type, Transform transform, ushort arg = 0, bool record = true, string pathSuffix = null) {
            if (!SteamIntegration.hasLobby || Node.activeNode == null || !transform)
                return;

            if (Loading)
                return;

            if (UnaddressablePoolCopy(transform))
                return;

            SceneEventMessageData data = new SceneEventMessageData() {
                eventType = type,
                arg = arg,
                objectPath = BuildEventPath(transform) + pathSuffix,
                sceneTag = CurrentSceneTag(),
            };

            if (record)
                RecordForLateJoin(type, arg, data.objectPath);

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, data);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            if (traceEnabled)
                EntangleLogger.Log("[SceneTrace] SENT " + type + " arg=" + arg
                    + " as=" + (SteamIntegration.isHost ? "HOST" : "CLIENT") + " path=" + data.objectPath);
        }

        public static bool applyingRemote;

        public static void ApplyRemoteEvent(SceneEventType type, ushort arg, string objectPath, bool replay = false, bool record = false) {
            if (traceEnabled)
                EntangleLogger.Log("[SceneTrace] RECV " + type + " arg=" + arg
                    + " as=" + (SteamIntegration.isHost ? "HOST" : "CLIENT") + " path=" + objectPath);

            if (type == SceneEventType.NpcBind) {
                if (record)
                    RecordForLateJoin(type, arg, objectPath);
                if (!Node.isServer)
                    SceneNpcSync.Apply(arg, objectPath);
                return;
            }

            if (type == SceneEventType.NpcConfig) {
                if (!Node.isServer)
                    NpcConfigSync.Apply(arg, objectPath);
                return;
            }

            if (type == SceneEventType.ClawBind || type == SceneEventType.ClawMental || type == SceneEventType.ClawExtend) {
                if (record)
                    RecordForLateJoin(type, arg, objectPath);
                // The path carries the body count and the extension. Dropping it left the claw
                // with bodies collected and none of them synced (the arm never moved).
                BossClawSync.ApplyEvent(type, arg, null, objectPath);
                return;
            }

            // NPC events address by poolee spawn id first, the scene path is only a fallback
            if (type == SceneEventType.NpcDeath || type == SceneEventType.NpcDespawn || type == SceneEventType.NpcFizzle) {
                if (record)
                    RecordForLateJoin(type, arg, objectPath);
                ApplyNpcEvent(type, arg, objectPath);
                return;
            }

            bool settled;
            Transform target = ResolveTarget(type, objectPath, out settled);
            if (settled && !target)
                return;
            if (!target) {
                // Buttons in culled zones can take a moment to exist after a scene loads
                MelonCoroutines.Start(RetryApply(type, arg, objectPath, replay, record));
                return;
            }

            if (record)
                RecordForLateJoin(type, arg, objectPath);

            bool wasApplying = applyingRemote;
            applyingRemote = true;
            try {
                switch (type) {
                    case SceneEventType.ButtonPress:
                    case SceneEventType.ButtonDepress: {
                        ButtonToggle button = target.GetComponent<ButtonToggle>();
                        if (!button) return;

                        // An older build may still send these. The turret's state comes from the host.
                        if (Patching.ButtonTogglePatch.IsTurretButton(button)) return;

                        bool remoteHeld = Patching.ButtonTogglePatch.IsRemoteHeld(button);
                        if (type == SceneEventType.ButtonPress) {
                            if (button._isPressed) {
                                EntangleLogger.Debug("[Button] " + button.name + ": press from another player ignored, already down here"
                                    + (remoteHeld ? " for them" : " (pushed here)"));
                                return;
                            }
                            if (!remoteHeld && Patching.ButtonTogglePatch.PressedByOtherJustNow(button)) {
                                EntangleLogger.Debug("[Button] " + button.name + ": press from another player ignored, it was pushed here a moment ago");
                                return;
                            }
                        }
                        else if (!remoteHeld) {
                            EntangleLogger.Debug("[Button] " + button.name + ": release from another player ignored, not held down for anyone here");
                            return;
                        }
                        EntangleLogger.Debug("[Button] " + button.name + (type == SceneEventType.ButtonPress
                            ? ": pressed by another player, held down here until they let go"
                            : ": let go by the player holding it") + (replay ? " (catching up)" : ""));

                        if (type == SceneEventType.ButtonPress) {
                            Patching.ButtonTogglePatch.HoldForRemote(button);
                            button._isPressed = true;
                            PlayButtonClip(button, true);
                            InvokeButtonEvent(button.onPress, button, "onPress");

                            if (!button._hasBeenPressed) {
                                button._hasBeenPressed = true;
                                InvokeButtonEvent(button.onPressOneShot, button, "onPressOneShot");
                            }
                        }
                        else {
                            Patching.ButtonTogglePatch.ReleaseRemote(button);
                            button._isPressed = false;
                            PlayButtonClip(button, false);
                            InvokeButtonEvent(button.onDepress, button, "onDepress");
                        }

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

                        device.OnHandlePull?.Invoke();

                        break;
                    }
                    case SceneEventType.MonoMatInsert: {
                        Control_MonoMat monoMat = target.GetComponent<Control_MonoMat>();
                        if (!monoMat) return;

                        // The inserted magazine is synced (it was held), so the machine can consume the same one here
                        if (!ObjectSync.TryGetSyncable(arg, out Syncable syncable) || !(syncable is TransformSyncable)) {
                            EntangleLogger.Debug("[MonoMat] Deposit of mag #" + arg + " into " + monoMat.name + ": no such synced object here");
                            Patching.MonoMatSync.ConsumeNear(monoMat);
                            return;
                        }

                        TransformSyncable magSync = (TransformSyncable)syncable;
                        StressLevelZero.Props.Weapons.Magazine magazine = magSync.GetComponentInChildren<StressLevelZero.Props.Weapons.Magazine>(true);
                        if (!magazine) {
                            EntangleLogger.Debug("[MonoMat] Deposit of #" + arg + " into " + monoMat.name + ": it has no magazine here");
                            return;
                        }

                        EntangleLogger.Debug("[MonoMat] Another player put mag #" + arg + " into " + monoMat.name
                            + ": taking our copy, balance left to the host (now " + monoMat._smallBullets + " small, " + monoMat._mediumBullets + " medium)");

                        // Only our copy of the magazine goes in: the balance comes from the host.
                        Patching.MonoMatInsertPatch.isRemoteInsert = true;
                        Patching.MonoMatSync.consumingCopy = true;
                        try { monoMat.InsertMagazine(magazine); }
                        finally {
                            Patching.MonoMatInsertPatch.isRemoteInsert = false;
                            Patching.MonoMatSync.consumingCopy = false;
                        }

                        break;
                    }
                    case SceneEventType.MonoMatBullets: {
                        // A client's deposit: the host adds it, and publishes the balance.
                        if (!Node.isServer)
                            break;
                        Control_MonoMat monoMat = target.GetComponent<Control_MonoMat>();
                        if (!monoMat) return;
                        int count = arg >> 1;
                        bool small = (arg & 1) != 0;
                        if (count > 0)
                            monoMat.AddBullets(count, small);
                        EntangleLogger.Debug("[MonoMat] A client deposited " + count + (small ? " small" : " medium") + " into " + monoMat.name
                            + ": balance now " + monoMat._smallBullets + " small, " + monoMat._mediumBullets + " medium");
                        break;
                    }

                    default: {
                        if (Patching.BreakSync.Apply(type, arg, target, replay))
                            break;
                        if (Patching.TurretEventSync.Apply(type, arg, target))
                            break;
                        if (BossClawSync.ApplyEvent(type, arg, target, objectPath))
                            break;
                        if (Patching.TriggerRelay.Apply(type, target))
                            break;
                        if (Patching.StoryInteractSync.Apply(type, arg, target))
                            break;
                        if (Patching.GarageDoorSync.Apply(type, target))
                            break;

                        EntangleLogger.Warn("Scene event " + type + " had no handler for " + objectPath);
                        break;
                    }
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn($"Failed to apply scene event {type} at {objectPath}: {e.Message}");
            }
            finally { applyingRemote = wasApplying; }
        }

        
        static int buttonEventFailures;

        static void PlayButtonClip(ButtonToggle button, bool press) {
            try {
                var clips = press ? button.pressClips : button.depressClips;
                if (clips == null || clips.Length == 0)
                    return;
                AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
                if (clip == null)
                    return;
                GameObject go = new GameObject("Entanglement Button Sound");
                go.transform.position = button.transform.position;
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                if (button.mixerGroup != null)
                    source.outputAudioMixerGroup = button.mixerGroup;
                source.clip = clip;
                source.Play();
                UnityEngine.Object.Destroy(go, clip.length + 0.1f);
            }
            catch { }
        }

        internal static void InvokeButtonEvent(UnityEngine.Events.UnityEvent handlers, ButtonToggle button, string name) {
            if (handlers == null)
                return;
            try { handlers.Invoke(); }
            catch (Exception e) {
                if (buttonEventFailures++ < 20)
                    EntangleLogger.Warn("[Button] " + button.name + " " + name + " threw while replaying a press: " + e.Message);
            }
        }

        // ---- Where it happened ---------------------------------------------------------------------

        const string SpotMark = "~@";

        public static string SpotSuffix(Transform transform) {
            Vector3 spot;
            if (!BreakPieces.TryGetHome(transform, out spot))
                return null;
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            return SpotMark + spot.x.ToString("F2", inv) + "," + spot.y.ToString("F2", inv) + "," + spot.z.ToString("F2", inv);
        }

        static bool TryParseSpot(string text, out Vector3 spot) {
            spot = Vector3.zero;
            string[] parts = text.Split(',');
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.NumberStyles style = System.Globalization.NumberStyles.Float;
            float x, y, z;
            if (parts.Length != 3
                || !float.TryParse(parts[0], style, inv, out x)
                || !float.TryParse(parts[1], style, inv, out y)
                || !float.TryParse(parts[2], style, inv, out z))
                return false;
            spot = new Vector3(x, y, z);
            return true;
        }

        static Transform ResolveTarget(SceneEventType type, string objectPath) {
            bool settled;
            return ResolveTarget(type, objectPath, out settled);
        }

        static Transform ResolveTarget(SceneEventType type, string objectPath, out bool settled) {
            settled = false;
            if (string.IsNullOrEmpty(objectPath))
                return null;

            int mark = objectPath.LastIndexOf(SpotMark, StringComparison.Ordinal);
            Vector3 spot;
            if (mark < 0 || !TryParseSpot(objectPath.Substring(mark + SpotMark.Length), out spot))
                return objectPath.GetFromFullPath();

            string path = objectPath.Substring(0, mark);
            Transform named = path.GetFromFullPath();

            // Pooled ("#id") and spawned ones are addressed exactly already.
            if (named) {
                Vector3 namedHome;
                if (!BreakPieces.TryGetHome(named, out namedHome))
                    return named;
                if ((namedHome - spot).sqrMagnitude <= BreakPieces.HomeMatch * BreakPieces.HomeMatch)
                    return named;
            }

            string[] parts = path.Split('/');
            if (parts.Length >= 3) {
                string name = parts[parts.Length - 1];
                Transform parent = string.Join("/", parts, 0, parts.Length - 2).GetFromFullPath();
                if (parent) {
                    Transform best = null;
                    float bestDistance = BreakPieces.HomeMatch * BreakPieces.HomeMatch;
                    for (int i = 0; i < parent.childCount; i++) {
                        Transform child = parent.GetChild(i);
                        if (!child || child.name != name)
                            continue;
                        Vector3 home;
                        if (!BreakPieces.TryGetHome(child, out home))
                            continue;
                        float distance = (home - spot).sqrMagnitude;
                        if (distance <= bestDistance) {
                            bestDistance = distance;
                            best = child;
                        }
                    }
                    if (best)
                        return best;
                }
            }

            if (type == SceneEventType.ObjectBreak && BreakPieces.BrokeAt(spot)) {
                settled = true;
                return null;
            }

            // The path landed on a different placed object.
            if (named)
                settled = true;
            return null;
        }

        const float RetryWindow = 10f;
        const float RetryStep = 0.25f;
        static int unresolved;

        /// <summary>Events still waiting for their object to exist here, for the state log.</summary>
        public static int RetriesPending { get; private set; }

        static System.Collections.IEnumerator RetryApply(SceneEventType type, ushort arg, string objectPath, bool replay, bool record) {
            RetriesPending++;
            try {
                IEnumerator inner = RetryApplyInner(type, arg, objectPath, replay, record);
                while (inner.MoveNext())
                    yield return inner.Current;
            }
            finally { RetriesPending--; }
        }

        static System.Collections.IEnumerator RetryApplyInner(SceneEventType type, ushort arg, string objectPath, bool replay, bool record) {
            ushort scene = CurrentSceneTag();
            float waited = 0f;

            while (waited < RetryWindow) {
                float wait = 0f;
                while (wait < RetryStep) {
                    yield return null;
                    if (!SceneLoader.loading)
                        wait += Time.unscaledDeltaTime;
                }
                waited += wait;

                if (CurrentSceneTag() != scene)
                    yield break;
                bool settled;
                if (!ResolveTarget(type, objectPath, out settled) && !settled)
                    continue;

                ApplyRemoteEvent(type, arg, objectPath, replay, record);
                yield break;
            }

            unresolved++;
            if (unresolved <= 20 || unresolved % 100 == 0)
                EntangleLogger.Warn($"Scene event {type} could not resolve path after retries: {objectPath}");
        }

        internal static void ApplyNpcEvent(SceneEventType type, ushort pooleeId, string objectPath) {
            // Its copy is still being built here; NpcPending applies this once it is.
            if (type == SceneEventType.NpcDeath && NpcPending.HoldDeath(pooleeId))
                return;
            // A body still being built here is going away: the same as a despawn.
            if ((type == SceneEventType.NpcDespawn || type == SceneEventType.NpcFizzle) && NpcPending.HoldDespawn(pooleeId))
                return;

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
                        KillCopy(root);
                        break;
                    }
                    case SceneEventType.NpcFizzle: {
                        // Already gone here (its zone put it away, or its own fizzle ran): nothing to show.
                        if (!root.gameObject.activeInHierarchy || CorpseCleanup.IsFizzling(root))
                            return;
                        KillCopy(root);
                        CorpseCleanup.FizzleNow(root, true);
                        break;
                    }
                    case SceneEventType.NpcDespawn: {
                        if (!root.gameObject.activeInHierarchy) return;
                        if (CorpseCleanup.IsFizzling(root)) return;

                        // A body that is dead here goes with a fizzle, not a pop. A living one was
                        // put away by its zone, as on the host, and simply goes.
                        if (DeadCopy(root) && CorpseCleanup.FizzleNow(root, true))
                            return;

                        Poolee rootPoolee = root.GetComponent<Poolee>();
                        if (!rootPoolee) return;

                        Patching.AIBrainDespawnPatch.isRemoteDespawn = true;
                        try { PooleeSyncable.DespawnPoolee(rootPoolee, true); }
                        finally { Patching.AIBrainDespawnPatch.isRemoteDespawn = false; }

                        break;
                    }
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn($"Failed to apply NPC event {type} for poolee {pooleeId}: {e.Message}");
            }
        }

        static bool DeadCopy(Transform root) {
            try {
                StressLevelZero.AI.AIBrain brain = root.GetComponentInChildren<StressLevelZero.AI.AIBrain>(true);
                return brain && NpcStateSync.IsDead(brain);
            }
            catch { return false; }
        }

        // Another player's NPC died: the same death here, told to no one.
        static void KillCopy(Transform root) {
            if (!root)
                return;

            // Even a body that was already dead here has to remember the host said so,
            // or a later pool restart stands a scene corpse back up.
            PooleeSyncable synced = FindPooleeSyncable(root);
            if (synced)
                synced.NoteDeclaredDeath();

            StressLevelZero.AI.AIBrain brain = root.GetComponentInChildren<StressLevelZero.AI.AIBrain>(true);
            if (!brain || brain.isDead) return;

            synced = FindPooleeSyncable(root);
            if (synced)
                synced.BeforeRemoteDeath();

            Patching.AIBrainDeathPatch.isRemoteDeath = true;
            try {
                brain.OnDeath();

                if (brain.puppetMaster)
                    brain.puppetMaster.Kill();
            }
            finally { Patching.AIBrainDeathPatch.isRemoteDeath = false; }
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

        static readonly HashSet<int> turretButtons = new HashSet<int>();

        public static void ClearTurretButtons() => turretButtons.Clear();

        static readonly Dictionary<int, float> remoteHeld = new Dictionary<int, float>();
        const float MaxRemoteHold = 10f;

        public static void ClearRemoteHolds() {
            remoteHeld.Clear();
            unsentPress.Clear();
            unsentPressAt.Clear();
            remoteReleasedAt.Clear();
        }

        // The same press can reach a button twice: pushed here by something that follows another
        // player, and as their press event. Either way round it counts once.
        const float EchoWindow = 1.5f;
        static readonly Dictionary<int, float> unsentPressAt = new Dictionary<int, float>();
        static readonly Dictionary<int, float> remoteReleasedAt = new Dictionary<int, float>();

        /// <summary>Pressed here a moment ago by something other than us: that was this press.</summary>
        public static bool PressedByOtherJustNow(ButtonToggle button) {
            float at;
            return unsentPressAt.TryGetValue(button.GetInstanceID(), out at) && Time.realtimeSinceStartup - at < EchoWindow;
        }

        // Presses nobody here made (a level script, another player's rep) happen on every machine
        // by themselves, so sending them would toggle the button twice everywhere.
        static readonly HashSet<int> unsentPress = new HashSet<int>();
        static readonly UnhollowerBaseLib.Il2CppReferenceArray<Collider> touchBuffer = new UnhollowerBaseLib.Il2CppReferenceArray<Collider>(48);

        static bool PressedHere(ButtonToggle button) {
            try {
                StressLevelZero.Rig.RigManager rig = Entanglement.Data.PlayerScripts.playerRig;
                if (!rig)
                    return true;

                Vector3 centre = button.transform.position;
                float radius = 0.3f;
                Collider shaft = button.buttonShaftCollider;
                if (shaft) {
                    Bounds bounds = shaft.bounds;
                    centre = bounds.center;
                    radius = bounds.extents.magnitude + 0.1f;
                }

                int count = Physics.OverlapSphereNonAlloc(centre, radius, touchBuffer);
                Transform rigRoot = rig.transform;
                int left = HeldId(Entanglement.Data.PlayerScripts.playerLeftHand);
                int right = HeldId(Entanglement.Data.PlayerScripts.playerRightHand);

                for (int i = 0; i < count; i++) {
                    Collider other = touchBuffer[i];
                    if (!other)
                        continue;
                    if (other.transform.IsChildOf(rigRoot))
                        return true;

                    Rigidbody body = other.attachedRigidbody;
                    if (body == null)
                        continue;
                    int bodyId = body.GetInstanceID();
                    if (bodyId == left || bodyId == right)
                        return true;

                    TransformSyncable sync = TransformSyncable.cache.Get(body.gameObject);
                    if (sync && sync.IsOwner())
                        return true;
                }
                return false;
            }
            catch { return true; }
        }

        static int HeldId(StressLevelZero.Interaction.Hand hand) {
            try {
                if (!hand || !hand.m_CurrentAttachedObject)
                    return 0;
                Grip grip = Grip.Cache.Get(hand.m_CurrentAttachedObject);
                return grip && grip.host && grip.host.rb ? grip.host.rb.GetInstanceID() : 0;
            }
            catch { return 0; }
        }

        public static int RemoteHeldCount => remoteHeld.Count;

        public static bool IsRemoteHeld(ButtonToggle button) => remoteHeld.ContainsKey(button.GetInstanceID());

        public static void HoldForRemote(ButtonToggle button) {
            int id = button.GetInstanceID();
            remoteHeld[id] = Time.realtimeSinceStartup + MaxRemoteHold;
            // Seen as it is, so the change isn't taken for a press here and sent back.
            pressedStates[id] = true;
        }

        public static void ReleaseRemote(ButtonToggle button) {
            int id = button.GetInstanceID();
            remoteHeld.Remove(id);
            pressedStates[id] = false;
            remoteReleasedAt[id] = Time.realtimeSinceStartup;
        }

        // Its own Update is left out while someone else holds it down here.
        public static bool Prefix(ButtonToggle __instance) {
            if (remoteHeld.Count == 0 && remoteReleasedAt.Count == 0)
                return true;
            try {
                int id = __instance.GetInstanceID();
                float until;
                if (!remoteHeld.TryGetValue(id, out until))
                    return AfterRemoteRelease(__instance, id);
                if (Time.realtimeSinceStartup < until) {
                    if (!__instance._isPressed)
                        __instance._isPressed = true;
                    return false;
                }

                // Their release never came (they left mid-press): let go here, not sent.
                EntangleLogger.Debug("[Button] " + __instance.name + ": no release after " + MaxRemoteHold + " s, letting it go here");
                remoteHeld.Remove(id);
                pressedStates[id] = false;
                __instance._isPressed = false;
                SceneEventSync.InvokeButtonEvent(__instance.onDepress, __instance, "onDepress");
                return true;
            }
            catch { return true; }
        }

        // Just let go of by another player: whatever of theirs still touches it here is them on the way
        // out, not a new press. Our own hands still press it.
        static bool AfterRemoteRelease(ButtonToggle button, int id) {
            float released;
            if (!remoteReleasedAt.TryGetValue(id, out released))
                return true;
            if (Time.realtimeSinceStartup - released >= EchoWindow) {
                remoteReleasedAt.Remove(id);
                return true;
            }
            return button._isPressed || PressedHere(button);
        }

        public static bool IsTurretButton(ButtonToggle button) {
            try { return button.GetComponentInParent<StressLevelZero.AI.EnemyTurret>() != null; }
            catch { return false; }
        }

        public static void Postfix(ButtonToggle __instance) {
            try {
                if (!SteamIntegration.hasLobby)
                    return;

                bool pressed = __instance._isPressed;
                int id = __instance.GetInstanceID();

                if (turretButtons.Contains(id))
                    return;

                if (!pressedStates.TryGetValue(id, out bool wasPressed)) {
                    if (IsTurretButton(__instance)) {
                        turretButtons.Add(id);
                        return;
                    }
                    pressedStates[id] = pressed;
                    return;
                }

                if (pressed == wasPressed)
                    return;

                pressedStates[id] = pressed;

                if (pressed) {
                    if (!PressedHere(__instance)) {
                        unsentPress.Add(id);
                        unsentPressAt[id] = Time.realtimeSinceStartup;
                        EntangleLogger.Debug("[Button] " + __instance.name + ": pressed by something other than us (level script or another player), not sent");
                        return;
                    }
                    unsentPress.Remove(id);
                }
                else if (unsentPress.Remove(id)) {
                    return;
                }

                SceneEventType type = pressed ? SceneEventType.ButtonPress : SceneEventType.ButtonDepress;
                SceneEventSync.SendEvent(type, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PuzzlePatches.Postfix", e); }
        }
    }

    // Fires when a key is fully seated and locked into its receiver
    [HarmonyPatch(typeof(KeyReciever), "OnMagazineLocked")]
    public static class KeyLockPatch
    {
        public static void Postfix(KeyReciever __instance) {
            try {
                if (!SteamIntegration.hasLobby)
                    return;

                if (SceneEventSync.MarkEvent(SceneEventSync.EventKey(__instance.GetInstanceID(), SceneEventType.KeyLock)))
                    SceneEventSync.SendEvent(SceneEventType.KeyLock, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PuzzlePatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(KeyReciever), "OnMagazineUnlocked")]
    public static class KeyUnlockPatch
    {
        public static void Postfix(KeyReciever __instance) {
            try {
                if (!SteamIntegration.hasLobby)
                    return;

                if (SceneEventSync.MarkEvent(SceneEventSync.EventKey(__instance.GetInstanceID(), SceneEventType.KeyUnlock)))
                    SceneEventSync.SendEvent(SceneEventType.KeyUnlock, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PuzzlePatches.Postfix", e); }
        }
    }

    // Pull box handles fire physically only on the puller's client, detect the pull and replicate it
    [HarmonyPatch(typeof(PullDevice), "OnGripAttachedUpdate")]
    public static class PullDevicePatch
    {
        public static Dictionary<int, bool> pulledStates = new Dictionary<int, bool>();

        public static void Postfix(PullDevice __instance) {
            try {
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
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PuzzlePatches.Postfix", e); }
        }
    }

    // NPC deaths converge on whichever simulation killed it first, everyone else applies the same death
    [HarmonyPatch(typeof(StressLevelZero.AI.AIBrain), "OnDeath")]
    public static class AIBrainDeathPatch
    {
        public static bool isRemoteDeath = false;

        public static void Postfix(StressLevelZero.AI.AIBrain __instance) {
            try { CorpseCleanup.Register(__instance); }
            catch { }

            try {
                if (!SteamIntegration.hasLobby)
                    return;

                // Someone else's death arriving here: nothing to tell anyone.
                if (isRemoteDeath) {
                    CorpseCleanup.NoteDeathSent(__instance);
                    return;
                }

                if (!SceneEventSync.MarkEvent(SceneEventSync.EventKey(__instance.GetInstanceID(), SceneEventType.NpcDeath)))
                    return;

                PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(__instance.transform);

                if (poolee == null && __instance.GetComponentInParent<StressLevelZero.Pool.Poolee>())
                    return;

                SceneEventSync.SendEvent(SceneEventType.NpcDeath, __instance.transform, poolee ? poolee.id : (ushort)0);
                CorpseCleanup.NoteDeathSent(__instance);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PuzzlePatches.Postfix", e); }
        }
    }

    public static class AIBrainDespawnPatch
    {
        public static bool isRemoteDespawn = false;
    }

    [HarmonyPatch(typeof(Control_MonoMat), "InsertMagazine")]
    public static class MonoMatInsertPatch
    {
        public static bool isRemoteInsert = false;

        public static void Prefix(Control_MonoMat __instance, out long __state) {
            __state = 0L;
            try {
                if (__instance)
                    __state = ((long)__instance._mediumBullets << 32) | (uint)__instance._smallBullets;
            }
            catch { }
        }

        public static void Postfix(Control_MonoMat __instance, StressLevelZero.Props.Weapons.Magazine magazine, long __state) {
            try {
                if (!SteamIntegration.hasLobby || isRemoteInsert)
                    return;

                ushort magId = 0;
                if (magazine) {
                    TransformSyncable magSync = TransformSyncable.cache.Get(magazine.gameObject);
                    if (magSync && magSync.isValid)
                        magId = magSync.objectId;
                }

                if (magId == 0)
                    EntangleLogger.Warn("A magazine was inserted into a MonoMat but wasn't synced, other players won't see it go in!");

                SceneEventSync.SendEvent(SceneEventType.MonoMatInsert, __instance.transform, magId, false);

                if (!Node.isServer) {
                    int small = __instance._smallBullets - (int)(uint)(__state & 0xFFFFFFFF);
                    int medium = __instance._mediumBullets - (int)(__state >> 32);
                    MonoMatSync.SendDeposit(__instance, small, true);
                    MonoMatSync.SendDeposit(__instance, medium, false);
                }
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PuzzlePatches.Postfix", e); }
        }
    }
}
