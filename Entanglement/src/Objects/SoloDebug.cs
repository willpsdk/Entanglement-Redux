using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using MelonLoader;

using StressLevelZero.AI;
using PuppetMasta;

using Entanglement.Data;
using Entanglement.Entities;
using Entanglement.Gamemodes;
using Entanglement.Network;
using Entanglement.Network.Combat;
using Entanglement.Network.Layers;
using Entanglement.Representation;
using Entanglement.Sync;
using Entanglement.UI;

namespace Entanglement.Objects
{
    /// <summary>
    /// Checks of the sync code that need no second player (Debug, Sync Tests). Each one runs the
    /// same code a real session runs and says what it measured, in the game and in the log.
    /// </summary>
    public static class SoloDebug
    {
        static void Report(string text, float seconds = 5f) {
            EntangleLogger.Log("[SoloTest] " + text);
            GameNotify.Show(text, seconds);
        }

        /// <summary>Called every physics step (Mod.OnFixedUpdate) while a test runs.</summary>
        public static void FixedTick() {
            if (followRun != null)
                StepFollow();
            if (npcRun != null)
                StepNpc();
            if (freezeRun != null)
                StepFreeze();
        }

        public static void OnSceneCleanup() {
            if (npcRun != null)
                FinishNpc(npcRun, "the level changed");
            followRun = null;
            freezeRun = null;
        }

        static Vector3 PlayerPosition() {
            try {
                TriggerRefProxy local = PlayerScripts.LocalProxy();
                if (local != null)
                    return local.transform.position;
                if (PlayerScripts.playerRig)
                    return PlayerScripts.playerRig.transform.position;
            }
            catch { }
            return Vector3.zero;
        }

        // ---- the follower drive on a prop ------------------------------------------------------------

        const float FollowSeconds = 4f;
        const float FollowRadius = 0.5f;
        const float FollowLap = 2f;

        class FollowRun {
            public Rigidbody body;
            public Vector3 centre;
            public float start;
            public double errorSum;
            public float errorMax;
            public int samples;
        }

        static FollowRun followRun;

        /// <summary>A loose prop is steered round a circle by FollowDrive, as a follower copy would be.</summary>
        public static string StartFollowTest() {
            if (followRun != null)
                return "The follow test is already running.";
            Rigidbody body = NearestProp(4f);
            if (body == null)
                return "Stand next to a loose prop (not held) for the follow test.";
            followRun = new FollowRun { body = body, centre = body.position + Vector3.up, start = Time.fixedTime };
            return "Steering " + body.name + " round a circle for " + FollowSeconds + "s with the follower drive.";
        }

        static void StepFollow() {
            FollowRun run = followRun;
            try {
                if (!run.body) {
                    followRun = null;
                    Report("Follow test stopped: the prop went away.");
                    return;
                }
                float t = Time.fixedTime - run.start;
                if (t >= FollowSeconds) {
                    followRun = null;
                    float mean = run.samples > 0 ? (float)(run.errorSum / run.samples) : 0f;
                    bool pass = mean < 0.05f && run.errorMax < 0.15f;
                    Report("Follow test " + (pass ? "PASSED" : "FAILED") + ": off by " + (mean * 100f).ToString("F1") + "cm on average, "
                        + (run.errorMax * 100f).ToString("F1") + "cm at most, moving at " + (2f * (float)Math.PI * FollowRadius / FollowLap).ToString("F1") + "m/s.");
                    return;
                }

                float w = 2f * (float)Math.PI / FollowLap;
                float c = Mathf.Cos(w * t), s = Mathf.Sin(w * t);
                Vector3 target = run.centre + new Vector3(c * FollowRadius, 0f, s * FollowRadius);
                Vector3 targetVel = new Vector3(-s * FollowRadius * w, 0f, c * FollowRadius * w);
                Quaternion targetRot = Quaternion.Euler(0f, t * 90f, 0f);
                Vector3 targetSpin = new Vector3(0f, 90f * ((float)Math.PI / 180f), 0f);

                Rigidbody rb = run.body;
                Vector3 position = rb.position;
                // Give it the first half second to get there before counting.
                if (t > 0.5f) {
                    float error = (position - target).magnitude;
                    run.errorSum += error;
                    run.samples++;
                    if (error > run.errorMax)
                        run.errorMax = error;
                }
                FollowDrive.Steer(rb, position, rb.rotation, rb.velocity, rb.angularVelocity, target, targetRot, targetVel, targetSpin, Time.fixedDeltaTime);
            }
            catch (Exception e) {
                followRun = null;
                Report("Follow test stopped: " + e.Message);
            }
        }

