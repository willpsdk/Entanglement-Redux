using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using HarmonyLib;

using MelonLoader;

using Entanglement.Network;

using UnityEngine.EventSystems;

using StressLevelZero.Arena;

namespace Entanglement.Patching
{
    public static class FantasyArena_Settings {
        public static bool m_invalidSettings = false;

        public static void SendEnemyCount(bool isLow) {
            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.FantasyCount, new FantasyEnemyCountMessageData() {
                isLow = isLow,
            });

            byte[] msgBytes = message.GetBytes();
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msgBytes);
        }

        public static void SendDifficulty(byte difficulty) {
            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.FantasyDiff, new FantasyDifficultyMessageData() {
                difficulty = difficulty,
            });

            byte[] msgBytes = message.GetBytes();
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msgBytes);
        }
    }

    [HarmonyPatch(typeof(UIHapticHoverArena), "OnPointerEnter")]
    public static class ChallengePatch {
        public static void Postfix(UIHapticHoverArena __instance, PointerEventData eventData) {
            try {
                if (!__instance.arenaUIControl || !__instance.challenge) return;

                if (__instance.arenaUIControl.activeChallenge == __instance.challenge) {
                    Arena_Challenge challenge = __instance.challenge;

    #if DEBUG
                    EntangleLogger.Log($"Set challenge to {challenge.name}!");
    #endif

                    // Disgusting linq stuff but it works
                    byte index = (byte)Arena_GameManager.instance.masterChallengeList.ToArray().ToList().FindIndex(o => o == challenge);

                    NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.FantasyChal, new FantasyChallengeMessageData() {
                        index = index,
                    });

                    byte[] msgBytes = message.GetBytes();
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msgBytes);
                }
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("FantasyArenaPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Control_UI_Arena), "SetEasyDifficulty")]
    public static class EasyDifficultyPatch {
        public static void Postfix() {
            if (!FantasyArena_Settings.m_invalidSettings) {
#if DEBUG
                EntangleLogger.Log($"Set easy difficulty!");
#endif

                FantasyArena_Settings.SendDifficulty(0);
            }
            FantasyArena_Settings.m_invalidSettings = false;
        }
    }

    [HarmonyPatch(typeof(Control_UI_Arena), "SetMediumDifficulty")]
    public static class MediumDifficultyPatch
    {
        public static void Postfix() {
            if (!FantasyArena_Settings.m_invalidSettings) {
#if DEBUG
                EntangleLogger.Log($"Set medium difficulty!");
#endif

                FantasyArena_Settings.SendDifficulty(1);
            }
            FantasyArena_Settings.m_invalidSettings = false;
        }
    }

    [HarmonyPatch(typeof(Control_UI_Arena), "SetHardDifficulty")]
    public static class HardDifficultyPatch {
        public static void Postfix() {
            if (!FantasyArena_Settings.m_invalidSettings) {
#if DEBUG
                EntangleLogger.Log($"Set hard difficulty!");
#endif

                FantasyArena_Settings.SendDifficulty(2);
            }
            FantasyArena_Settings.m_invalidSettings = false;
        }
    }

    [HarmonyPatch(typeof(Control_UI_Arena), "ToggleEnemyCount")]
    public static class ToggleEnemyCountPatch {
        public static void Postfix(Control_UI_Arena __instance) {
            try {
                bool isLow = __instance.arenaStats.arenaDataPlayer.playerStats.isLowEnemyCount;

    #if DEBUG
                EntangleLogger.Log($"Toggling Enemy Count to {(isLow ? "LOW" : "HIGH")}!");
    #endif

                FantasyArena_Settings.SendEnemyCount(isLow);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("FantasyArenaPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Control_UI_Arena), "OnLoadSaveFile")]
    public static class LoadSaveFilePatch {
        public static void Postfix(Control_UI_Arena __instance) {
            try {
                if (Server.instance != null) {
                    bool isLow = __instance.arenaStats.arenaDataPlayer.playerStats.isLowEnemyCount;

    #if DEBUG
                    EntangleLogger.Log($"Loading saved enemy count of {(isLow ? "LOW" : "HIGH")}!");
    #endif

                    FantasyArena_Settings.SendEnemyCount(isLow);
                }
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("FantasyArenaPatches.Postfix", e); }
        }
    }
}

namespace Entanglement.Patching
{
    using Entanglement.Objects;

    // The Dungeon's NPC that copies the player's movements: Boneworks.TransformMirror copies a target
    // skeleton onto its own bones every frame. On each machine that target is the local player, and the
    // NPC is also followed bone by bone from the host, so the two fought. On clients it stands down and
    // the host's NPC is copied, as the host's own copy mirrors the host.
    [HarmonyPatch(typeof(Boneworks.TransformMirror), "Update")]
    public static class TransformMirrorPatch
    {
        static readonly System.Collections.Generic.Dictionary<int, bool> followed = new System.Collections.Generic.Dictionary<int, bool>();

        public static void OnSceneCleanup() => followed.Clear();

        public static bool Prefix(Boneworks.TransformMirror __instance) {
            try {
                if (!SteamIntegration.hasLobby || Node.isServer || Node.activeNode == null)
                    return true;

                int key = __instance.GetInstanceID();
                bool follows;
                if (!followed.TryGetValue(key, out follows) || !follows) {
                    // Only once the host's NPC is the one driving these bones; until then (or if it
                    // never is) the script keeps running so the NPC is not left standing still.
                    follows = PooleeSyncable.SyncedNpcOf(__instance.transform) != null;
                    if (follows) {
                        followed[key] = true;
                        EntangleLogger.Debug("[Mirror] " + __instance.gameObject.name + " follows the host now; its own mirroring is off here");
                    }
                }
                return !follows;
            }
            catch { return true; }
        }
    }

    // The lift that rises when a player steps on it: its trigger sees only the local player on each
    // machine, never a rep, so the car can move on one machine and not another. How the car is moved
    // is not in the game stubs, so for now this only writes down, in the debug log, who stepped on
    // which lift and what state its controller is in, so the next test shows what to sync.
    [HarmonyPatch(typeof(elevator_trigger), "OnTriggerEnter")]
    public static class ElevatorTriggerEnterPatch
    {
        public static void Postfix(elevator_trigger __instance, UnityEngine.Collider other) => ElevatorTriggerLog.Note(__instance, other, true);
    }

    [HarmonyPatch(typeof(elevator_trigger), "OnTriggerExit")]
    public static class ElevatorTriggerExitPatch
    {
        public static void Postfix(elevator_trigger __instance, UnityEngine.Collider other) => ElevatorTriggerLog.Note(__instance, other, false);
    }

    public static class ElevatorTriggerLog
    {
        static float nextLog;

        public static void Note(elevator_trigger trigger, UnityEngine.Collider other, bool enter) {
            try {
                if (!EntangleLogger.verbose || !SteamIntegration.hasLobby || !trigger)
                    return;
                if (UnityEngine.Time.realtimeSinceStartup < nextLog)
                    return;
                nextLog = UnityEngine.Time.realtimeSinceStartup + 0.5f;

                elevator_controller controller = trigger.GetComponentInParent<elevator_controller>();
                EntangleLogger.Debug("[Lift] " + (enter ? "enter " : "exit ") + trigger.transform.GetFullPath() + " by " + (other ? other.name : "?")
                    + (controller ? ", controller CarUp=" + controller.CarUp : ", no controller above it")
                    + ", newParent=" + (trigger.newParent ? trigger.newParent.name : "none"));
            }
            catch { }
        }
    }
}
