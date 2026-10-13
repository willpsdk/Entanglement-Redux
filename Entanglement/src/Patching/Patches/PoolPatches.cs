using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Objects;
using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.Data;

using StressLevelZero.Pool;
using StressLevelZero.AI;
using StressLevelZero.Zones;
using StressLevelZero.Arena;
using StressLevelZero.Props;

using HarmonyLib;

using UnityEngine;

using PuppetMasta;

using MelonLoader;

namespace Entanglement.Patching {

    public static class Pool_Settings {
        public static List<Poolee> GetAllPoolees(this Pool pool) {
            List<Poolee> poolees;
            if (!ObjectSync.poolPairs.TryGetValue(pool, out poolees)) {
                poolees = new List<Poolee>();
                ObjectSync.poolPairs.Add(pool, poolees);
            }
            return poolees;
        }

        public static float GetRelativeSpawnTime(this Poolee poolee) {
            if (!poolee.pool) return -1f;
            float timeSpawned = poolee.gameObject.activeInHierarchy ? poolee.timeSpawned : 0f;
            return (float)poolee.pool._timeOfLastSpawn - timeSpawned;
        }

        public static Poolee GetAccuratePoolee(this Pool pool, int index, float relativeTime = -1f) {
            List<Poolee> poolees = pool.GetAllPoolees();
            if (poolees.Count <= 0) return null;

            Poolee toReturn = null;

            int pooleeCount = poolees.Count;

            if (relativeTime < 0f)
                return poolees[Math.Min(index, pooleeCount - 1)];
            else {
                int closestIndex = -1;
                float closestTime = -1f;
                for (int thisIndex = 0; thisIndex < pooleeCount; thisIndex++) {
                    Poolee thisPoolee = poolees[thisIndex];
                    float thisTime = thisPoolee.GetRelativeSpawnTime();

                    if (Mathf.Abs(thisTime - relativeTime) > Mathf.Abs(closestTime - relativeTime))
                        continue;

                    if (Math.Abs(thisIndex - index) > Math.Abs(closestIndex - index))
                        continue;

                    closestIndex = thisIndex;
                    closestTime = thisTime;
                    toReturn = thisPoolee;
                }
            }

            return toReturn;
        }
    }

    [HarmonyPatch(typeof(Poolee), "OnCleanup")]
    public static class CleanupPatch {
        public static void Prefix(Poolee __instance, ref SimplifiedTransform __state) {
            __state = default(SimplifiedTransform);
            try { __state = new SimplifiedTransform(__instance.transform); }
            catch (Exception e) { PatchGuard.Report("PoolPatches.CleanupPrefix", e); }
        }

        public static void Postfix(Poolee __instance, ref SimplifiedTransform __state) {
            try { __state.Apply(__instance.transform); }
            catch (Exception e) { PatchGuard.Report("PoolPatches.CleanupPostfix", e); }
        }
    }

    [HarmonyPatch(typeof(Pool), "InstantiatePoolee")]
    public static class InstantiatePatch {
        public static bool Prefix(Pool __instance) {
            if (!__instance.Prefab)
                return false;
            return true;
        }

        public static void Postfix(Pool __instance, Poolee __result, Vector3 position, Quaternion rotation) {
            if (__instance.IsBlacklisted())
                return;

            try {
                if (__instance._pooledObjects.Contains(__result)) {
                    List<Poolee> poolees;

                    if (!ObjectSync.poolPairs.TryGetValue(__instance, out poolees))
                    {
                        poolees = new List<Poolee>();
                        if (ObjectSync.poolPairs.ContainsKey(__instance))
                            ObjectSync.poolPairs[__instance] = poolees;
                        else
                            ObjectSync.poolPairs.Add(__instance, poolees);
                    }

                    if (!poolees.Contains(__result)) { 
                        poolees.Add(__result);
                        __result.onSpawnDelegate = Il2CppSystem.Delegate.Combine(__result.onSpawnDelegate, (Il2CppSystem.Action<GameObject>)((go) => { OnSpawn(go, __instance); })).Cast<Il2CppSystem.Action<GameObject>>();
                        MelonCoroutines.Start(CatchMissedFirstSpawn(__result, __instance));
                    }
                }
            } catch { }
        }

