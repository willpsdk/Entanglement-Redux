using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnhollowerRuntimeLib;

using UnityEngine;

using StressLevelZero.Interaction;
using StressLevelZero.Pool;
using StressLevelZero.Props.Weapons;
using StressLevelZero.Combat;
using StressLevelZero.Data;

using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.Data;
using Entanglement.Patching;

using MelonLoader;

namespace Entanglement.Objects
{
    public static class ObjectSync {
        public static Dictionary<Pool, List<Poolee>> poolPairs = new Dictionary<Pool, List<Poolee>>(new UnityComparer());

        public static Dictionary<ushort, Syncable> syncedObjects = new Dictionary<ushort, Syncable>(new UnityComparer());
        public static List<Syncable> queuedSyncs = new List<Syncable>();
        public static ushort lastId = 0;

        public static void OnCleanup() {
            try { RemoveObjects(); } catch { }
            lastId = 0;
        }

        public static void RemoveObjects() {
            foreach (Syncable syncable in syncedObjects.Values) {
                try { syncable.Cleanup(); }
                catch { }
            }
            syncedObjects.Clear();
            queuedSyncs.Clear();

            TransformSyncable.cache = new CustomComponentCache<TransformSyncable>();
            TransformSyncable.DestructCache = new CustomComponentCache<TransformSyncable>();
        }

        public static void MoveSyncable(Syncable syncable, ushort newId) {
            syncedObjects.Remove(syncable.objectId);
            syncedObjects.Remove(newId);

            syncedObjects.Add(newId, syncable);

            syncable.objectId = newId;
        }

        public static void RegisterSyncable(Syncable syncable, ushort objectId) {
            if (syncedObjects.ContainsKey(objectId)) {
                if (syncedObjects[objectId] != syncable) syncedObjects[objectId].Cleanup();
                syncedObjects.Remove(objectId);
            }

            syncedObjects.Add(objectId, syncable);
            lastId = objectId;
        }

        // Scans for a free id instead of blindly incrementing. A bare lastId++ eventually
        // lands on an id a live object still holds, and RegisterSyncable then evicts that
        // object to reuse the id - which is how a grabbed magazine snapped onto a floor one
        public static ushort GetNextObjectId() {
            ushort start = lastId;
            do {
                lastId++;
                if (lastId == 0) lastId = 1; // 0 is reserved (host / unset)
                if (!syncedObjects.ContainsKey(lastId))
                    return lastId;
            } while (lastId != start);

            return lastId; // Table full (65k live objects), extremely unlikely
        }

        // Contiguous free block for multi-body spawns, whose rigidbodies take sequential ids.
        // Returns the first id; every id in [first, first + count) is guaranteed free
        public static ushort GetNextObjectIdBlock(int count) {
            if (count <= 1)
                return GetNextObjectId();

            ushort start = lastId;
            do {
                lastId++;
                if (lastId == 0) lastId = 1;

                bool blockFree = true;
                for (int i = 0; i < count; i++) {
                    ushort id = (ushort)(lastId + i);
                    if (id == 0 || syncedObjects.ContainsKey(id)) {
                        blockFree = false;
                        break;
                    }
                }

                if (blockFree)
                    return lastId;
            } while (lastId != start);

            return lastId;
        }

        public static ushort QueueSyncable(Syncable syncable) {
            // IEqualityComparer doesnt work for lists or im dumb but whatever
            if (queuedSyncs.Has(syncable)) {
                int index = queuedSyncs.FindIndex(o => o == syncable);
                queuedSyncs.RemoveAt(index);
            }

            queuedSyncs.Add(syncable);
            return (ushort)queuedSyncs.IndexOf(syncable);
        }

        public static bool TryGetSyncable(ushort id, out Syncable syncable) => syncedObjects.TryGetValue(id, out syncable);

