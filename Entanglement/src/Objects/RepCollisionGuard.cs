using System;
using System.Collections.Generic;

using UnityEngine;

using UnhollowerBaseLib;

using StressLevelZero.AI;
using StressLevelZero.Rig;

using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.Objects
{
    public static class RepCollisionGuard
    {
        public static bool enabled = false;

        public static bool platforms = true;

        const float scanInterval = 0.25f;
        const float scanRadius = 2.5f;
        const float repairInterval = 8f;
        static float nextScan;

        class RepPairs {
            public Collider[] colliders;
            public readonly HashSet<int> handled = new HashSet<int>();
            public float repairAt;
        }

        static readonly Dictionary<long, RepPairs> pairs = new Dictionary<long, RepPairs>();
        static readonly Dictionary<int, bool> platformCache = new Dictionary<int, bool>();
        static readonly Dictionary<int, bool> buttonCache = new Dictionary<int, bool>();
        // NPC, rep and local-player bodies, never touched here. Working that out takes a dozen calls into
        // the game per body; in a pile of crate pieces that was up to 17ms each repair pass (Oct 8 logs).
        // It is remembered per body, and only a few new bodies are worked out per scan.
        static readonly Dictionary<int, bool> skipCache = new Dictionary<int, bool>();
        const int ClassifyPerScan = 32;
        static int classifyBudget;

        // Switching off every collider pair between a body and a rep is thousands of calls for a big
        // platform (54ms in one frame on Oct 9). A scan stops after this long and the rest of the bodies
        // wait for the next scan.
        static readonly long scanBudgetTicks = System.Diagnostics.Stopwatch.Frequency * 4 / 1000;
        static long scanStart;

        static readonly Il2CppReferenceArray<Collider> overlapBuffer = new Il2CppReferenceArray<Collider>(128);

        public static void OnSceneCleanup() {
            pairs.Clear();
            platformCache.Clear();
            buttonCache.Clear();
            skipCache.Clear();
            nextScan = 0f;
        }

        public static void Tick() {
            if ((!enabled && !platforms) || !SteamIntegration.hasLobby)
                return;
            // Mid-load our old rig is gone and the new one is still frozen, which reads as a platform.
            if (!LocalRigReady())
                return;

            if (Time.time < nextScan)
                return;
            nextScan = Time.time + scanInterval;

            var reps = PlayerRepresentation.representations;
            if (reps.Count == 0)
                return;

            classifyBudget = ClassifyPerScan;
            scanStart = System.Diagnostics.Stopwatch.GetTimestamp();

            foreach (KeyValuePair<long, PlayerRepresentation> pair in reps) {
                PlayerRepresentation rep = pair.Value;
                if (rep == null || rep.repRoot == null || rep.colliders == null || rep.colliders.Length == 0 || rep.Parked)
                    continue;

                try { GuardRep(pair.Key, rep); }
                catch (Exception e) { EntangleLogger.Warn("[RepCollision] " + e.Message); }
            }

            if (platformCache.Count > 2048)
                platformCache.Clear();
            if (buttonCache.Count > 2048)
                buttonCache.Clear();
            if (skipCache.Count > 4096)
                skipCache.Clear();
        }

        static void GuardRep(long id, PlayerRepresentation rep) {
            RepPairs state;
            if (!pairs.TryGetValue(id, out state)) {
                state = new RepPairs();
                pairs[id] = state;
            }
            if (!ReferenceEquals(state.colliders, rep.colliders) || Time.time >= state.repairAt) {
                state.colliders = rep.colliders;
                state.handled.Clear();
                // Spread out, so several reps don't all repair in the same scan.
                state.repairAt = Time.time + repairInterval + (int)(id & 7) * scanInterval;
            }

            Scan(rep, state, rep.repRoot.position);
            if (rep.repTransforms[0] != null)
                Scan(rep, state, rep.repTransforms[0].position);
        }

        static void Scan(PlayerRepresentation rep, RepPairs state, Vector3 centre) {
            int count;
            try { count = Physics.OverlapSphereNonAlloc(centre, scanRadius, overlapBuffer); }
            catch { return; }

            for (int i = 0; i < count; i++) {
                Collider other = overlapBuffer[i];
                if (other == null)
                    continue;

                Rigidbody body = other.attachedRigidbody;
                if (body == null)
                    continue;

                int key = body.GetInstanceID();
                if (state.handled.Contains(key))
                    continue;

                if (System.Diagnostics.Stopwatch.GetTimestamp() - scanStart > scanBudgetTicks)
                    return;

                bool skip;
                if (!skipCache.TryGetValue(key, out skip)) {
                    // Out of budget: left for the next scan.
                    if (classifyBudget <= 0)
                        continue;
                    classifyBudget--;
                    skip = IsNpc(body) || IsRepOwned(body) || IsLocalPlayer(body);
                    skipCache[key] = skip;
                }
                state.handled.Add(key);
                if (skip)
                    continue;

                if (!enabled && !IsPlatform(body) && !IsButton(body))
                    continue;

                IgnoreSolid(rep, body);
            }
        }

        static void IgnoreSolid(PlayerRepresentation rep, Rigidbody body) {
            Collider[] theirs;
            try { theirs = body.GetComponentsInChildren<Collider>(); }
            catch { return; }
            Collider[] mine = rep.colliders;
            if (theirs == null || mine == null)
                return;

            for (int t = 0; t < theirs.Length; t++) {
                Collider other = theirs[t];
                try {
                    if (!other || other.isTrigger || !other.enabled)
                        continue;
                }
                catch { continue; }
                for (int m = 0; m < mine.Length; m++) {
                    Collider own = mine[m];
                    try {
                        if (own && !own.isTrigger && own.enabled && own.gameObject.activeInHierarchy)
                            Physics.IgnoreCollision(own, other, true);
                    }
                    catch { }
                }
            }
        }

        // Another player's press reaches us as a press event. Their rep pushing our copy of the button
        // as well pressed it twice, and a toggle like the cart went and came straight back.
        static bool IsButton(Rigidbody body) {
            int key = body.GetInstanceID();
            bool button;
            if (buttonCache.TryGetValue(key, out button))
                return button;
            try { button = body.GetComponentInParent<StressLevelZero.Interaction.ButtonToggle>() != null; }
            catch { button = false; }
            buttonCache[key] = button;
            return button;
        }

        // Something a rep stands on or rides that moves by itself: a lift moved by script, or a body
        // sliding on a joint like the cart or the claw's arm. A rep's pose lags a little behind it, so
        // colliding just shoves and shakes it. A hinged / kinematic door is NOT a platform: treating
        // every isKinematic body as one made reps walk straight through closed doors.
        public static bool IsPlatform(Rigidbody body) {
            int key = body.GetInstanceID();
            bool platform;
            if (platformCache.TryGetValue(key, out platform))
                return platform;

            platform = false;
            try { platform = HeldToWorld(body, 0, true); }
            catch { platform = false; }

            platformCache[key] = platform;
            return platform;
        }

        /// <summary>Kept in place by the level (kinematic, or jointed to something that is), sliding or
        /// swinging. Such things are never adopted as loose props.</summary>
        public static bool IsHeldToWorld(Rigidbody body) {
            try { return HeldToWorld(body, 0, false); }
            catch { return false; }
        }

        const int MaxChain = 8;

        static bool HeldToWorld(Rigidbody body, int depth, bool slidingOnly) {
            if (depth >= MaxChain)
                return false;

            if (body.isKinematic) {
                // Loose-prop path: kinematic means world-held (never adopt as a free syncable).
                if (!slidingOnly)
                    return true;

                // Platform path (Fusion ideology): kinematic alone is not enough. Doors, panels, and
                // other static-ish kinematics must stay solid so remotes can body-block against them.
                // Only scripted movers (lifts) or sliding/fixed world mounts count as platforms.
                if (IsScriptedPlatform(body))
                    return true;
                // Fall through to joint checks below.
            }

            Joint[] joints = body.GetComponents<Joint>();
            for (int j = 0; j < joints.Length; j++) {
                Joint joint = joints[j];
                if (!joint)
                    continue;
                Rigidbody anchor = joint.connectedBody;
                if (anchor == null || anchor.isKinematic) {
                    // HingeJoint-to-world (doors) must NOT match here when slidingOnly.
                    if (!slidingOnly || Slides(joint) || (anchor != null && joint.TryCast<FixedJoint>() != null))
                        return true;
                    continue;
                }
                if (anchor == body || IsLocalPlayer(anchor) || IsNpc(anchor) || IsRepOwned(anchor))
                    continue;
                if (HeldToWorld(anchor, depth + 1, slidingOnly))
                    return true;
            }
            return false;
        }

        /// <summary>Level movers that should ignore lagging reps (lifts), not ordinary doors.</summary>
        static bool IsScriptedPlatform(Rigidbody body) {
            try {
                Transform t = body.transform;
                if (t.GetComponentInParent<ElevatorPowerable>() != null)
                    return true;
                if (t.GetComponentInParent<elevator_controller>() != null)
                    return true;
                if (t.GetComponentInParent<PressureElevator>() != null)
                    return true;
                if (t.GetComponentInParent<GrabbyElevator>() != null)
                    return true;
                if (t.GetComponentInParent<ZombieElevator>() != null)
                    return true;
            }
            catch { }
            return false;
        }

        static bool Slides(Joint joint) {
            ConfigurableJoint configurable = joint.TryCast<ConfigurableJoint>();
            if (configurable == null)
                return false;
            return configurable.xMotion != ConfigurableJointMotion.Locked
                || configurable.yMotion != ConfigurableJointMotion.Locked
                || configurable.zMotion != ConfigurableJointMotion.Locked;
        }

        static bool IsNpc(Rigidbody body) {
            try {
                return body.GetComponentInParent<AIBrain>() != null
                    || body.GetComponentInParent<PuppetMasta.PuppetMaster>() != null
                    || PooleeSyncable.IsNpcBody(body.transform);
            }
            catch { return false; }
        }

        static bool LocalRigReady() {
            try {
                RigManager rig = Entanglement.Data.PlayerScripts.playerRig;
                return rig && rig.physicsRig && rig.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        public static bool IsLocalPlayer(Rigidbody body) {
            try {
                RigManager rig = Entanglement.Data.PlayerScripts.playerRig;
                if (rig && body.transform.IsChildOf(rig.transform))
                    return true;
                // The rig reference can lag a load behind; any player rig here that isn't a rep is ours.
                return body.GetComponentInParent<RigManager>() != null && !IsRepOwned(body);
            }
            catch { return false; }
        }

        public static bool IsRepOwned(Rigidbody body) {
            try {
                Transform t = body.transform;
                if (t.root && t.root.name.StartsWith("PlayerRep."))
                    return true;
                foreach (PlayerRepresentation rep in PlayerRepresentation.representations.Values) {
                    if (rep == null || rep.repRoot == null)
                        continue;
                    if (t.IsChildOf(rep.repRoot))
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}
