using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.AI;
using StressLevelZero.Pool;

using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.Representation;

using MelonLoader;

namespace Entanglement.Objects
{
    public static class SceneNpcSync
    {
        const float ScanInterval = 3f;
        const float ResolveWindow = 10f;
        const float SettleTime = 5f;

        static float nextScan;
        static readonly HashSet<int> seen = new HashSet<int>();
        static int mismatches;

        // A brain that could not be registered is looked at again on the next scans (it may still be
        // setting up), and given up on after this many, with its reason in the log.
        const int MaxAttempts = 5;
        static readonly Dictionary<int, int> attempts = new Dictionary<int, int>();

        public static void OnSceneCleanup() {
            seen.Clear();
            attempts.Clear();
            nextScan = 0f;
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || !Node.isServer || Node.activeNode == null)
                return;
            // Nobody to share them with yet; whoever joins triggers it.
            if (PlayerRepresentation.RemoteCount == 0)
                return;
            if (Time.realtimeSinceStartup < nextScan)
                return;
            nextScan = Time.realtimeSinceStartup + ScanInterval;
            if (SceneLoader.loading)
                return;
            if (SceneEventSync.SinceReady < SettleTime)
                return;

            AIBrain[] brains;
            // Shared with CorpseCleanup, which reuses this search.
            brains = CorpseCleanup.SceneBrains(1f);
            if (brains == null)
                return;

            for (int i = 0; i < brains.Length; i++) {
                try {
                    AIBrain brain = brains[i];
                    if (!brain || NpcStateSync.IsDead(brain))
                        continue;

                    int key = brain.GetInstanceID();
                    if (seen.Contains(key))
                        continue;

                    string why;
                    Transform root = NpcRoot(brain, out why);
                    if (root == null) {
                        // Spawned from a pool: the spawn sync has it.
                        if (why == null) {
                            seen.Add(key);
                            continue;
                        }
                        int tries;
                        attempts.TryGetValue(key, out tries);
                        attempts[key] = ++tries;
                        if (tries == 1 || tries >= MaxAttempts)
                            EntangleLogger.Debug("[SceneNpc] " + NpcTrace.Name(brain.gameObject) + " not synced (" + why + ")"
                                + (tries >= MaxAttempts ? ", giving up" : ", will look again"));
                        if (tries >= MaxAttempts)
                            seen.Add(key);
                        continue;
                    }

                    seen.Add(key);
                    if (PooleeSyncable._Cache.Get(root.gameObject))
                        continue;

                    Register(root);
                }
                catch { }
            }
        }

        // why: null when it belongs to a pool (not ours to register), else the reason it was refused.
        static Transform NpcRoot(AIBrain brain, out string why) {
            why = null;
            for (Transform current = brain.transform; current; current = current.parent) {
                Poolee poolee = Poolee.Cache.Get(current.gameObject);
                if (!poolee)
                    continue;
                if (FromPool(poolee))
                    return null;
                // An NPC prefab placed straight in the level (the Arena's King is one) carries a Poolee
                // like a pooled one, but no pool ever spawns it, so nothing synced it: every placed NPC
                // was skipped here (no 'placed NPC' line in a whole Oct 8 session). Its prefab is the unit.
                if (current.GetComponentsInChildren<AIBrain>(true).Length == 1 && current.GetComponentsInChildren<Rigidbody>(true).Length > 0)
                    return current;
                why = "placed pool prefab with several brains or no bodies";
                return null;
            }

            Transform[] candidates = { brain.transform, brain.transform.parent };
            for (int i = 0; i < candidates.Length; i++) {
                Transform candidate = candidates[i];
                if (!candidate)
                    continue;
                if (candidate.GetComponentsInChildren<AIBrain>(true).Length != 1)
                    continue;
                if (candidate.GetComponentsInChildren<Rigidbody>(true).Length == 0)
                    continue;
                return candidate;
            }
            why = "no part of it has exactly one brain and some bodies";
            return null;
        }

        static bool FromPool(Poolee poolee) {
            try { return poolee.pool != null; }
            catch { return true; }
        }

        static void Register(Transform root) {
            GameObject go = root.gameObject;
            Rigidbody[] bodies = PooleeSyncable.SyncBodies(go);
            if (bodies.Length == 0 || bodies.Length > 255)
                return;

            ushort id = ObjectSync.GetNextObjectIdBlock(bodies.Length);
            PooleeSyncable poolee = Bind(go, bodies, id, (byte)bodies.Length);

            SceneEventSync.SendEventPath(SceneEventType.NpcBind, go.transform.GetFullPath() + "|" + bodies.Length, id);
            SpawnCatchUp.QueuePoseForEveryone(poolee);

            NpcTrace.Log("placed NPC " + go.name + " #" + id + " bodies=" + bodies.Length + " is synced now");
            NpcStateSync.PublishDeath(go, id);
        }