        static Rigidbody NearestProp(float range) {
            Vector3 at = PlayerPosition();
            Collider[] found;
            try { found = Physics.OverlapSphere(at, range); }
            catch { return null; }
            Rigidbody best = null;
            float bestSqr = range * range;
            Transform rig = PlayerScripts.playerRig ? PlayerScripts.playerRig.transform : null;
            Rigidbody heldLeft = null, heldRight = null;
            try { heldLeft = PlayerScripts.playerLeftHand ? PlayerScripts.playerLeftHand.GetHeldObject() : null; } catch { }
            try { heldRight = PlayerScripts.playerRightHand ? PlayerScripts.playerRightHand.GetHeldObject() : null; } catch { }
            for (int i = 0; found != null && i < found.Length; i++) {
                try {
                    Rigidbody rb = found[i] ? found[i].attachedRigidbody : null;
                    if (!rb || rb.isKinematic || rb.mass < 0.05f || rb.mass > 30f)
                        continue;
                    if (rb == heldLeft || rb == heldRight || (rig && rb.transform.IsChildOf(rig)))
                        continue;
                    if (PooleeSyncable.IsNpcBody(rb.transform) || HitSourceIsRep(rb))
                        continue;
                    float sqr = (rb.position - at).sqrMagnitude;
                    if (sqr < bestSqr) {
                        bestSqr = sqr;
                        best = rb;
                    }
                }
                catch { }
            }
            return best;
        }

        static bool HitSourceIsRep(Rigidbody rb) {
            try { return Patching.HitSource.IsRepBody(rb); }
            catch { return false; }
        }

        // ---- an NPC that follows by forces -------------------------------------------------------------

        // 2.5 s of the NPC at 90 steps a second, then played back to its bones as a follower gets them.
        const int NpcRecordSteps = 225;

        struct BonePose {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 velocity;
            public Vector3 angularVelocity;
        }

        class NpcRun {
            public AIBrain brain;
            public PuppetMaster puppet;
            public BehaviourBaseNav nav;
            public Rigidbody[] bones;
            public readonly List<BonePose[]> frames = new List<BonePose[]>();
            public bool replaying;
            public int step;
            public float spring, damper;
            public bool anchors, navWasOn;
            public double errorSum;
            public float errorMax;
            public int samples;
            public float hipsDrop;
        }

        static NpcRun npcRun;

        /// <summary>
        /// The nearest living NPC is recorded for 2.5s as it runs, then made a follower (its AI and
        /// muscles off, as on a client) and its bones steered through the recording by FollowDrive.
        /// How far its bones stray, and whether its hips sink, is what a client would see.
        /// </summary>
        public static string StartNpcFollowTest() {
            if (npcRun != null)
                return "The NPC follow test is already running.";
            if (PlayerRepresentation.RemoteCount > 0)
                return "Run the NPC follow test with nobody else in the lobby.";
            AIBrain brain = NearestBrain(20f, true);
            if (brain == null)
                return "No living, awake NPC within 20m (NPCs At Dummy wakes some).";
            PuppetMaster puppet = brain.puppetMaster;
            BehaviourBaseNav nav = brain.behaviour;
            if (!puppet || nav == null)
                return "That NPC has no puppet to follow.";

            List<Rigidbody> bones = new List<Rigidbody>();
            var muscles = puppet.muscles;
            for (int i = 0; muscles != null && i < muscles.Length; i++) {
                Rigidbody rb = muscles[i] != null ? muscles[i].rigidbody : null;
                if (rb)
                    bones.Add(rb);
            }
            if (bones.Count == 0)
                return "That NPC has no bones to follow.";

            npcRun = new NpcRun { brain = brain, puppet = puppet, nav = nav, bones = bones.ToArray() };
            return "Recording " + NpcTrace.Name(brain.transform.root.gameObject) + " for 2.5s, then it follows the recording as a client's copy would.";
        }

