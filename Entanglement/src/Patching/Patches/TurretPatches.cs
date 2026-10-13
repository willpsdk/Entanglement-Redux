using System;

using HarmonyLib;

using UnityEngine;

using StressLevelZero.AI;

using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    public static class TurretEventSync
    {
        public static bool isRemote;

        static int unaddressed;
        static int stateLogs;

        static void Send(SceneEventType type, EnemyTurret turret, ushort arg, bool record) {
            if (isRemote || !SteamIntegration.hasLobby || turret == null)
                return;

            try {
                PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(turret.transform);
                if ((poolee == null || poolee.id == 0) && Node.isServer) {
                    // Scene turrets that spawn / wake after the lobby scan often have no id yet.
                    TurretSync.EnsureRegistered(turret);
                    poolee = SceneEventSync.FindPooleeSyncable(turret.transform);
                }

                if (poolee == null || poolee.id == 0) {
                    if (!Node.isServer)
                        return;
                    unaddressed++;
                    if (unaddressed <= 5 || unaddressed % 200 == 0)
                        EntangleLogger.Warn("[Turret] " + type + " on " + turret.name
                            + " has no spawn id, so it was not sent (" + unaddressed + " so far)");
                    return;
                }

                // The machine running its AI speaks for it (TurretSync.TickAuthority). A kill comes
                // from wherever the shots that finished it landed, and goes to everyone from there.
                if (type != SceneEventType.TurretKill && !poolee.RunsHere)
                    return;

                // A pooled turret just respawned and not yet placed: its bind sends its state.
                if (string.IsNullOrEmpty(poolee.placeKey) && TurretSync.IsPooled(poolee.gameObject))
                    return;

                SceneEventSync.SendEvent(type, poolee.transform, arg, record);
            }
            catch { }
        }

        public static void SendState(EnemyTurret turret, EnemyTurret.TurretStates state) {
            TurretSight.Describe(turret);
            if (!isRemote && SteamIntegration.hasLobby && stateLogs < 40) {
                stateLogs++;
                try { EntangleLogger.Debug("[Turret] " + turret.name + " -> " + state); }
                catch { }
            }

            Send(SceneEventType.TurretState, turret, (ushort)state, false);
        }

        public static void SendCurrentState(GameObject root) {
            if (!root)
                return;
            try {
                EnemyTurret turret = root.GetComponentInChildren<EnemyTurret>(true);
                if (turret)
                    Send(SceneEventType.TurretState, turret, (ushort)turret.state, false);
            }
            catch { }
        }

        public static void SendFire(EnemyTurret turret) {
            Send(SceneEventType.TurretFire, turret, 0, false);
        }

        public static void SendKill(EnemyTurret turret) {
            Send(SceneEventType.TurretKill, turret, 0, true);
        }

        /// <summary>Applies a turret event from the machine that runs it.</summary>
        public static bool Apply(SceneEventType type, ushort arg, Transform target) {
            if (!target)
                return false;

            EnemyTurret turret = null;
            try { turret = target.GetComponentInChildren<EnemyTurret>(true); }
            catch { return false; }

            if (turret == null)
                return false;

            // Its AI runs here now: what it does is decided here. A kill still counts.
            if (type != SceneEventType.TurretKill) {
                PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(turret.transform);
                if (poolee != null && poolee.id != 0 && poolee.CurrentOwner() == SteamIntegration.currentUserId)
                    return true;
            }

            isRemote = true;
            try {
                switch (type) {
                    case SceneEventType.TurretState:
                        turret.SetState((EnemyTurret.TurretStates)arg);
                        return true;
                    case SceneEventType.TurretFire:
                        turret.TriggerFire();
                        return true;
                    case SceneEventType.TurretKill:
                        turret.Kill();
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Turret] Could not apply " + type + ": " + e.Message);
                return false;
            }
            finally {
                isRemote = false;
            }
        }
    }

    [HarmonyPatch(typeof(EnemyTurret), "SetState")]
    public static class TurretSetStatePatch
    {
        public static void Postfix(EnemyTurret __instance, EnemyTurret.TurretStates nextState) {
            try { TurretEventSync.SendState(__instance, nextState); }
            catch (Exception e) { PatchGuard.Report("TurretPatches.SetState", e); }
        }
    }

    public static class TurretSight
    {
        static int logs;
        static readonly System.Collections.Generic.HashSet<int> described = new System.Collections.Generic.HashSet<int>();

        public static void Describe(EnemyTurret turret) {
            try {
                if (!turret || !described.Add(turret.GetInstanceID()))
                    return;
                AITrigger trigger = turret.trigger;
                EntangleLogger.Debug("[TurretSight] " + turret.name
                    + " enabled=" + turret.enabled
                    + " trigger=" + (trigger ? (trigger.enabled ? "on" : "OFF") : "MISSING")
                    + (trigger ? " triggerRadius=" + trigger.radius + " fov=" + trigger.fov : "")
                    + " visionRadius=" + turret.visionRadius + " visionAngle=" + turret.visionAngle);
            }
            catch (Exception e) { EntangleLogger.Warn("[TurretSight] describe failed: " + e.Message); }
        }

        public static void Saw(EnemyTurret turret, AITarget target, bool entered) {
            if (logs >= 60)
                return;
            logs++;
            try {
                string who = "unknown";
                if (target) {
                    Transform root = target.transform.root;
                    who = root ? root.name : target.name;
                    if (Entanglement.Data.PlayerScripts.playerRig && target.transform.IsChildOf(Entanglement.Data.PlayerScripts.playerRig.transform))
                        who = "LOCAL PLAYER (" + who + ")";
                }
                EntangleLogger.Debug("[TurretSight] " + turret.name + (entered ? " sees " : " lost ") + who
                    + " state=" + turret.state);
            }
            catch { }
        }

        public static void OnSceneCleanup() {
            described.Clear();
            logs = 0;
        }
    }

    [HarmonyPatch(typeof(EnemyTurret), "OnAITriggerEnter")]
    public static class TurretSawPatch
    {
        public static void Postfix(EnemyTurret __instance, AITarget target) {
            try { TurretSight.Saw(__instance, target, true); }
            catch (Exception e) { PatchGuard.Report("TurretPatches.OnAITriggerEnter", e); }
        }
    }

    [HarmonyPatch(typeof(EnemyTurret), "OnAITriggerExit")]
    public static class TurretLostPatch
    {
        public static bool Prefix(EnemyTurret __instance, AITarget target) {
            try {
                if (!__instance || !target)
                    return true;

                float maxR = __instance.visionRadius;
                try {
                    if (__instance.trigger)
                        maxR = Mathf.Max(maxR, __instance.trigger.radius);
                }
                catch { }

                maxR += 1f;
                Vector3 delta = target.transform.position - __instance.transform.position;
                if (delta.sqrMagnitude <= maxR * maxR)
                    return false;
            }
            catch { }

            return true;
        }

        public static void Postfix(EnemyTurret __instance, AITarget target) {
            try { TurretSight.Saw(__instance, target, false); }
            catch (Exception e) { PatchGuard.Report("TurretPatches.OnAITriggerExit", e); }
        }
    }

    [HarmonyPatch(typeof(EnemyTurret), "TriggerFire")]
    public static class TurretFirePatch
    {
        public static void Postfix(EnemyTurret __instance) {
            try { TurretEventSync.SendFire(__instance); }
            catch (Exception e) { PatchGuard.Report("TurretPatches.TriggerFire", e); }
        }
    }

    [HarmonyPatch(typeof(EnemyTurret), "Kill")]
    public static class TurretKillPatch
    {
        public static void Postfix(EnemyTurret __instance) {
            try { TurretEventSync.SendKill(__instance); }
            catch (Exception e) { PatchGuard.Report("TurretPatches.Kill", e); }
        }
    }
}