        static PooleeSyncable Bind(GameObject go, Rigidbody[] bodies, ushort id, byte count) {
            long host = SteamIntegration.lobbyOwnerId;
            for (ushort i = 0; i < count; i++) {
                Rigidbody rb = bodies[i];
                if (!rb)
                    continue;
                ushort thisId = (ushort)(id + i);

                // A body a grab already synced keeps its component and takes the NPC's id.
                TransformSyncable existing = TransformSyncable.cache.GetOrAdd(rb.gameObject);
                if (existing) {
                    ObjectSync.MoveSyncable(existing, thisId);
                    existing.ClearOwner();
                    existing.TrySetStale(host);
                }
                else {
                    TransformSyncable.CreateSync(host, ComponentCacheExtensions.m_RigidbodyCache.GetOrAdd(rb.gameObject), thisId);
                }
                ObjectSync.lastId = thisId;
            }

            PooleeSyncable poolee = PooleeSyncable.Attach(go);
            poolee.pathAddressed = true;
            poolee.AssignId(id, count);
            poolee.BindTransforms(go.GetComponentsInChildren<TransformSyncable>(true));
            return poolee;
        }

        /// <summary>Client: the host has numbered a placed NPC. "path|bodies", arg = first id.</summary>
        public static void Apply(ushort id, string objectPath) {
            // A pooled turret, named by where it stands: "~name@x,y,z|bodies|state".
            if (objectPath.Length > 1 && objectPath[0] == '~') {
                string[] parts = objectPath.Split('|');
                int bodies, state = -1;
                if (parts.Length < 2 || !int.TryParse(parts[1], out bodies))
                    return;
                if (parts.Length > 2 && !int.TryParse(parts[2], out state))
                    state = -1;
                TurretSync.ApplyPlaceBind(id, parts[0], bodies, state);
                return;
            }

            int bar = objectPath.LastIndexOf('|');
            int count;
            if (bar <= 0 || !int.TryParse(objectPath.Substring(bar + 1), out count) || count <= 0 || count > 255)
                return;
            string path = objectPath.Substring(0, bar);

            // Owner changes and deaths for this NPC wait until it is bound (NpcPending).
            NpcPending.BeginBuild(id);
            MelonCoroutines.Start(CoApply(id, path, count));
        }

        static IEnumerator CoApply(ushort id, string path, int count) {
            try {
                IEnumerator resolve = CoResolveAndBind(id, path, count);
                while (resolve.MoveNext())
                    yield return resolve.Current;
            }
            finally {
                NpcPending.EndBuild(id);
            }
        }

        static IEnumerator CoResolveAndBind(ushort id, string path, int count) {
            ushort scene = SceneEventSync.CurrentSceneTag();
            float waited = 0f;
            Transform root = path.GetFromFullPath();

            // Its area may not have loaded here yet.
            while (!root && waited < ResolveWindow) {
                float step = 0f;
                while (step < 0.25f) {
                    yield return null;
                    if (!SceneLoader.loading)
                        step += Time.unscaledDeltaTime;
                }
                waited += step;
                if (SceneEventSync.CurrentSceneTag() != scene)
                    yield break;
                root = path.GetFromFullPath();
            }
            if (!root)
                yield break;

            try {
                GameObject go = root.gameObject;
                PooleeSyncable already = PooleeSyncable._Cache.Get(go);
                if (already)
                    yield break;

                Rigidbody[] bodies = PooleeSyncable.SyncBodies(go);
                int bind = Math.Min(bodies.Length, count);
                if (bind <= 0) {
                    mismatches++;
                    if (mismatches <= 10)
                        EntangleLogger.Warn("[SceneNpc] " + go.name + " has no bodies here, so it stays local");
                    yield break;
                }
                if (bodies.Length != count) {
                    mismatches++;
                    if (mismatches <= 10)
                        EntangleLogger.Warn("[SceneNpc] " + go.name + " has " + bodies.Length + " bodies here but " + count
                            + " on the host; binding the first " + bind);
                }

                Bind(go, bodies, id, (byte)bind);
                NpcTrace.Log("placed NPC " + go.name + " #" + id + " now follows the host");
            }
            catch (Exception e) { EntangleLogger.Debug("[SceneNpc] Could not bind " + path + ": " + e.Message); }
        }
    }
}