        static void StepNpc() {
            NpcRun run = npcRun;
            try {
                if (!run.brain || run.brain.isDead) {
                    FinishNpc(run, "the NPC died or went away");
                    return;
                }
                if (!run.replaying) {
                    run.frames.Add(Capture(run.bones));
                    if (run.frames.Count >= NpcRecordSteps)
                        BeginReplay(run);
                    return;
                }
                if (run.step >= run.frames.Count) {
                    FinishNpc(run, null);
                    return;
                }

                BonePose[] frame = run.frames[run.step];
                float dt = Time.fixedDeltaTime;
                for (int b = 0; b < run.bones.Length; b++) {
                    Rigidbody rb = run.bones[b];
                    if (!rb)
                        continue;
                    Vector3 position = rb.position;
                    float error = (position - frame[b].position).magnitude;
                    run.errorSum += error;
                    run.samples++;
                    if (error > run.errorMax)
                        run.errorMax = error;
                    if (b == 0) {
                        float drop = frame[0].position.y - position.y;
                        if (drop > run.hipsDrop)
                            run.hipsDrop = drop;
                    }
                    FollowDrive.Steer(rb, position, rb.rotation, rb.velocity, rb.angularVelocity,
                        frame[b].position, frame[b].rotation, frame[b].velocity, frame[b].angularVelocity, dt);
                }
                run.step++;
            }
            catch (Exception e) { FinishNpc(run, e.Message); }
        }

        static BonePose[] Capture(Rigidbody[] bones) {
            BonePose[] poses = new BonePose[bones.Length];
            for (int i = 0; i < bones.Length; i++) {
                Rigidbody rb = bones[i];
                if (!rb)
                    continue;
                poses[i] = new BonePose { position = rb.position, rotation = rb.rotation, velocity = rb.velocity, angularVelocity = rb.angularVelocity };
            }
            return poses;
        }

        // The follower's start: muscles and AI off (PooleeSyncable.FollowRemote), and its bones put on
        // the first pose, as a follower too far from its owner's pose is.
        static void BeginReplay(NpcRun run) {
            run.spring = run.puppet.muscleSpring;
            run.damper = run.puppet.muscleDamper;
            run.anchors = run.puppet.updateJointAnchors;
            run.navWasOn = run.nav.enabled;
            run.puppet.muscleSpring = 0f;
            run.puppet.muscleDamper = 0f;
            run.puppet.updateJointAnchors = false;
            run.nav.enabled = false;

            BonePose[] first = run.frames[0];
            for (int b = 0; b < run.bones.Length; b++) {
                Rigidbody rb = run.bones[b];
                if (!rb)
                    continue;
                rb.position = first[b].position;
                rb.rotation = first[b].rotation;
                rb.velocity = first[b].velocity;
                rb.angularVelocity = first[b].angularVelocity;
            }
            run.replaying = true;
            run.step = 1;
        }

        static void FinishNpc(NpcRun run, string stopped) {
            npcRun = null;
            if (run.replaying) {
                try {
                    if (run.puppet) {
                        run.puppet.muscleSpring = run.spring;
                        run.puppet.muscleDamper = run.damper;
                        run.puppet.updateJointAnchors = run.anchors;
                    }
                    if (run.nav != null && run.navWasOn)
                        run.nav.enabled = true;
                }
                catch { }
            }
            if (stopped != null) {
                Report("NPC follow test stopped: " + stopped + ".");
                return;
            }
            float mean = run.samples > 0 ? (float)(run.errorSum / run.samples) : 0f;
            bool pass = mean < 0.05f && run.hipsDrop < 0.15f;
            Report("NPC follow test " + (pass ? "PASSED" : "FAILED") + ": its bones were off by " + (mean * 100f).ToString("F1")
                + "cm on average, " + (run.errorMax * 100f).ToString("F1") + "cm at most; its hips sank " + (run.hipsDrop * 100f).ToString("F1") + "cm at most.", 7f);
        }

        // ---- frozen is not dead -------------------------------------------------------------------------

        class FreezeRun {
            public AIBrain brain;
            public PuppetMaster puppet;
            public int phase;
            public float at;
            public bool frozen;
            public bool deadWhileFrozen;
        }

        static FreezeRun freezeRun;

