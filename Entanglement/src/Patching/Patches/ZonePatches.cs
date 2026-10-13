using HarmonyLib;

using System;

using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.Data;
using Entanglement.Objects;

using StressLevelZero.Zones;

using UnityEngine;

using System.Collections.Generic;

namespace Entanglement.Patching
{
    public static class ZoneTrackingUtilities {
        static Dictionary<SceneZone, HashSet<int>> localInside = new Dictionary<SceneZone, HashSet<int>>(new UnityComparer());
        static Dictionary<SceneZone, HashSet<int>> probesInside = new Dictionary<SceneZone, HashSet<int>>(new UnityComparer());

        public static void OnSceneCleanup() {
            try { localInside.Clear(); }
            catch { localInside = new Dictionary<SceneZone, HashSet<int>>(new UnityComparer()); }

            try { probesInside.Clear(); }
            catch { probesInside = new Dictionary<SceneZone, HashSet<int>>(new UnityComparer()); }

            localZones.Clear();
            forcing = false;
        }

        public static bool forcing;

        class LocalZone {
            public SceneZone zone;
            public int id;
            public Collider collider;
        }

        static readonly List<LocalZone> localZones = new List<LocalZone>();

        static void NoteLocal(SceneZone zone, Collider other, bool inside) {
            int id;
            try { id = zone.GetInstanceID(); }
            catch { return; }
            for (int i = localZones.Count - 1; i >= 0; i--) {
                if (localZones[i].id == id)
                    localZones.RemoveAt(i);
            }
            if (inside)
                localZones.Add(new LocalZone { zone = zone, id = id, collider = other });
        }

        /// <summary>Zones our own player is in, most recent last, each with one of our colliders in it.</summary>
        public static List<KeyValuePair<SceneZone, Collider>> LocalZones() {
            var list = new List<KeyValuePair<SceneZone, Collider>>(localZones.Count);
            for (int i = 0; i < localZones.Count; i++)
                list.Add(new KeyValuePair<SceneZone, Collider>(localZones[i].zone, localZones[i].collider));
            return list;
        }

        static HashSet<int> Set(Dictionary<SceneZone, HashSet<int>> map, SceneZone zone) {
            HashSet<int> set;
            if (!map.TryGetValue(zone, out set)) {
                set = new HashSet<int>();
                map[zone] = set;
            }
            return set;
        }

        public static int LocalCount(SceneZone zone) {
            HashSet<int> set;
            return localInside.TryGetValue(zone, out set) ? set.Count : 0;
        }

        public static int ProbeCount(SceneZone zone) {
            HashSet<int> set;
            return probesInside.TryGetValue(zone, out set) ? set.Count : 0;
        }

        public static void LocalEntered(SceneZone zone, Collider other) {
            Set(localInside, zone).Add(other.GetInstanceID());
            NoteLocal(zone, other, true);
        }

        public static void LocalLeft(SceneZone zone, Collider other) {
            HashSet<int> set = Set(localInside, zone);
            set.Remove(other.GetInstanceID());
            if (set.Count == 0)
                NoteLocal(zone, other, false);
        }
        public static void ProbeEntered(SceneZone zone, Collider other) => Set(probesInside, zone).Add(other.GetInstanceID());
        public static void ProbeLeft(SceneZone zone, Collider other) => Set(probesInside, zone).Remove(other.GetInstanceID());

        // Remote player reps carry colliders of their own; they never drive zones physically.
        public static bool IsRemoteRep(Collider other) {
            Transform root = other ? other.transform.root : null;
            return root && root.name.Contains("PlayerRep");
        }

        public static bool IsProbe(Collider other) {
            Transform root = other ? other.transform.root : null;
            return root && root.name.StartsWith("ZoneProbe.");
        }
    }