        static IEnumerator CatchMissedFirstSpawn(Poolee poolee, Pool pool) {
            yield return null;
            if (!poolee || !pool)
                yield break;
            GameObject go = poolee.gameObject;
            if (!go || !go.activeInHierarchy)
                yield break;
            if (PooleeSyncable._Cache.Get(go))
                yield break;
            if (pendingSpawns.Contains(go.GetInstanceID()))
                yield break;
            OnSpawn(go, pool);
        }

        static readonly HashSet<int> pendingSpawns = new HashSet<int>();

        public static void OnSceneCleanup() {
            pendingSpawns.Clear();
            SceneNpcHost.OnSceneCleanup();
        }

        public static void OnSpawn(GameObject spawnedObject, Pool pool) {
            if (suppressingNpc)
                return;
            if (!SteamIntegration.hasLobby || SpawnManager.SpawnOverride)
                return;
            if (!spawnedObject)
                return;

            int instanceId = spawnedObject.GetInstanceID();
            if (!pendingSpawns.Add(instanceId))
                return;

            try {
                bool npc = IsSceneNpc(spawnedObject);

                // We don't want to dupe items
                var pooleeSyncable = PooleeSyncable._Cache.Get(spawnedObject);
                if (npc && Node.isServer)
                    NpcPoolGuard.OnNpcSpawned(pool, pooleeSyncable != null);
                if (npc)
                    NpcTrace.Log("pool spawned " + NpcTrace.Name(spawnedObject) + " at " + spawnedObject.transform.position
                        + (pooleeSyncable ? " (respawn of #" + pooleeSyncable.id + ", owner was " + pooleeSyncable.CurrentOwner() + ")" : " (new)"));
                if (pooleeSyncable) {
                    // A new life: what was noted about the last one (its death, its config) goes.
                    if (Node.isServer)
                        pooleeSyncable.HostRespawned();
                    bool turret = !string.IsNullOrEmpty(pooleeSyncable.placeKey) || TurretSync.IsTurret(spawnedObject);
                    if (turret) {
                        // Every machine has its own turrets, bound by place (TurretSync).
                        if (Node.isServer) {
                            pooleeSyncable.SetOwner(SteamIntegration.currentUserId);
                            // Its old place is wrong now; events wait for the new one (TurretEventSync.Send).
                            pooleeSyncable.placeKey = null;
                            TurretSync.BindLater(spawnedObject);
                        }
                        else {
                            TurretSync.Unbind(spawnedObject);
                            TurretSync.KeepLocal(spawnedObject);
                        }
                        return;
                    }

                    // Now we transfer the spawn to the host
                    if (Node.isServer) {
                        // Set us as owner
                        pooleeSyncable.SetOwner(SteamIntegration.currentUserId);

                        SpawnCatchUp.BroadcastRespawn(pooleeSyncable);
                    }
                    else if (npc) {
                        NpcTrace.Log("removed a local respawn of old copy #" + pooleeSyncable.id + ", the host sends the real one");
                        SuppressLocalNpc(spawnedObject);
                    }
                    return;
                }

                // Server spawned (---> Clients)
                if (Node.isServer) {
                    MelonCoroutines.Start(OnSpawnHost(spawnedObject, pool, instanceId));
                    return; // pendingSpawns cleared when host coroutine finishes
                }

                if (npc && TurretSync.IsTurret(spawnedObject))
                    TurretSync.KeepLocal(spawnedObject);
                else if (npc) {
                    NpcTrace.Log("removed local copy of " + NpcTrace.Name(spawnedObject) + ", the host sends the real one");
                    SuppressLocalNpc(spawnedObject);
                }
                else
                    MelonCoroutines.Start(OnSpawnClient(spawnedObject, instanceId));
            }
            finally {
                // Host coroutine keeps the lock until PooleeSyncable exists
                if (!Node.isServer || !spawnedObject || PooleeSyncable._Cache.Get(spawnedObject))
                    pendingSpawns.Remove(instanceId);
            }
        }