        /// <summary>The nearest living NPC is frozen the way the game rests one out of sight, checked, and woken.</summary>
        public static string StartFreezeTest() {
            if (freezeRun != null)
                return "The freeze test is already running.";
            AIBrain brain = NearestBrain(20f, true);
            if (brain == null || !brain.puppetMaster)
                return "No living, awake NPC within 20m.";
            freezeRun = new FreezeRun { brain = brain, puppet = brain.puppetMaster, at = Time.time };
            brain.puppetMaster.state = PuppetMaster.State.Frozen;
            return "Freezing " + NpcTrace.Name(brain.transform.root.gameObject) + " as the game does when one rests out of sight.";
        }

        static void StepFreeze() {
            FreezeRun run = freezeRun;
            try {
                if (!run.brain || !run.puppet) {
                    freezeRun = null;
                    Report("Freeze test stopped: the NPC went away.");
                    return;
                }
                if (Time.time - run.at < 0.5f)
                    return;
                if (run.phase == 0) {
                    run.frozen = run.puppet.isFrozen;
                    run.deadWhileFrozen = NpcStateSync.IsDead(run.brain);
                    run.puppet.state = PuppetMaster.State.Alive;
                    run.phase = 1;
                    run.at = Time.time;
                    return;
                }
                freezeRun = null;
                bool awake = run.puppet.isAlive && !NpcStateSync.IsDead(run.brain);
                bool pass = run.frozen && !run.deadWhileFrozen && awake;
                Report("Freeze test " + (pass ? "PASSED" : "FAILED") + ": frozen " + (run.frozen ? "yes" : "no")
                    + ", taken for dead while frozen " + (run.deadWhileFrozen ? "YES" : "no")
                    + ", awake again " + (awake ? "yes" : "no") + ".");
            }
            catch (Exception e) {
                freezeRun = null;
                Report("Freeze test stopped: " + e.Message);
            }
        }

        static AIBrain NearestBrain(float range, bool awake) {
            AIBrain[] brains;
            try { brains = UnityEngine.Object.FindObjectsOfType<AIBrain>(); }
            catch { return null; }
            Vector3 at = PlayerPosition();
            AIBrain best = null;
            float bestSqr = range * range;
            for (int i = 0; brains != null && i < brains.Length; i++) {
                AIBrain brain = brains[i];
                try {
                    if (!brain || brain.isDead || NpcStateSync.IsDead(brain))
                        continue;
                    if (awake && NpcStateSync.IsFrozen(brain))
                        continue;
                    BehaviourBaseNav nav = brain.behaviour;
                    Vector3 position = nav != null ? nav.transform.position : brain.transform.position;
                    float sqr = (position - at).sqrMagnitude;
                    if (sqr < bestSqr) {
                        bestSqr = sqr;
                        best = brain;
                    }
                }
                catch { }
            }
            return best;
        }

        // ---- reports ------------------------------------------------------------------------------------

        /// <summary>Every synced NPC: would a hit on it reach it (one sync component, its behaviours on the hit list)?</summary>
        public static string HitCheck() {
            int npcs = 0, bad = 0;
            foreach (PooleeSyncable poolee in PooleeSyncable._PooleeLookup.Values) {
                try {
                    if (!ObjectSync.Live(poolee) || poolee.Brain == null)
                        continue;
                    npcs++;
                    string problem = poolee.HitRoutingProblem();
                    if (problem == null)
                        continue;
                    bad++;
                    EntangleLogger.Log("[SoloTest] #" + poolee.id + " " + NpcTrace.Name(poolee.gameObject) + ": " + problem);
                }
                catch { }
            }
            if (npcs == 0)
                return SteamIntegration.hasLobby ? "No synced NPCs here yet." : "Start a lobby first: only synced NPCs are checked.";
            return npcs + " synced NPC(s): " + (bad == 0 ? "a hit on any of them reaches it." : bad + " would lose hits (details in the log).");
        }

        public static string TurretAndClaw() {
            string text = TurretSync.Describe() + " " + BossClawSync.Describe();
            EntangleLogger.Log("[SoloTest] " + text);
            return text;
        }

