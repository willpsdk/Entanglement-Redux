using System;
using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.AI;

using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.Objects
{
    /// <summary>
    /// Lets the NPCs this machine runs notice other players. A rep is not one of the game's own AI
    /// targets, so the game never tells an NPC's vision about it. Here each NPC's vision sphere is
    /// checked against the reps' hit bodies and the NPC is told when one comes in or goes out, the
    /// way Unity would tell it. An NPC that keeps looking into someone it can plainly see is then
    /// told to fight them (CompleteSight). Turrets see other players by running on their machine
    /// instead (TurretSync).
    /// </summary>
    public static class RepTargetEmulator
    {
        const float TickInterval = 0.1f;
        const float VisionRescan = 1f;
        const float SoloRescan = 3f;

        static float nextTick;

        public static void OnSceneCleanup() {
            ReportSummary(true);
            visions.Clear();
            bodies.Clear();
            torsoById.Clear();
            nextVisionScan = 0f;
            visionLogs = 0;
        }

        public static void Tick() {
            if (Time.time < nextTick)
                return;
            nextTick = Time.time + TickInterval;

            TickVision();
            ReportSummary(false);
        }

        class Vision {
            public AIBrain brain;
            public PuppetMasta.BehaviourBaseNav nav;
            public PuppetMasta.SubBehaviourSensors sensors;
            public SphereCollider sphere;
            // The rep it has been looking into, and since when (CompleteSight).
            public TriggerRefProxy investigating;
            public float investigatingSince;
            public bool fightingRep;
            public bool seen;
            public int delivered;
            public readonly HashSet<int> inside = new HashSet<int>();
        }

        class RepBody {
            public PlayerRepresentation rep;
            public TriggerRefProxy proxy;
            public Collider head;
            public Collider torso;
            public int id;
        }

        static readonly Dictionary<int, Vision> visions = new Dictionary<int, Vision>();
        static readonly List<RepBody> bodies = new List<RepBody>();
        static readonly Dictionary<int, Collider> torsoById = new Dictionary<int, Collider>();
        static readonly List<int> staleIds = new List<int>();
        static float nextVisionScan;
        static int visionLogs;

        static void TickVision() {
            bodies.Clear();
            foreach (PlayerRepresentation rep in PlayerRepresentation.representations.Values)
                AddBody(rep);
            AddBody(PlayerRepresentation.debugRepresentation);

            if (bodies.Count == 0) {
                if (visions.Count > 0)
                    DropAll();
                return;
            }

            if (Time.time >= nextVisionScan) {
                bool lobby = SteamIntegration.hasLobby;
                nextVisionScan = Time.time + (lobby ? VisionRescan : SoloRescan);
                ScanVisions(lobby);
            }

            foreach (Vision vision in visions.Values) {
                try { TickVision(vision); }
                catch { }
            }
        }

        static void AddBody(PlayerRepresentation rep) {
            if (rep == null)
                return;
            try {
                TriggerRefProxy proxy = rep.EnemyProxy;
                Collider torso = rep.EnemyTorso;
                if (proxy == null || !torso)
                    return;
                RepBody body = new RepBody { rep = rep, proxy = proxy, head = rep.EnemyHead, torso = torso, id = proxy.GetInstanceID() };
                bodies.Add(body);
                torsoById[body.id] = torso;
            }
            catch { }
        }

        // In a lobby: the synced NPCs this machine runs (a copy that follows someone else's has its
        // AI off). Solo with the test dummy there is nothing synced, so the scene is searched, slowly.
        static void ScanVisions(bool lobby) {
            foreach (Vision vision in visions.Values)
                vision.seen = false;

            if (lobby) {
                foreach (PooleeSyncable npc in PooleeSyncable._PooleeLookup.Values) {
                    try {
                        if (!ObjectSync.Live(npc) || !npc.RunsHere)
                            continue;
                        Consider(npc.Brain);
                    }
                    catch { }
                }
            }
            else {
                AIBrain[] brains = null;
                try { brains = UnityEngine.Object.FindObjectsOfType<AIBrain>(); }
                catch { }
                for (int i = 0; brains != null && i < brains.Length; i++) {
                    try { Consider(brains[i]); }
                    catch { }
                }
            }

            // Gone, or now run by someone else: walked out of what it saw, as Unity would on removal.
            staleIds.Clear();
            foreach (KeyValuePair<int, Vision> pair in visions) {
                if (!pair.Value.seen)
                    staleIds.Add(pair.Key);
            }
            for (int i = 0; i < staleIds.Count; i++) {
                Vision vision = visions[staleIds[i]];
                visions.Remove(staleIds[i]);
                WalkOut(vision);
            }
        }

        static void Consider(AIBrain brain) {
            if (!brain || brain.isDead)
                return;
            PuppetMasta.BehaviourBaseNav nav = brain.behaviour;
            if (nav == null)
                return;
            // The void watcher has its own OnTriggerEnter, which Unity would call instead.
            if (nav.TryCast<PuppetMasta.BehaviourVoidWatcher>() != null)
                return;

            int id = nav.GetInstanceID();
            Vision vision;
            if (!visions.TryGetValue(id, out vision)) {
                vision = new Vision { brain = brain, nav = nav };
                visions[id] = vision;
            }
            vision.sensors = nav.sensors;
            vision.sphere = vision.sensors != null ? vision.sensors._visionSphere : null;
            vision.seen = true;
        }

        static void WalkOut(Vision vision) {
            if (vision.inside.Count == 0)
                return;
            try {
                if (vision.nav && vision.nav.triggerCount >= vision.delivered) {
                    foreach (int id in vision.inside) {
                        Collider torso;
                        if (torsoById.TryGetValue(id, out torso) && torso)
                            vision.nav.OnTriggerExit(torso);
                    }
                }
            }
            catch { }
            vision.inside.Clear();
            vision.delivered = 0;
        }

        static void DropAll() {
            foreach (Vision vision in visions.Values)
                WalkOut(vision);
            visions.Clear();
        }

        static void TickVision(Vision vision) {
            SphereCollider sphere = vision.sphere;
            if (!vision.nav || !sphere || !sphere.enabled || !sphere.gameObject.activeInHierarchy) {
                vision.inside.Clear();
                return;
            }

            if (vision.nav.triggerCount < vision.delivered) {
                vision.delivered = 0;
                vision.inside.Clear();
            }

            Transform tr = sphere.transform;
            Vector3 centre = tr.TransformPoint(sphere.center);
            Vector3 scale = tr.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

            for (int b = 0; b < bodies.Count; b++) {
                RepBody body = bodies[b];
                bool was = vision.inside.Contains(body.id);
                bool now = Touches(body.torso, centre, radius) || (body.head && Touches(body.head, centre, radius));
                if (now == was)
                    continue;

                if (now) {
                    vision.nav.OnTriggerEnter(body.torso);
                    vision.inside.Add(body.id);
                    vision.delivered++;
                    npcsNoticed++;
                }
                else {
                    vision.nav.OnTriggerExit(body.torso);
                    vision.inside.Remove(body.id);
                    if (vision.delivered > 0)
                        vision.delivered--;
                }
                LogVision(vision, body.proxy, now ? " noticed " : " lost ");
            }

            // A rep that is gone without a word (its object destroyed): forget it, as Unity would.
            if (vision.inside.Count > 0) {
                staleIds.Clear();
                foreach (int id in vision.inside) {
                    bool present = false;
                    for (int b = 0; b < bodies.Count; b++) {
                        if (bodies[b].id == id) {
                            present = true;
                            break;
                        }
                    }
                    if (!present)
                        staleIds.Add(id);
                }
                for (int i = 0; i < staleIds.Count; i++)
                    vision.inside.Remove(staleIds[i]);
            }

            CompleteSight(vision);
        }

        static bool Touches(Collider col, Vector3 centre, float radius) {
            Vector3 closest = col.ClosestPoint(centre);
            return (closest - centre).sqrMagnitude <= radius * radius;
        }

        const float StuckInvestigating = 2f;

        // The game's own sight check only knows its own targets, so an NPC looking into a rep never
        // decides it has seen them. When the rep is in front of its eyes with nothing in between,
        // it is told so, as the game would have.
        static void CompleteSight(Vision vision) {
            if (!vision.brain || !vision.brain.enabled || vision.brain.isDead) {
                vision.investigating = null;
                vision.fightingRep = false;
                return;
            }

            PuppetMasta.SubBehaviourSensors sensors = vision.sensors;
            TriggerRefProxy target = sensors != null ? sensors.target : null;
            PuppetMasta.BehaviourBaseNav.MentalState state = vision.nav.mentalState;

            RepBody body = null;
            if (target != null) {
                for (int b = 0; b < bodies.Count; b++) {
                    if (bodies[b].proxy == target) {
                        body = bodies[b];
                        break;
                    }
                }
            }

            // Counted once per fight, for the summary.
            bool fighting = body != null && (state == PuppetMasta.BehaviourBaseNav.MentalState.Agroed
                || state == PuppetMasta.BehaviourBaseNav.MentalState.Engaged);
            if (fighting && !vision.fightingRep)
                npcsFought++;
            vision.fightingRep = fighting;

            if (state != PuppetMasta.BehaviourBaseNav.MentalState.Investigate || body == null) {
                vision.investigating = null;
                return;
            }
            if (vision.investigating != target) {
                vision.investigating = target;
                vision.investigatingSince = Time.time;
            }

            Transform eye = vision.nav.eyeTran;
            Rigidbody head = target.targetHead;
            if (!eye || !head)
                return;

            Vector3 dir = head.position - eye.position;
            float distance = dir.magnitude;
            if (distance < 0.01f)
                return;
            dir /= distance;

            if (Vector3.Dot(eye.forward, dir) <= sensors.visionFovCos)
                return;

            RaycastHit[] hits = Physics.RaycastAll(eye.position, dir, distance, sensors.blockVisionRaycast);
            if (hits == null || hits.Length == 0) {
                if (!vision.inside.Contains(body.id) || Time.time - vision.investigatingSince < StuckInvestigating)
                    return;
                vision.nav.SwitchMentalState(PuppetMasta.BehaviourBaseNav.MentalState.Agroed);
                forcedFights++;
                LogVision(vision, target, " had it in plain view and never committed, fighting ");
                return;
            }
            for (int i = 0; i < hits.Length; i++) {
                if (!body.rep.OwnsCollider(hits[i].collider))
                    return;
            }

            vision.nav.SwitchMentalState(PuppetMasta.BehaviourBaseNav.MentalState.Agroed);
            forcedFights++;
            LogVision(vision, target, " only lost sight of it through its own body, fighting ");
        }

        const float SummaryEvery = 60f;
        static float nextSummary;
        static int npcsNoticed, npcsFought, forcedFights;
        static int reportedNoticed, reportedFought, reportedForced;

        static void ReportSummary(bool final) {
            if (!final && Time.time < nextSummary)
                return;
            nextSummary = Time.time + SummaryEvery;
            if (npcsNoticed == reportedNoticed && npcsFought == reportedFought && forcedFights == reportedForced)
                return;
            EntangleLogger.Log("[RepTarget] Since the last report: NPCs noticed other players " + (npcsNoticed - reportedNoticed)
                + " time(s) and went for them " + (npcsFought - reportedFought)
                + " (" + (forcedFights - reportedForced) + " nudged from looking into fighting).");
            reportedNoticed = npcsNoticed;
            reportedFought = npcsFought;
            reportedForced = forcedFights;
        }

        static void LogVision(Vision vision, TriggerRefProxy proxy, string what) {
            if (visionLogs >= 150)
                return;
            visionLogs++;
            string npc, who;
            try { npc = vision.nav.transform.root.name; }
            catch { npc = "?"; }
            try { who = proxy.transform.root.name; }
            catch { who = "?"; }
            EntangleLogger.Debug("[RepTarget] " + npc + what + who);
        }

        /// <summary>A rep's body is about to be destroyed: take it out of every NPC's vision now.</summary>
        public static void ForgetProxy(TriggerRefProxy proxy) {
            int id;
            try { id = proxy.GetInstanceID(); }
            catch { return; }

            Collider torso;
            torsoById.TryGetValue(id, out torso);
            torsoById.Remove(id);

            foreach (Vision vision in visions.Values) {
                if (!vision.inside.Remove(id))
                    continue;
                try {
                    // Not after a reset: that enter was already wiped by the game.
                    if (torso && vision.nav && vision.nav.triggerCount >= vision.delivered) {
                        vision.nav.OnTriggerExit(torso);
                        if (vision.delivered > 0)
                            vision.delivered--;
                    }
                }
                catch { }
            }
        }
    }
}