        public static bool IsSceneNpc(GameObject spawnedObject) {
            if (!spawnedObject)
                return false;
            if (spawnedObject.GetComponentInChildren<AIBrain>(true))
                return true;
            // Turrets have no AIBrain - without this every client keeps a local copy and they spam-spawn
            return Entanglement.Objects.TurretSync.IsTurret(spawnedObject);
        }

        static bool suppressingNpc;

        public static void SuppressLocalNpc(GameObject spawnedObject) {
            if (suppressingNpc || !spawnedObject)
                return;

            suppressingNpc = true;
            try {
                spawnedObject.SetActive(false);
                Poolee poolee = Poolee.Cache.Get(spawnedObject);
                if (poolee)
                    PooleeSyncable.DespawnPoolee(poolee);
            }
            catch { }
            finally {
                suppressingNpc = false;
            }
        }

        public static IEnumerator OnSpawnClient(GameObject spawnedObject, int instanceId) {
            try {
                if (SceneLoader.loading) {
                    while (SceneLoader.loading)
                        yield return null;
                }

                for (int i = 0; i < 2; i++) {
                    yield return null;
                    if (spawnedObject)
                        spawnedObject.SetActive(false);
                }
            }
            finally {
                pendingSpawns.Remove(instanceId);
            }
        }

        public static IEnumerator OnSpawnHost(GameObject spawnedObject, Pool pool, int instanceId = 0) {
            try {
                // Wait till we finish loading
                if (SceneLoader.loading) {
                    while (SceneLoader.loading)
                        yield return null;

                    if (!spawnedObject || !spawnedObject.activeInHierarchy)
                        yield break;
                }

                // Puppet / NPC bones are often inactive on the first frame - wait so SyncBodies sees them
                for (int i = 0; i < 3; i++) {
                    yield return null;
                    if (!spawnedObject)
                        yield break;
                }

                if (!spawnedObject || !spawnedObject.activeInHierarchy)
                    yield break;

                float waited = 0f;
                while (waited < 1f && PooleeSyncable.SyncBodies(spawnedObject).Length == 0 && !SpawnManager.IsDebris(spawnedObject.transform)) {
                    yield return null;
                    waited += Time.unscaledDeltaTime;
                    if (!spawnedObject || !spawnedObject.activeInHierarchy)
                        yield break;
                }

                // Another coroutine already synced this
                if (PooleeSyncable._Cache.Get(spawnedObject))
                    yield break;

                if (TurretSync.IsTurret(spawnedObject))
                    TurretSync.BindByPlace(spawnedObject);
                else
                    RegisterHostSpawn(spawnedObject);
            }
            finally {
                if (instanceId != 0)
                    pendingSpawns.Remove(instanceId);
            }
        }