    [HarmonyPatch(typeof(SceneZone), "OnTriggerEnter")]
    public static class ZoneEnterPatch
    {
        public static bool Prefix(SceneZone __instance, Collider other) {
            if (!SteamIntegration.hasLobby)
                return true;

            try {
                if (ZoneTrackingUtilities.IsRemoteRep(other))
                    return false;

                if (ZoneTrackingUtilities.IsProbe(other)) {
                    // Someone is already in here, so the zone is already on.
                    bool occupied = ZoneTrackingUtilities.LocalCount(__instance) > 0 || ZoneTrackingUtilities.ProbeCount(__instance) > 0;
                    ZoneTrackingUtilities.ProbeEntered(__instance, other);
                    return ZoneTrackingUtilities.forcing || !occupied;
                }

                if (other.CompareTag("Player"))
                    ZoneTrackingUtilities.LocalEntered(__instance, other);
            }
            catch { }

            return true;
        }
    }

    [HarmonyPatch(typeof(SceneZone), "OnTriggerExit")]
    public static class ZoneExitPatch
    {
        public static bool Prefix(SceneZone __instance, Collider other) {
            if (!SteamIntegration.hasLobby)
                return true;

            try {
                if (ZoneTrackingUtilities.IsRemoteRep(other))
                    return false;

                if (ZoneTrackingUtilities.IsProbe(other)) {
                    ZoneTrackingUtilities.ProbeLeft(__instance, other);
                    return ZoneTrackingUtilities.LocalCount(__instance) == 0 && ZoneTrackingUtilities.ProbeCount(__instance) == 0;
                }

                if (other.CompareTag("Player")) {
                    ZoneTrackingUtilities.LocalLeft(__instance, other);
                    // Another player is still in here: keep it on for them.
                    if (ZoneTrackingUtilities.LocalCount(__instance) == 0 && ZoneTrackingUtilities.ProbeCount(__instance) > 0)
                        return false;
                }
            }
            catch { }

            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerTrigger), "OnTriggerEnter")]
    public static class PlayerTriggerEnterPatch
    {
        public static bool Prefix(Collider other) {
            if (!SteamIntegration.hasLobby)
                return true;
            try { return !ZoneTrackingUtilities.IsRemoteRep(other) && !ZoneTrackingUtilities.IsProbe(other); }
            catch { return true; }
        }

        public static void Postfix(PlayerTrigger __instance, Collider other) {
            try { TriggerRelay.Local(__instance, other, __instance.TriggerLayerMask.value, true, SceneEventType.PlayerTriggerEnter, SceneEventType.PlayerTriggerExit); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PlayerTrigger), "OnTriggerExit")]
    public static class PlayerTriggerExitPatch
    {
        public static bool Prefix(Collider other) {
            if (!SteamIntegration.hasLobby)
                return true;
            try { return !ZoneTrackingUtilities.IsRemoteRep(other) && !ZoneTrackingUtilities.IsProbe(other); }
            catch { return true; }
        }

        public static void Postfix(PlayerTrigger __instance, Collider other) {
            try { TriggerRelay.Local(__instance, other, __instance.TriggerLayerMask.value, false, SceneEventType.PlayerTriggerEnter, SceneEventType.PlayerTriggerExit); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TriggerLasers), "OnTriggerEnter")]
    public static class TriggerLasersEnterPatch
    {
        public static void Postfix(TriggerLasers __instance, Collider other) {
            try { TriggerRelay.Local(__instance, other, __instance.LayerFilter.value, true, SceneEventType.LaserTriggerEnter, SceneEventType.LaserTriggerExit); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TriggerLasers), "OnTriggerExit")]
    public static class TriggerLasersExitPatch
    {
        public static void Postfix(TriggerLasers __instance, Collider other) {
            try { TriggerRelay.Local(__instance, other, __instance.LayerFilter.value, false, SceneEventType.LaserTriggerEnter, SceneEventType.LaserTriggerExit); }
            catch { }
        }
    }

    public static class TriggerRelay
    {
        static bool applying;

        static readonly Dictionary<int, int> localInside = new Dictionary<int, int>();

        // Host: volumes a relay entered, so only those are left again by a relay.
        static readonly HashSet<int> relayed = new HashSet<int>();

        public static void OnSceneCleanup() {
            localInside.Clear();
            relayed.Clear();
        }

        static bool IsLocal(Collider other) {
            try { return other && PlayerScripts.playerRig && other.transform.IsChildOf(PlayerScripts.playerRig.transform); }
            catch { return false; }
        }

        static bool InMask(Collider collider, int mask) => (mask & (1 << collider.gameObject.layer)) != 0;

        /// <summary>After the game's own enter or exit ran for one of our colliders.</summary>
        public static void Local(Component volume, Collider other, int mask, bool enter, SceneEventType enterType, SceneEventType exitType) {
            if (applying || !SteamIntegration.hasLobby || !volume || !IsLocal(other) || !InMask(other, mask))
                return;

            int id = volume.GetInstanceID();
            int count;
            localInside.TryGetValue(id, out count);
            if (enter) {
                localInside[id] = count + 1;
                if (count > 0)
                    return;
            }
            else {
                if (count <= 0)
                    return;
                localInside[id] = count - 1;
                if (count > 1)
                    return;
            }

            // The host's own triggers already spawn and start everything themselves.
            if (Node.isServer)
                return;

            SceneEventSync.SendEvent(enter ? enterType : exitType, volume.transform, 0, false);
        }

        public static bool Apply(SceneEventType type, Transform target) {
            bool enter, laser;
            switch (type) {
                case SceneEventType.PlayerTriggerEnter: enter = true; laser = false; break;
                case SceneEventType.PlayerTriggerExit: enter = false; laser = false; break;
                case SceneEventType.LaserTriggerEnter: enter = true; laser = true; break;
                case SceneEventType.LaserTriggerExit: enter = false; laser = true; break;
                default: return false;
            }

            if (!Node.isServer || !target)
                return true;

            TriggerLasers lasers = laser ? target.GetComponent<TriggerLasers>() : null;
            PlayerTrigger trigger = laser ? null : target.GetComponent<PlayerTrigger>();
            Component volume = laser ? (Component)lasers : trigger;
            if (!volume)
                return true;

            int id = volume.GetInstanceID();
            int mine;
            localInside.TryGetValue(id, out mine);

            if (enter) {
                // Our own player is already in it, so the game has run it.
                if (mine > 0 || !relayed.Add(id))
                    return true;
            }
            else {
                if (!relayed.Remove(id) || mine > 0)
                    return true;
            }

            int mask = laser ? lasers.LayerFilter.value : trigger.TriggerLayerMask.value;
            Collider collider = HostCollider(mask);
            if (!collider) {
                // Nothing ran, so nothing is entered: the next relay for it may try again.
                if (enter)
                    relayed.Remove(id);
                return true;
            }

            applying = true;
            bool wasApplyingRemote = SceneEventSync.applyingRemote;
            SceneEventSync.applyingRemote = false;
            try {
                if (laser) {
                    if (enter) lasers.OnTriggerEnter(collider);
                    else lasers.OnTriggerExit(collider);
                }
                else {
                    if (enter) trigger.OnTriggerEnter(collider);
                    else trigger.OnTriggerExit(collider);
                }
            }
            catch (Exception e) { EntangleLogger.Debug("[Trigger] Could not run " + target.name + " for a client: " + e.Message); }
            finally {
                applying = false;
                SceneEventSync.applyingRemote = wasApplyingRemote;
            }
            return true;
        }

        static Collider HostCollider(int mask) {
            try {
                GameObject player = SceneZone.PlayerObject;
                if (player) {
                    Collider own = player.GetComponent<Collider>();
                    if (own && InMask(own, mask) && IsLocal(own))
                        return own;
                }
                if (PlayerScripts.playerRig) {
                    Collider[] all = PlayerScripts.playerRig.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < all.Length; i++) {
                        if (all[i] && all[i].enabled && InMask(all[i], mask))
                            return all[i];
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
