using System;
using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.Zones;

using Entanglement.Data;
using Entanglement.Network;
using Entanglement.Representation;
using Entanglement.Patching;

namespace Entanglement.Objects
{
    public static class RemoteZonePresence
    {
        public static bool enabled = true;

        const float tickInterval = 0.25f;
        const float zoneRescan = 10f;

        const float BodyHeight = 0.9f;     // feet to middle of the body
        const float EnterMargin = 0.3f;    // the probe's own radius, as a real collider would touch
        const float ExitMargin = 1.5f;
        const float ExitDwell = 1f;

        static SceneZone[] zones;
        static float nextZoneScan;
        static float nextTick;

        class Presence {
            public GameObject probe;
            public Collider collider;
            public readonly Dictionary<int, SceneZone> inside = new Dictionary<int, SceneZone>();
            public readonly Dictionary<int, float> outsideSince = new Dictionary<int, float>();
            // Zone -> when to check it really came on (see Enter).
            public readonly Dictionary<int, float> verifyAt = new Dictionary<int, float>();
            // Zones this probe had to enter through the zone's own OnTriggerEnter.
            public readonly HashSet<int> viaGame = new HashSet<int>();
        }

        const float VerifyDelay = 1.2f;

        static readonly Dictionary<long, Presence> players = new Dictionary<long, Presence>();

        static int playerLayer = -1;

        public static void OnSceneCleanup() {
            // The scene is going away with its zones, so no exit calls, just forget everything.
            foreach (Presence p in players.Values) {
                try { if (p.probe) UnityEngine.Object.Destroy(p.probe); } catch { }
            }
            players.Clear();
            zones = null;
            nextZoneScan = 0f;
            playerLayer = -1;
        }

        public static void Tick() {
            if (!enabled || !SteamIntegration.hasLobby)
                return;

            if (Time.time < nextTick)
                return;
            nextTick = Time.time + tickInterval;

            // Nobody else here: nothing to do, and no reason to search the level for zones.
            if (PlayerRepresentation.RemoteCount == 0 && players.Count == 0)
                return;

            if (zones == null || Time.time >= nextZoneScan) {
                nextZoneScan = Time.time + zoneRescan;
                try { zones = UnityEngine.Object.FindObjectsOfType<SceneZone>(); }
                catch { zones = null; }
            }
            if (zones == null || zones.Length == 0)
                return;

            foreach (KeyValuePair<long, PlayerRepresentation> pair in PlayerRepresentation.Remotes()) {
                try { Update(pair.Key, pair.Value); }
                catch (Exception e) { NpcTrace.Log("zone probe for " + pair.Key + " failed: " + e.Message); }
            }

            // Players who left: walk them out of every zone so the counts go back down.
            if (players.Count > PlayerRepresentation.RemoteCount) {
                List<long> gone = new List<long>();
                foreach (long id in players.Keys) {
                    if (!PlayerRepresentation.IsRemote(id))
                        gone.Add(id);
                }
                for (int i = 0; i < gone.Count; i++)
                    Remove(gone[i]);
            }
        }

        static void Update(long playerId, PlayerRepresentation rep) {
            if (rep == null || rep.repRoot == null)
                return;

            Presence presence = GetPresence(playerId);
            if (presence == null)
                return;

            Vector3 feet = rep.repRoot.position;
            Vector3 body = feet + Vector3.up * BodyHeight;
            presence.probe.transform.position = feet;
            float now = Time.time;

            // Leave first, then enter, the same order a real player's collider would produce.
            List<int> left = null;
            foreach (KeyValuePair<int, SceneZone> pair in presence.inside) {
                bool still;
                try { still = pair.Value && (Contains(pair.Value, body, ExitMargin) || Contains(pair.Value, feet, ExitMargin)); }
                catch { still = false; }
                if (still) {
                    presence.outsideSince.Remove(pair.Key);
                    continue;
                }

                float since;
                if (!presence.outsideSince.TryGetValue(pair.Key, out since)) {
                    presence.outsideSince[pair.Key] = now;
                    continue;
                }
                bool gone;
                try { gone = !pair.Value; }
                catch { gone = true; }
                if (!gone && now - since < ExitDwell)
                    continue;

                if (left == null)
                    left = new List<int>();
                left.Add(pair.Key);
            }
            if (left != null) {
                for (int i = 0; i < left.Count; i++) {
                    SceneZone zone = presence.inside[left[i]];
                    presence.inside.Remove(left[i]);
                    presence.outsideSince.Remove(left[i]);
                    bool live;
                    try { live = zone; }
                    catch { live = false; }
                    if (live)
                        Exit(zone, presence, playerId);
                }
            }

            Verify(presence, playerId);

            for (int i = 0; i < zones.Length; i++) {
                SceneZone zone = zones[i];
                int id;
                try {
                    if (!zone)
                        continue;
                    id = zone.GetInstanceID();
                    if (presence.inside.ContainsKey(id)) {
                        // Keep the newest wrapper for when it is left.
                        presence.inside[id] = zone;
                        continue;
                    }
                    if (!Contains(zone, body, EnterMargin) && !Contains(zone, feet, EnterMargin))
                        continue;
                }
                catch { continue; }

                presence.inside[id] = zone;
                presence.outsideSince.Remove(id);
                Enter(zone, presence, playerId);
            }
        }