        /// <summary>Installed models, items and mods, matched to Thunderstore packages the way they are when sent.</summary>
        public static string ThunderstoreCheck() {
            if (!ThunderstoreIndex.OnlineReady)
                return ThunderstoreIndex.OnlineLoading ? "The Thunderstore list is still loading; try again in a moment." : "The Thunderstore list could not be loaded.";

            int models = 0, modelsMatched = 0, items = 0, itemsMatched = 0, mods = 0, modsMatched = 0;
            ThunderstorePackage package;

            foreach (string file in Files(Path.Combine(MelonUtils.UserDataDirectory, "PlayerModels"), "*.body")) {
                models++;
                if (Thunderstore.TryMatchFile(file, out package)) {
                    modelsMatched++;
                    EntangleLogger.Log("[SoloTest] " + Path.GetFileName(file) + " -> " + package.author + "/" + package.name + "/" + package.version);
                }
            }
            foreach (string file in Files(Path.Combine(MelonUtils.UserDataDirectory, "CustomItems"), "*.melon")) {
                items++;
                if (Thunderstore.TryMatchFile(file, out package)) {
                    itemsMatched++;
                    EntangleLogger.Log("[SoloTest] " + Path.GetFileName(file) + " -> " + package.author + "/" + package.name + "/" + package.version);
                }
            }
            foreach (MelonMod mod in MelonHandler.Mods) {
                string name = null, author = null;
                try {
                    name = mod.Info.Name;
                    author = mod.Info.Author;
                }
                catch { }
                if (string.IsNullOrEmpty(name))
                    continue;
                mods++;
                if (Thunderstore.TryMatchModName(name, author, out package)) {
                    modsMatched++;
                    EntangleLogger.Log("[SoloTest] mod " + name + " -> " + package.author + "/" + package.name + "/" + package.version);
                }
                else
                    EntangleLogger.Log("[SoloTest] mod " + name + " by " + author + " is not on Thunderstore under a name we can be sure of");
            }

            string text = "Thunderstore: models " + modelsMatched + "/" + models + ", items " + itemsMatched + "/" + items
                + ", mods " + modsMatched + "/" + mods + " matched. Details in the log.";
            EntangleLogger.Log("[SoloTest] " + text);
            return text;
        }

        static string[] Files(string folder, string pattern) {
            try {
                if (Directory.Exists(folder))
                    return Directory.GetFiles(folder, pattern, SearchOption.AllDirectories);
            }
            catch { }
            return new string[0];
        }

        // ---- the test dummy and corpses ----------------------------------------------------------------

        static string EnsureDummy() {
            if (PlayerRepresentation.debugRepresentation == null)
                PlayerRepresentation.StartRepresentationTest();
            if (PlayerRepresentation.debugRepresentation == null)
                return "Could not make the dummy.";
            PlayerRepresentation.debugRepresentation.EnsureEnemyTarget();
            return null;
        }

        public static string SendNpcs() {
            string error = EnsureDummy();
            if (error != null)
                return error;
            int sent = PlayerRepresentation.SendNpcsAtDummy();
            return sent == 0 ? "No living NPCs near the dummy." : "Sent " + sent + " NPC(s) at the dummy.";
        }

        /// <summary>The nearest corpse fizzles the way a client's copy does when the host says so.</summary>
        public static string FizzleNearest() {
            AIBrain[] brains;
            try { brains = UnityEngine.Object.FindObjectsOfType<AIBrain>(); }
            catch { return "Could not look for NPCs."; }
            Vector3 from = PlayerPosition();
            AIBrain best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; brains != null && i < brains.Length; i++) {
                AIBrain brain = brains[i];
                try {
                    if (!brain || !NpcStateSync.IsDead(brain))
                        continue;
                    float sqr = (brain.transform.position - from).sqrMagnitude;
                    if (sqr < bestSqr) {
                        bestSqr = sqr;
                        best = brain;
                    }
                }
                catch { }
            }
            if (best == null)
                return "No dead NPCs nearby. Kill one first.";

            Transform root = best.transform;
            try {
                StressLevelZero.Pool.Poolee poolee = best.GetComponentInParent<StressLevelZero.Pool.Poolee>();
                if (poolee)
                    root = poolee.transform;
            }
            catch { }
            return CorpseCleanup.FizzleNow(root, true) ? "Fizzling " + root.name + " the way a client's copy does." : "Could not fizzle " + root.name + ".";
        }

        // ---- new Fusion-shaped stack (solo) -------------------------------------------------------------