        public static void GetPooleeData(Transform obj, out Rigidbody[] rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime) {
            Transform objRoot = obj.transform.root;

            overrideRootName = null;
            spawnIndex = -1;
            spawnTime = -1f;
            rigidbodies = default;

            Magazine magazine = Magazine.Cache.Get(objRoot.gameObject);
            if (magazine) {
                SpawnableObject spawnable = magazine.magazineData.spawnableObject;

                if (!spawnable) return;

                spawnTime = 0f;
                spawnIndex = 0;
                overrideRootName = spawnable.title;

                rigidbodies = objRoot.GetChildBodies();
                return;
            }

            Poolee objPoolee = Poolee.Cache.Get(objRoot.gameObject);

            if (objPoolee) {
                Pool objPool = objPoolee.pool;
            
                if (objPool) {
                    List<Poolee> allPoolees = objPool.GetAllPoolees();
                    spawnIndex = (short)allPoolees.FindIndex(o => o == objPoolee);
                    spawnTime = objPoolee.GetRelativeSpawnTime();

                    overrideRootName = objPool.name.Remove(0, 7);
                }
                rigidbodies = objPoolee.transform.GetChildBodies();
            }
            else
                rigidbodies = obj.transform.GetJointedBodies();
        }

        public static bool CheckForInstantiation(GameObject prefab, string poolName) {
            switch (poolName.ToLower()) {
                case "nimbus gun":
                case "utility gun":
                    return true;
            }
            
            Magazine magazineScript = prefab.GetComponent<Magazine>();
            if (magazineScript)
                return true;
            else
                return false;
        }

        public static void OnGripAttached(GameObject grip) {
            if (!SteamIntegration.hasLobby)
                return;

            MelonCoroutines.Start(OnGripValid(grip));
        }

        // We wait two frames so custom gun magazines don't spawn regular ones at 0, 0, 0 too
        public static IEnumerator OnGripValid(GameObject grip) {
            yield return null;
            yield return null;

            if (!grip || !grip.activeInHierarchy)
                yield break;

            if (grip.IsBlacklisted()) yield break;

            Rigidbody[] rigidbodies = null;

            GetPooleeData(grip.transform, out rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime);

            for (int i = 0; i < rigidbodies.Length; i++) SyncUtilities.UpdateBodyAttached(rigidbodies[i], overrideRootName, spawnIndex, spawnTime);
        }

        // Punched/shoved bodies sync as a grab-and-instant-release, already-synced ones
        // are left to their owner
        public static void OnBodyPunched(GameObject hit) {
            if (!SteamIntegration.hasLobby)
                return;

            if (!hit || hit.IsBlacklisted())
                return;

            MelonCoroutines.Start(OnPunchValid(hit));
        }

        public static IEnumerator OnPunchValid(GameObject hit) {
            yield return null;

            if (!hit || !hit.activeInHierarchy)
                yield break;

            // Punches are indiscriminate where grips are deliberate - never sync NPC skeletons,
            // their state replicates through the scene event system instead
            Transform hitRoot = hit.transform.root;
            if (hitRoot && hitRoot.GetComponentInChildren<StressLevelZero.AI.AIBrain>(true))
                yield break;

            GetPooleeData(hit.transform, out Rigidbody[] rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime);

            if (rigidbodies == null)
                yield break;

            for (int i = 0; i < rigidbodies.Length; i++) {
                Rigidbody rb = rigidbodies[i];
                if (!rb || rb.isKinematic)
                    continue;

                if (TransformSyncable.cache.Get(rb.gameObject))
                    continue;

                SyncUtilities.UpdateBodyAttached(rb, overrideRootName, spawnIndex, spawnTime);
                SyncUtilities.UpdateBodyDetached(rb);
            }
        }

        public static void OnGripDetached(Hand __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            GameObject currentObject = __instance.m_CurrentAttachedObject;
            if (!currentObject)
                return;

            if (currentObject.IsBlacklisted()) return;

            Rigidbody[] rigidbodies = currentObject.transform.GetJointedBodies();

            // Two hand check
            Rigidbody otherRb = __instance.otherHand.GetHeldObject();
            if (otherRb && rigidbodies.Has(otherRb))
                return;

            for (int i = 0; i < rigidbodies.Length; i++)
                SyncUtilities.UpdateBodyDetached(rigidbodies[i]);
        }

        public static void OnForcePullCancelled(GameObject grip) {
            if (!SteamIntegration.hasLobby)
                return;

            if (grip.IsBlacklisted()) return;

            Rigidbody[] rigidbodies = grip.transform.GetJointedBodies();

            for (int i = 0; i < rigidbodies.Length; i++)
                SyncUtilities.UpdateBodyDetached(rigidbodies[i]);
        }
    }
}
