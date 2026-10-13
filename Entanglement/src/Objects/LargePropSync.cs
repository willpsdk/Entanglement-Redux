using System;
using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.Pool;
using StressLevelZero.VRMK;
using StressLevelZero.Rig;
using StressLevelZero.AI;

using Entanglement.Network;
using Entanglement.Extensions;

using MelonLoader;

namespace Entanglement.Objects
{
    public static class LargePropSync {
        const float MinMass = 4f;
        const float MinScale = 1.15f;

        const float ScanInterval = 10f;

        const int BodiesPerSlice = 128;

        const int MaxAutoRegistered = 24;

        static int autoRegistered;
        static bool warnedBudget;

        static readonly HashSet<int> registered = new HashSet<int>();
        static float nextScan;

        static Rigidbody[] pending;
        static int pendingIndex;
        static bool pendingMovingOnly;

        public static void OnSceneCleanup() {
            registered.Clear();
            nextScan = 0f;
            pending = null;
            pendingIndex = 0;
            autoRegistered = 0;
            warnedBudget = false;
        }

        public static void OnSceneReady() {
            if (!SteamIntegration.hasLobby || !Node.isServer)
                return;
            MelonCoroutines.Start(CoRegister());
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || !Node.isServer)
                return;

            if (Entanglement.Representation.PlayerRepresentation.RemoteCount == 0)
                return;

            // Finish the walk already in progress before starting another.
            if (pending != null) {
                StepScan();
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < nextScan)
                return;
            nextScan = now + ScanInterval;

            BeginScan(movingOnly: true, strict: false);
        }

        static bool pendingStrict;

        static void BeginScan(bool movingOnly, bool strict) {
            // Nothing to do once the budget is spent - do not even pay for the scan.
            if (autoRegistered >= MaxAutoRegistered) {
                pending = null;
                return;
            }

            try { pending = UnityEngine.Object.FindObjectsOfType<Rigidbody>(); }
            catch { pending = null; }

            pendingIndex = 0;
            pendingMovingOnly = movingOnly;
            pendingStrict = strict;
        }

        static void StepScan() {
            if (pending == null)
                return;

            int end = pendingIndex + BodiesPerSlice;
            if (end > pending.Length)
                end = pending.Length;

            for (; pendingIndex < end; pendingIndex++) {
                Rigidbody rb;
                try { rb = pending[pendingIndex]; }
                catch { continue; }

                try {
                    if (!IsCandidate(rb, pendingMovingOnly, pendingStrict))
                        continue;
                    TryRegister(rb);
                }
                catch { }

                if (autoRegistered >= MaxAutoRegistered) {
                    pending = null;
                    return;
                }
            }

            if (pendingIndex >= pending.Length)
                pending = null;
        }

        static System.Collections.IEnumerator CoRegister() {
            for (int i = 0; i < 8; i++)
                yield return null;

            if (!SteamIntegration.hasLobby || !Node.isServer)
                yield break;

            ScanAndRegister(movingOnly: false, strict: true);

            EntangleLogger.Debug("[LargeProp] Scene sweep adopted " + autoRegistered + " prop(s); "
                + ObjectSync.syncedObjects.Count + " synced object(s) total.");
        }

        // Used by the one-shot scene sweep, which wants the whole level in one pass.
        static void ScanAndRegister(bool movingOnly, bool strict) {
            BeginScan(movingOnly, strict);
            while (pending != null)
                StepScan();
        }

        static bool IsCandidate(Rigidbody rb, bool movingOnly, bool strict) {
            if (!rb || rb.isKinematic)
                return false;

            // Asleep bodies are the overwhelming majority - reject them before anything else.
            if (movingOnly) {
                Vector3 v = rb.velocity;
                Vector3 a = rb.angularVelocity;
                if (v.sqrMagnitude < 0.04f && a.sqrMagnitude < 0.04f)
                    return false;
            }

            float mass = rb.mass;
            Vector3 s = rb.transform.lossyScale;
            float maxScale = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            bool big = mass >= MinMass || maxScale >= MinScale;

            GameObject go = rb.gameObject;
            if (!go)
                return false;

            bool nameHit = NameHit(go.name);

            // Nothing below can qualify, so stop before the expensive checks.
            if (!big && !nameHit)
                return false;

            int id = go.GetInstanceID();
            if (registered.Contains(id))
                return false;

            if (go.IsBlacklisted())
                return false;

            if (TransformSyncable.cache.Get(go))
                return false;

            try {
                if (RepCollisionGuard.IsHeldToWorld(rb))
                    return false;
            }
            catch { }

            // A heavy plank flying out of a crate is still that machine's own piece.
            if (SpawnManager.IsDebris(rb.transform))
                return false;

            try {
                if (go.GetComponentInParent<RigManager>() || go.GetComponentInParent<PhysBody>())
                    return false;
            }
            catch { }

            try {
                if (go.GetComponentInParent<AIBrain>())
                    return false;
                if (go.GetComponentInParent<PuppetMasta.PuppetMaster>())
                    return false;
            }
            catch { }

            bool sphere = false;
            try { sphere = go.GetComponent<SphereCollider>() != null; }
            catch { }

            if (strict) {
                if (nameHit && (big || sphere))
                    return true;
                if (mass >= 50f && maxScale >= 2f)
                    return true;
                return false;
            }

            if (nameHit && (big || sphere))
                return true;
            if (sphere && big)
                return true;
            if (mass >= 20f && maxScale >= 1.5f)
                return true;

            return false;
        }

        // Case-insensitive match without allocating a lowercase copy of every object's name.
        static bool NameHit(string name) {
            if (string.IsNullOrEmpty(name))
                return false;

            return name.IndexOf("ball", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("soccer", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("sphere", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("orb", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("massive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void TryRegister(Rigidbody rb) {
            if (!rb || !Node.isServer || Node.activeNode == null)
                return;

            GameObject go = rb.gameObject;
            if (!go)
                return;

            if (autoRegistered >= MaxAutoRegistered) {
                if (!warnedBudget) {
                    warnedBudget = true;
                    EntangleLogger.Warn("[LargeProp] Hit the " + MaxAutoRegistered
                        + " auto-synced prop budget for this scene; ignoring the rest. "
                        + "Grabbing or hitting a prop still syncs it normally.");
                }
                return;
            }

            int id = go.GetInstanceID();
            if (!registered.Add(id))
                return;

            if (TransformSyncable.cache.Get(go))
                return;

            autoRegistered++;

            string rootName = null;
            short spawnIndex = -1;
            float spawnTime = -1f;
            try {
                Poolee poolee = go.GetComponentInParent<Poolee>();
                if (poolee && poolee.gameObject)
                    rootName = poolee.gameObject.name;
            }
            catch { }

            ushort objectId = ObjectSync.GetNextObjectId();
            TransformSyncable.CreateSync(SteamIntegration.currentUserId, rb, objectId);

            TransformCreateMessageData createSync = new TransformCreateMessageData() {
                ownerId = SteamIntegration.currentUserId,
                objectId = objectId,
                callbackIndex = 0,
                objectPath = rb.transform.GetFullPath(rootName),
                spawnIndex = spawnIndex,
                spawnTime = spawnTime,
                enqueueOwner = false,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCreate, createSync);
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }
    }
}