        /// <summary>Spawns the dummy and checks PhysicsRigPuppet limbs + NetworkPlayer.</summary>
        public static string PuppetCheck() {
            string error = EnsureDummy();
            if (error != null)
                return error;

            PlayerRepresentation dummy = PlayerRepresentation.debugRepresentation;
            dummy.EnsureEnemyTarget();
            NetworkPlayer.Register(dummy);

            PhysicsRigPuppet phys = dummy.PhysicsPuppet;
            bool net = NetworkPlayer.IsRegistered(dummy.playerId);
            NetworkPlayer player;
            bool hasPuppet = NetworkPlayer.TryGet(dummy.playerId, out player) && player != null && player.Puppet != null && player.Puppet.HasPuppet;

            string detail = "dummy puppet="
                + (phys != null && phys.IsValid ? "ok" : "FAIL")
                + " pelvis=" + (phys != null && phys.Pelvis != null)
                + " chest=" + (phys != null && phys.Chest != null)
                + " hands=" + (phys != null && phys.LeftHand != null && phys.RightHand != null)
                + " feet=" + (phys != null && phys.LeftFoot != null && phys.RightFoot != null)
                + " proxy=" + (dummy.EnemyProxy != null)
                + " NetworkPlayer=" + net
                + " RigPuppet=" + hasPuppet;

            EntangleLogger.Log("[SoloTest] " + detail);
            DebugLog.DumpAll();
            bool pass = phys != null && phys.IsValid && net && dummy.EnemyProxy != null;
            return pass ? "PASS — " + detail : "FAIL — " + detail;
        }

        /// <summary>Counts NetworkEntity stack + registers a NetworkProp on the nearest synced body if any.</summary>
        public static string EntityStackCheck() {
            string error = EnsureDummy();
            if (error != null)
                return error;

            NetworkPlayer.Register(PlayerRepresentation.debugRepresentation);

            int props = NetworkProp.Count;
            int npcs = NpcEntity.Count;
            int players = NetworkPlayer.Count;
            int entities = NetworkEntityManager.Count;

            // Touch nearest TransformSyncable so NetworkProp registers (needs objectId).
            int touched = 0;
            try {
                foreach (Syncable sync in ObjectSync.syncedObjects.Values) {
                    TransformSyncable ts = sync as TransformSyncable;
                    if (ts == null || ts.objectId == 0)
                        continue;
                    if (NetworkProp.Register(ts) != null)
                        touched++;
                    if (touched >= 5)
                        break;
                }
            }
            catch { }

            string detail = "entities=" + entities + " players=" + players + " npcs=" + npcs
                + " props=" + NetworkProp.Count + " (touched " + touched + " syncables)"
                + " syncedObjects=" + ObjectSync.syncedObjects.Count;
            EntangleLogger.Log("[SoloTest] EntityStack: " + detail);
            bool pass = players >= 1;
            return (pass ? "PASS — " : "FAIL — ") + detail
                + (SteamIntegration.hasLobby ? "" : " (start a lobby to populate props/NPCs)");
        }

        /// <summary>Fusion MenuGamemode flow solo: browse highlight → Select → Ready/Start → Exit.</summary>
        public static string GamemodeSolo() {
            Gamemode mode = GamemodeManager.Find("Deathmatch");
            if (mode == null && GamemodeManager.Gamemodes.Count > 0)
                mode = GamemodeManager.Gamemodes[0];
            if (mode == null)
                return "FAIL — no gamemodes registered.";

            MenuGamemode.Show(() => { });
            // Browse highlight (Fusion SelectedGamemode) then Select.
            typeof(MenuGamemode).GetProperty("SelectedGamemode")
                ?.SetValue(null, mode, null);
            // Property has private set — select through manager directly after simulating browse.
            try {
                var prop = typeof(MenuGamemode).GetProperty("SelectedGamemode");
                var setter = prop?.GetSetMethod(true);
                setter?.Invoke(null, new object[] { mode });
            }
            catch { }

            GamemodeManager.Select(mode);
            bool selected = GamemodeManager.Active != null && GamemodeManager.Active.IsSelected;
            GamemodeManager.Ready();
            bool ready = GamemodeManager.IsGamemodeReady;
            GamemodeManager.StartActive();
            bool started = GamemodeManager.IsGamemodeStarted;
            bool canHit = NetworkCombat.CanAttack(1, 2);
            GamemodeManager.Stop();
            bool stopped = !GamemodeManager.IsGamemodeStarted;
            GamemodeManager.Deselect();
            bool cleared = GamemodeManager.Active == null;

            string detail = mode.Title
                + " modes=" + GamemodeManager.Gamemodes.Count
                + " selected=" + selected + " ready=" + ready + " started=" + started
                + " canAttack=" + canHit + " stopped=" + stopped + " cleared=" + cleared;
            EntangleLogger.Log("[SoloTest] Gamemode: " + detail);
            bool pass = selected && ready && started && canHit && stopped && cleared;
            return (pass ? "PASS — " : "FAIL — ") + detail;
        }