        // Inside the zone's box grown by margin metres on every side.
        static bool Contains(SceneZone zone, Vector3 point, float margin) {
            BoxCollider box = zone.boxCollider;
            if (!box)
                return false;
            Transform t = box.transform;
            Vector3 local = t.InverseTransformPoint(point) - box.center;
            Vector3 half = box.size * 0.5f;
            Vector3 scale = t.lossyScale;
            return Mathf.Abs(local.x) <= half.x + Grow(margin, scale.x)
                && Mathf.Abs(local.y) <= half.y + Grow(margin, scale.y)
                && Mathf.Abs(local.z) <= half.z + Grow(margin, scale.z);
        }

        // The margin in the box's own units.
        static float Grow(float margin, float scale) {
            scale = Mathf.Abs(scale);
            return scale > 0.0001f ? margin / scale : 0f;
        }

        static void Enter(SceneZone zone, Presence presence, long playerId) {
            try {
                int id = zone.GetInstanceID();
                ZoneTrackingUtilities.ProbeEntered(zone, presence.collider);

                Il2CppSystem.Collections.Generic.List<SceneZone> triggered = SceneZone._triggeredZones;
                if (triggered != null && !triggered.Contains(zone)) {
                    triggered.Add(zone);
                    SceneZone._hasZonesChanged = true;
                }
                presence.verifyAt[id] = Time.time + VerifyDelay;
                NpcTrace.Log("zone " + zone.name + " entered by " + playerId + " (listed, on=" + zone._isZoneEnabled + ")");
            }
            catch (Exception e) { NpcTrace.Log("zone " + zone.name + " enter for " + playerId + " failed: " + e.Message); }
        }

        static void Exit(SceneZone zone, Presence presence, long playerId) {
            try {
                int id = zone.GetInstanceID();
                presence.verifyAt.Remove(id);

                if (presence.viaGame.Remove(id)) {
                    ZoneTrackingUtilities.forcing = false;
                    zone.OnTriggerExit(presence.collider);   // ZoneExitPatch keeps it on if anyone is still in it
                }
                else {
                    ZoneTrackingUtilities.ProbeLeft(zone, presence.collider);
                    // Still ours, or someone else's: stays on the list.
                    if (ZoneTrackingUtilities.LocalCount(zone) == 0 && ZoneTrackingUtilities.ProbeCount(zone) == 0) {
                        Il2CppSystem.Collections.Generic.List<SceneZone> triggered = SceneZone._triggeredZones;
                        if (triggered != null && triggered.Contains(zone)) {
                            triggered.Remove(zone);
                            SceneZone._hasZonesChanged = true;
                        }
                    }
                }
                NpcTrace.Log("zone " + zone.name + " left by " + playerId + " (on=" + zone._isZoneEnabled + ")");
            }
            catch (Exception e) { NpcTrace.Log("zone " + zone.name + " exit for " + playerId + " failed: " + e.Message); }
        }

        // A zone only put on the list that still isn't on: use its own enter, then give primary back.
        static void Verify(Presence presence, long playerId) {
            if (presence.verifyAt.Count == 0)
                return;
            List<int> due = null;
            foreach (KeyValuePair<int, float> pair in presence.verifyAt) {
                if (Time.time >= pair.Value) {
                    if (due == null)
                        due = new List<int>();
                    due.Add(pair.Key);
                }
            }
            if (due == null)
                return;

            for (int i = 0; i < due.Count; i++) {
                int id = due[i];
                presence.verifyAt.Remove(id);
                SceneZone zone;
                if (!presence.inside.TryGetValue(id, out zone))
                    continue;
                try {
                    if (!zone || zone.IsEnabled)
                        continue;

                    ZoneTrackingUtilities.forcing = true;
                    try { zone.OnTriggerEnter(presence.collider); }
                    finally { ZoneTrackingUtilities.forcing = false; }
                    presence.viaGame.Add(id);
                    LocalZoneWatch.GiveBackPrimary();
                    NpcTrace.Log("zone " + zone.name + " needed its own enter for " + playerId + " (on=" + zone._isZoneEnabled + ")");
                }
                catch (Exception e) { NpcTrace.Log("zone verify for " + playerId + " failed: " + e.Message); }
            }
        }