        public static PooleeSyncable RegisterHostSpawn(GameObject spawnedObject) {
            PooleeSyncable already = PooleeSyncable._Cache.Get(spawnedObject);
            if (already)
                return already;

            if (TurretSync.IsTurret(spawnedObject)) {
                TurretSync.BindByPlace(spawnedObject);
                return PooleeSyncable._Cache.Get(spawnedObject);
            }

            // Create the sync transforms
            Rigidbody[] rbs = PooleeSyncable.SyncBodies(spawnedObject);

            // Reserve a contiguous, collision-free block for all the object's bodies
            ushort id = ObjectSync.GetNextObjectIdBlock(Math.Max(1, rbs.Length));
            byte rbCount = (byte)Math.Min(rbs.Length, 255);
            for (ushort i = 0; i < rbCount; i++)
            {
                Rigidbody rb = rbs[i];
                if (!rb)
                    continue;
                GameObject go = rb.gameObject;
                ushort thisId = (ushort)(i + id);

                TransformSyncable existingSync = TransformSyncable.cache.GetOrAdd(go);
                if (existingSync) {
                    ObjectSync.MoveSyncable(existingSync, thisId);
                    existingSync.ClearOwner();
                    existingSync.TrySetStale(SteamIntegration.lobbyOwnerId);
                }
                else {
                    TransformSyncable.CreateSync(SteamIntegration.lobbyOwnerId, ComponentCacheExtensions.m_RigidbodyCache.GetOrAdd(go), thisId);
                }

                ObjectSync.lastId = thisId;
            }

            if (rbCount == 0)
                ObjectSync.lastId = id;

            var pooleeSyncable = PooleeSyncable.Attach(spawnedObject);
            pooleeSyncable.AssignId(id, rbCount);
            pooleeSyncable.BindTransforms(spawnedObject.GetComponentsInChildren<TransformSyncable>(true));

            // Now we sync this object back to the clients
            SpawnCatchUp.Broadcast(pooleeSyncable);

            if (IsSceneNpc(spawnedObject)) {
                NpcTrace.Log("registered " + NpcTrace.Name(spawnedObject) + " #" + id + " bodies=" + rbCount
                    + " owner=" + pooleeSyncable.CurrentOwner() + " me=" + SteamIntegration.currentUserId
                    + " lobbyOwner=" + SteamIntegration.lobbyOwnerId
                    + " sent to " + Node.activeNode.connectedUsers.Count + " player(s)");
                NpcStateSync.PublishDeath(spawnedObject, id);
            }

            TurretEventSync.SendCurrentState(spawnedObject);

            return pooleeSyncable;
        }
    }

    // Whether a zone tracker may put its NPC back in the pool. ZoneTracker.Despawn itself takes a
    // bool?, which Harmony can't pass here (see NativeHook): patching it crashed any level where a
    // zone put an NPC away. Its only two callers are patched instead, ZoneTracker.OnZoneDisabled
    // below and DespawnVolume.OnTriggerEnter in StoryInteractPatches.
    public static class ZoneDespawnGuard {
        static int blocked;
        static int stormKept;

        const float JustSpawned = 1.5f;

        public static bool Allows(StressLevelZero.Zones.ZoneTracker tracker) {
            try {
                if (!SteamIntegration.hasLobby || !tracker)
                    return true;
                if (Node.isServer)
                    return HostAllows(tracker);
                PooleeSyncable npc = PooleeSyncable.SyncedNpcOf(tracker.transform);
                if (npc == null || npc.id == 0)
                    return true;
                blocked++;
                if (blocked <= 10 || blocked % 100 == 0)
                    NpcTrace.Log("kept #" + npc.id + " out of its pool here (" + blocked + " so far)");
                return false;
            }
            catch { return true; }
        }

        static bool HostAllows(StressLevelZero.Zones.ZoneTracker tracker) {
            Poolee poolee = tracker.GetComponentInParent<Poolee>();
            if (!poolee || !poolee.pool)
                return true;
            float age = Time.time - poolee.timeSpawned;
            if (age > JustSpawned || !NpcPoolGuard.Storming(poolee.pool))
                return true;
            stormKept++;
            if (stormKept <= 10 || stormKept % 100 == 0)
                EntangleLogger.Log("[NpcPool] Kept " + poolee.name + " out of its pool " + age.ToString("F2")
                    + "s after it spawned, while " + poolee.pool.name + " was respawning its NPCs over and over ("
                    + stormKept + " so far)");
            return false;
        }
    }

    [HarmonyPatch(typeof(StressLevelZero.Zones.ZoneTracker), "OnZoneDisabled")]
    public static class ZoneTrackerDisabledPatch {
        public static bool Prefix(StressLevelZero.Zones.ZoneTracker __instance) {
            if (ZoneDespawnGuard.Allows(__instance))
                return true;
            // Kept out of its pool, but the rest of OnZoneDisabled still runs: its event.
            try {
                UnityEngine.Events.UnityEvent disabled = __instance.onDisabledDelegate;
                if (disabled != null)
                    disabled.Invoke();
            }
            catch (Exception e) { PatchGuard.Report("ZoneTrackerDisabledPatch", e); }
            return false;
        }
    }

