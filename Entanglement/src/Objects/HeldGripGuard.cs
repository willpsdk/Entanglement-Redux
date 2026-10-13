using System;
using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.Interaction;
using StressLevelZero.Props.Weapons;

using Entanglement.Data;
using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.Objects {
    public static class HeldGripGuard {
        const float Interval = 0.1f;
        const float HandReach = 0.15f;

        static float next;
        static readonly Dictionary<int, Grip> switchedOff = new Dictionary<int, Grip>();
        static readonly Dictionary<int, Grip> wanted = new Dictionary<int, Grip>();
        static readonly HashSet<int> heldHere = new HashSet<int>();
        static readonly Dictionary<int, Grip[]> gripsOf = new Dictionary<int, Grip[]>();
        static readonly List<int> scratch = new List<int>();

        public static void OnSceneCleanup() {
            switchedOff.Clear();
            wanted.Clear();
            gripsOf.Clear();
            next = 0f;
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby) {
                // Left the lobby: everything back on.
                if (switchedOff.Count > 0) {
                    wanted.Clear();
                    Apply();
                }
                return;
            }
            if (Time.time < next)
                return;
            next = Time.time + Interval;

            wanted.Clear();
            NoteHeldHere();

            long me = SteamIntegration.currentUserId;
            foreach (Syncable syncable in ObjectSync.syncedObjects.Values) {
                if (syncable == null || syncable.ownerQueue.Count == 0)
                    continue;
                try { Consider(syncable, me); }
                catch { }
            }

            Apply();

            if (gripsOf.Count > 256)
                gripsOf.Clear();
        }

        static void Consider(Syncable syncable, long me) {
            if (!syncable)
                return;
            List<long> holders = syncable.ownerQueue;
            bool someoneElse = false;
            for (int i = 0; i < holders.Count; i++) {
                if (holders[i] != me) {
                    someoneElse = true;
                    break;
                }
            }
            if (!someoneElse)
                return;

            TransformSyncable body = syncable.TryCast<TransformSyncable>();
            if (!body)
                return;

            Grip[] grips = GripsOf(body);

            // Their gun: its handle is theirs.
            Gun gun = body._CachedGun;
            if (gun) {
                Grip handle = gun.triggerGrip;
                if (handle)
                    Want(handle);
            }

            // Whichever grip each of their hands is on.
            if (grips.Length == 0)
                return;
            for (int i = 0; i < holders.Count; i++) {
                long holder = holders[i];
                if (holder == me)
                    continue;
                PlayerRepresentation rep;
                if (!PlayerRepresentation.representations.TryGetValue(holder, out rep) || rep == null || rep.Parked)
                    continue;
                WantNearest(grips, rep.repTransforms[1]);
                WantNearest(grips, rep.repTransforms[2]);
            }
        }

        static Grip[] GripsOf(TransformSyncable body) {
            int key = body.GetInstanceID();
            Grip[] grips;
            if (gripsOf.TryGetValue(key, out grips))
                return grips;

            List<Grip> kept = new List<Grip>();
            try {
                foreach (Grip grip in body.GetComponentsInChildren<Grip>(true)) {
                    if (grip && !grip.GetComponentInParent<Magazine>())
                        kept.Add(grip);
                }
            }
            catch { }
            grips = kept.ToArray();
            gripsOf[key] = grips;
            return grips;
        }

        static void WantNearest(Grip[] grips, Transform hand) {
            if (!hand)
                return;
            Vector3 at = hand.position;
            Grip best = null;
            float bestDist = HandReach * HandReach;
            for (int i = 0; i < grips.Length; i++) {
                Grip grip = grips[i];
                if (!grip)
                    continue;
                float dist = (grip.transform.position - at).sqrMagnitude;
                if (dist <= bestDist) {
                    bestDist = dist;
                    best = grip;
                }
            }
            if (best)
                Want(best);
        }

        static void Want(Grip grip) {
            int key = grip.GetInstanceID();
            // Never under our own hand: switching it off would pull the grip out of it.
            if (heldHere.Contains(key))
                return;
            wanted[key] = grip;
        }

        static void NoteHeldHere() {
            heldHere.Clear();
            NoteHeld(PlayerScripts.playerLeftHand);
            NoteHeld(PlayerScripts.playerRightHand);
        }

        static void NoteHeld(Hand hand) {
            try {
                if (!hand || !hand.m_CurrentAttachedObject)
                    return;
                Grip grip = Grip.Cache.Get(hand.m_CurrentAttachedObject);
                if (grip)
                    heldHere.Add(grip.GetInstanceID());
            }
            catch { }
        }

        static void Apply() {
            // Back on: no longer theirs, or gone.
            scratch.Clear();
            foreach (KeyValuePair<int, Grip> pair in switchedOff) {
                if (!wanted.ContainsKey(pair.Key))
                    scratch.Add(pair.Key);
            }
            for (int i = 0; i < scratch.Count; i++) {
                Grip grip = switchedOff[scratch[i]];
                switchedOff.Remove(scratch[i]);
                try {
                    if (grip && grip.isInteractionDisabled)
                        grip.EnableInteraction();
                }
                catch { }
            }

            // Off: theirs now, and on until now.
            foreach (KeyValuePair<int, Grip> pair in wanted) {
                if (switchedOff.ContainsKey(pair.Key))
                    continue;
                Grip grip = pair.Value;
                try {
                    if (!grip || grip.isInteractionDisabled)
                        continue;
                    grip.DisableInteraction();
                    switchedOff[pair.Key] = grip;
                }
                catch { }
            }
        }
    }
}
