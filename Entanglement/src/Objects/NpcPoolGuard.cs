using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.AI;
using StressLevelZero.Pool;

using Entanglement.Network;

namespace Entanglement.Objects
{
    public static class NpcPoolGuard
    {
        const int MaxPoolSize = 32;
        const int FreeBodiesAt = 24;

        static readonly Dictionary<int, PoolMode> original = new Dictionary<int, PoolMode>();
        static readonly Dictionary<int, int> recentRespawns = new Dictionary<int, int>();

        const int StormRespawns = 8;
        const float StormHold = 10f;
        static readonly Dictionary<int, float> stormUntil = new Dictionary<int, float>();

        /// <summary>True while this pool keeps having its NPCs taken back as fast as they come out.</summary>
        public static bool Storming(Pool pool) {
            try {
                if (!pool)
                    return false;
                float until;
                if (!stormUntil.TryGetValue(pool.GetInstanceID(), out until))
                    return false;
                return Time.unscaledTime < until;
            }
            catch { return false; }
        }
        static float windowStart;
        static bool warnedStorm;

        public static void OnSceneCleanup() {
            original.Clear();
            recentRespawns.Clear();
            stormUntil.Clear();
            warnedStorm = false;
            npcPools.Clear();
            nextSweep = 0f;
        }

        static readonly List<Pool> npcPools = new List<Pool>();
        static float nextSweep;
        const float SweepInterval = 3f;
        const float SettleBeforeSweep = 1f;

        static void Remember(Pool pool) {
            for (int i = 0; i < npcPools.Count; i++) {
                if (npcPools[i] == pool)
                    return;
            }
            npcPools.Add(pool);
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || !Node.isServer || SceneLoader.loading)
                return;
            if (Time.realtimeSinceStartup < nextSweep)
                return;
            nextSweep = Time.realtimeSinceStartup + SweepInterval;

            int registered = 0;
            for (int p = npcPools.Count - 1; p >= 0; p--) {
                Pool pool = npcPools[p];
                bool live;
                try { live = pool; }
                catch { live = false; }
                if (!live) {
                    npcPools.RemoveAt(p);
                    continue;
                }

                Il2CppSystem.Collections.Generic.List<Poolee> spawned;
                try { spawned = pool._spawnedObjects; }
                catch { continue; }
                if (spawned == null)
                    continue;

                for (int i = 0; i < spawned.Count; i++) {
                    try {
                        Poolee poolee = spawned[i];
                        if (!poolee)
                            continue;
                        GameObject go = poolee.gameObject;
                        if (!go.activeInHierarchy || PooleeSyncable._Cache.Get(go))
                            continue;
                        // Give the normal path its three frames first.
                        if (Time.time - poolee.timeSpawned < SettleBeforeSweep)
                            continue;
                        if (TurretSync.IsTurret(go)) {
                            TurretSync.BindByPlace(go);
                            if (PooleeSyncable._Cache.Get(go))
                                registered++;
                        }
                        else if (Entanglement.Patching.InstantiatePatch.RegisterHostSpawn(go))
                            registered++;
                    }
                    catch { }
                }
            }

            if (registered > 0)
                NpcTrace.Log("pool sweep registered " + registered + " NPC(s) that had never been sent to anyone");
        }

        /// <summary>Called whenever the host sees an NPC or turret come out of its pool.</summary>
        public static void OnNpcSpawned(Pool pool, bool respawn) {
            if (!pool || !SteamIntegration.hasLobby)
                return;

            Remember(pool);
            try {
                int id = pool.GetInstanceID();
                PoolMode before = pool.mode;
                if (!original.ContainsKey(id))
                    original[id] = before;

                int size = pool._pooledObjects != null ? pool._pooledObjects.Count : 0;

                if (size < MaxPoolSize && before != PoolMode.GROW) {
                    pool.mode = PoolMode.GROW;
                    NpcTrace.Log("pool " + pool.name + " (" + size + " NPCs) grows now instead of " + before);
                }
                else if (size >= MaxPoolSize && before == PoolMode.GROW && original[id] != PoolMode.GROW) {
                    PoolMode full = original[id] == PoolMode.REUSENONE ? PoolMode.REUSEOLDEST : original[id];
                    pool.mode = full;
                    EntangleLogger.Warn("[NpcPool] " + pool.name + " reached " + size + " NPCs, now " + full);
                }

                if (size >= FreeBodiesAt)
                    CorpseCleanup.FizzleOldestOf(pool);

                if (respawn)
                    CountRespawn(pool, id);
            }
            catch { }
        }

        static void CountRespawn(Pool pool, int id) {
            float now = Time.unscaledTime;
            if (now - windowStart > 2f) {
                windowStart = now;
                recentRespawns.Clear();
            }

            int count;
            recentRespawns.TryGetValue(id, out count);
            recentRespawns[id] = ++count;

            if (count >= StormRespawns)
                stormUntil[id] = now + StormHold;

            if (count == 15 && !warnedStorm) {
                warnedStorm = true;
                EntangleLogger.Warn("[NpcPool] " + pool.name + " respawned NPCs " + count
                    + " times in 2 seconds (mode " + pool.mode + "). Something is taking them back as fast as they spawn.");
            }
        }

        public static void OnSceneReady() {
            if (!SteamIntegration.hasLobby || !Node.isServer)
                return;

            try {
                Pool[] pools = Object.FindObjectsOfType<Pool>();
                if (pools == null)
                    return;
                for (int i = 0; i < pools.Length; i++) {
                    Pool pool = pools[i];
                    try {
                        if (!pool || !pool.Prefab)
                            continue;
                        if (!pool.Prefab.GetComponentInChildren<AIBrain>(true) && !TurretSync.IsTurret(pool.Prefab))
                            continue;
                        OnNpcSpawned(pool, false);
                        RegisterSpawned(pool);
                    }
                    catch { }
                }
            }
            catch { }
        }

        static void RegisterSpawned(Pool pool) {
            if (pool._spawnedObjects == null)
                return;
            for (int i = 0; i < pool._spawnedObjects.Count; i++) {
                try {
                    Poolee poolee = pool._spawnedObjects[i];
                    if (poolee && poolee.gameObject.activeInHierarchy)
                        Entanglement.Patching.InstantiatePatch.RegisterHostSpawn(poolee.gameObject);
                }
                catch { }
            }
        }
    }
}