    [HarmonyPatch(typeof(ClaimedSpawner), "Start")]
    public static class ClaimedSpawnerLobbyPatch {
        public static void Prefix(ClaimedSpawner __instance) {
            if (SteamIntegration.hasLobby && __instance)
                __instance.spawnOverride = true;
        }
    }

    [HarmonyPatch(typeof(ZoneSpawner), "Spawn")]
    public static class ZoneSpawnerHostPatch {
        public static bool Prefix(ZoneSpawner __instance) => SceneNpcHost.AllowLocalSpawn(__instance);
    }

    [HarmonyPatch(typeof(ZoneSpawner), "StartSpawn")]
    public static class ZoneStartSpawnHostPatch {
        public static bool Prefix(ZoneSpawner __instance) => SceneNpcHost.AllowLocalSpawn(__instance);
    }

    [HarmonyPatch(typeof(Arena_EnemySpawner), "SpawnEnemy")]
    public static class ArenaEnemyHostPatch {
        public static bool Prefix() => SceneNpcHost.AllowLocalSpawn();
    }

    [HarmonyPatch(typeof(Arena_SpawnEnemies), "SpawnEnemies")]
    public static class ArenaWaveHostPatch {
        public static bool Prefix() => SceneNpcHost.AllowLocalSpawn();
    }

    [HarmonyPatch(typeof(Arena_SpawnEnemies), "SpawnEnemyProfile")]
    public static class ArenaProfileHostPatch {
        public static bool Prefix() => SceneNpcHost.AllowLocalSpawn();
    }

    [HarmonyPatch(typeof(Arena_SpawnEnemies), "SpawnZombieThrower")]
    public static class ArenaThrowerHostPatch {
        public static bool Prefix() => SceneNpcHost.AllowLocalSpawn();
    }

    [HarmonyPatch(typeof(Arena_SpawnEnemies), "SpawnOrEvolveClone")]
    public static class ArenaCloneHostPatch {
        public static bool Prefix() => SceneNpcHost.AllowLocalSpawn();
    }

    [HarmonyPatch(typeof(Arena_SpawnEnemies), "SpawnProtectKing")]
    public static class ArenaKingHostPatch {
        public static bool Prefix() => SceneNpcHost.AllowLocalSpawn();
    }

    [HarmonyPatch(typeof(Arena_SpawnEnemies), "SpawnFriendlyFords")]
    public static class ArenaFriendlyHostPatch {
        public static bool Prefix() => SceneNpcHost.AllowLocalSpawn();
    }

    [HarmonyPatch(typeof(Arena_SpawnEnemies), "SpawnThreeFriendlyFords")]
    public static class ArenaThreeFriendlyHostPatch {
        public static bool Prefix() => SceneNpcHost.AllowLocalSpawn();
    }

    public static class SceneNpcHost {
        public static bool AllowLocalSpawn() {
            if (!SteamIntegration.hasLobby || SpawnManager.SpawnOverride)
                return true;
            return Node.isServer;
        }

        // Turrets are the exception. Every machine spawns its own and TurretSync binds them by
        // place, so a client's turret spawners have to run or it never has any turrets at all.
        public static bool AllowLocalSpawn(ZoneSpawner spawner) => AllowLocalSpawn() || SpawnsTurrets(spawner);

        static readonly Dictionary<int, bool> turretSpawners = new Dictionary<int, bool>();

        public static void OnSceneCleanup() => turretSpawners.Clear();

        static bool SpawnsTurrets(ZoneSpawner spawner) {
            try {
                if (!spawner)
                    return false;
                int id = spawner.GetInstanceID();
                bool turret;
                if (!turretSpawners.TryGetValue(id, out turret)) {
                    StressLevelZero.Data.SpawnableObject spawnable = spawner.spawnObject;
                    turret = spawnable != null && spawnable.prefab && TurretSync.IsTurret(spawnable.prefab);
                    turretSpawners[id] = turret;
                }
                return turret;
            }
            catch { return false; }
        }
    }
}