        static void Remove(long playerId) {
            Presence presence;
            if (!players.TryGetValue(playerId, out presence))
                return;
            players.Remove(playerId);

            foreach (SceneZone zone in new List<SceneZone>(presence.inside.Values)) {
                try {
                    if (zone)
                        Exit(zone, presence, playerId);
                }
                catch { }
            }
            try { if (presence.probe) UnityEngine.Object.Destroy(presence.probe); } catch { }
        }

        static Presence GetPresence(long playerId) {
            Presence presence;
            if (players.TryGetValue(playerId, out presence) && presence.probe)
                return presence;

            int layer = PlayerLayer();
            if (layer < 0)
                return null;

            GameObject probe = new GameObject("ZoneProbe." + playerId);
            probe.layer = layer;
            probe.tag = "Player";

            Rigidbody rb = probe.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = false;

            SphereCollider col = probe.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.3f;
            col.enabled = false; // Physics never sees it; only the zones we call get it

            presence = new Presence { probe = probe, collider = col };
            players[playerId] = presence;
            return presence;
        }

        // The layer of the local player's collider that zones react to.
        static int PlayerLayer() {
            if (playerLayer >= 0)
                return playerLayer;
            try {
                if (!PlayerScripts.playerRig)
                    return -1;
                Collider[] cols = PlayerScripts.playerRig.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++) {
                    if (cols[i] && cols[i].CompareTag("Player")) {
                        playerLayer = cols[i].gameObject.layer;
                        NpcTrace.Log("zone probes use layer " + playerLayer + " from " + cols[i].name);
                        return playerLayer;
                    }
                }
            }
            catch { }
            EntangleLogger.Warn("[ZoneProbe] The local player has no collider tagged Player, so remote players can't wake zones.");
            playerLayer = 0;
            return playerLayer;
        }
    }

    public static class LocalZoneWatch
    {
        const float Interval = 2f;
        const float RepairCooldown = 10f;
        static float nextCheck;
        static int badChecks;
        static float lastRepair = -100f;

        public static void OnSceneCleanup() {
            nextCheck = 0f;
            badChecks = 0;
            lastRepair = -100f;
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || SceneLoader.loading || SceneEventSync.SinceReady < 5f)
                return;
            if (Time.realtimeSinceStartup < nextCheck)
                return;
            nextCheck = Time.realtimeSinceStartup + Interval;

            List<KeyValuePair<SceneZone, Collider>> mine = ZoneTrackingUtilities.LocalZones();
            if (mine.Count == 0) {
                badChecks = 0;
                return;
            }

            string problem = null;
            try {
                SceneZone current = SceneZone.CurrentZone;
                bool primaryIsMine = false;
                for (int i = 0; i < mine.Count; i++) {
                    SceneZone zone = mine[i].Key;
                    if (!zone)
                        continue;
                    if (!zone.IsEnabled && problem == null)
                        problem = zone.name + " is switched off with you standing in it";
                    if (current && current == zone)
                        primaryIsMine = true;
                }
                if (problem == null && !primaryIsMine)
                    problem = "the zone you are in is not the primary one" + (current ? " (" + current.name + " is)" : "");
            }
            catch { return; }

            if (problem == null) {
                badChecks = 0;
                return;
            }
            if (++badChecks < 2 || Time.realtimeSinceStartup - lastRepair < RepairCooldown)
                return;

            badChecks = 0;
            lastRepair = Time.realtimeSinceStartup;
            EntangleLogger.Log("[Zone] " + problem + "; handing it back to you.");
            GiveBackPrimary();
        }

        /// <summary>Runs the enter of the zone we entered most recently, with our own collider.</summary>
        public static void GiveBackPrimary() {
            List<KeyValuePair<SceneZone, Collider>> mine = ZoneTrackingUtilities.LocalZones();
            for (int i = mine.Count - 1; i >= 0; i--) {
                SceneZone zone = mine[i].Key;
                Collider collider = mine[i].Value;
                try {
                    if (!zone || !collider || !collider.gameObject.activeInHierarchy)
                        continue;
                    zone.OnTriggerEnter(collider);
                    return;
                }
                catch (Exception e) {
                    EntangleLogger.Debug("[Zone] Could not hand " + zone.name + " back: " + e.Message);
                }
            }
        }
    }
}