        /// <summary>Dummy enemy proxy + ImpactProperties on puppet parts (melee/gun target surface).</summary>
        public static string CombatDummyCheck() {
            string error = EnsureDummy();
            if (error != null)
                return error;

            PlayerRepresentation dummy = PlayerRepresentation.debugRepresentation;
            dummy.EnsureEnemyTarget();
            PhysicsRigPuppet phys = dummy.PhysicsPuppet;

            int impacts = 0;
            void CountImpact(UnityEngine.Component c) {
                if (c == null)
                    return;
                try {
                    if (c.GetComponent<StressLevelZero.Combat.ImpactProperties>() != null)
                        impacts++;
                }
                catch { }
            }
            if (phys != null) {
                CountImpact(phys.Pelvis);
                CountImpact(phys.Chest);
                CountImpact(phys.LeftHand);
                CountImpact(phys.RightHand);
            }

            bool can = NetworkCombat.CanAttack(SteamIntegration.currentUserId, dummy.playerId)
                || !SteamIntegration.hasLobby;
            string detail = "proxy=" + (dummy.EnemyProxy != null)
                + " puppetValid=" + (phys != null && phys.IsValid)
                + " impactProps=" + impacts
                + " canAttackDummy=" + can
                + " lobby=" + SteamIntegration.hasLobby;
            EntangleLogger.Log("[SoloTest] CombatDummy: " + detail);
            bool pass = dummy.EnemyProxy != null && phys != null && phys.IsValid && impacts >= 2;
            return (pass ? "PASS — " : "FAIL — ") + detail
                + " (punch/shoot the dummy to feel contact; damage needs a lobby peer path)";
        }

        /// <summary>Active + preferred transport (Steam / SteamVR Meta).</summary>
        public static string TransportCheck() {
            SteamNetworkLayer steam = NetworkLayerManager.Layer as SteamNetworkLayer;
            uint appId = steam != null ? steam.ApplicationID : 0;
            string detail = "active=" + NetworkLayerManager.ActiveTitle
                + " preferred=" + NetworkPrefs.ResolvedLayerTitle()
                + " appId=" + (appId == 0 ? "game/default" : appId.ToString())
                + " available=" + (NetworkLayerManager.Layer != null && NetworkLayerManager.Layer.IsAvailable)
                + " loggedIn=" + (NetworkLayerManager.Layer != null && NetworkLayerManager.Layer.IsLoggedIn);
            EntangleLogger.Log("[SoloTest] Transport: " + detail);
            DebugLog.TransportLog(detail);
            return detail;
        }

        /// <summary>The nearest synced NPC is restarted the way a client restarts a pooled copy (RestartLikeHost).</summary>
        public static string RestartNearestNpc() {
            AIBrain[] brains;
            try { brains = UnityEngine.Object.FindObjectsOfType<AIBrain>(); }
            catch { return "Could not look for NPCs."; }
            Vector3 from = PlayerPosition();
            PooleeSyncable best = null;
            float bestSqr = 25f * 25f;
            for (int i = 0; brains != null && i < brains.Length; i++) {
                try {
                    AIBrain brain = brains[i];
                    if (!brain)
                        continue;
                    PooleeSyncable poolee = PooleeSyncable.SyncedNpcOf(brain.transform);
                    if (poolee == null)
                        continue;
                    float sqr = (brain.transform.position - from).sqrMagnitude;
                    if (sqr < bestSqr) {
                        bestSqr = sqr;
                        best = poolee;
                    }
                }
                catch { }
            }
            if (best == null)
                return SteamIntegration.hasLobby ? "No synced NPC within 25m." : "Start a lobby first: only synced NPCs restart this way.";
            best.RestartLikeHost();
            return "Restarted " + best.gameObject.name + " #" + best.id + " the way a client's copy is.";
        }
    }
}
