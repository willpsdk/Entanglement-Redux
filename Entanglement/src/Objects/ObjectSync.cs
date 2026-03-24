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
    public static class ObjectSync
    {
        public static Dictionary<Pool, List<Poolee>> poolPairs = new Dictionary<Pool, List<Poolee>>(new UnityComparer());
        public static Dictionary<ushort, Syncable> syncedObjects = new Dictionary<ushort, Syncable>();
        public static List<Syncable> queuedSyncs = new List<Syncable>();
        public static ushort lastId = 0;

        // FIX: The global network lock flag. When true, all physics updates and spawns are dropped.
        public static bool isTransitioning = false;

        public static void OnCleanup()
        {
            try
            {
                RemoveObjects();
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"Error in ObjectSync.OnCleanup: {e.Message}");
            }

            try
            {
                poolPairs.Clear();
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"Error clearing poolPairs: {e.Message}");
            }

            lastId = 0;

            TransformSyncable.cache = new CustomComponentCache<TransformSyncable>();
            TransformSyncable.DestructCache = new CustomComponentCache<TransformSyncable>();

            EntangleLogger.Verbose("ObjectSync cleanup completed. All synced objects, pools, and caches cleared.");
        }

        public static void RemoveObjects()
        {
            foreach (Syncable syncable in syncedObjects.Values.ToList())
            {
                try
                {
                    if (syncable != null && syncable.gameObject != null)
                    {
                        syncable.Cleanup();
                    }
                }
                catch (Exception e)
                {
                    EntangleLogger.Verbose($"Syncable {syncable?.objectId} cleanup skipped: {e.Message}");
                }
            }
            syncedObjects.Clear();
            queuedSyncs.Clear();
        }

        public static void MoveSyncable(Syncable syncable, ushort newId)
        {
            if (isTransitioning) return; // Drop if network is locked
            
            syncedObjects.Remove(syncable.objectId);
            syncedObjects.Remove(newId);
            syncedObjects.Add(newId, syncable);
            syncable.objectId = newId;
        }

        public static void RegisterSyncable(Syncable syncable, ushort objectId)
        {
            if (isTransitioning) return; // Drop if network is locked
            
            if (syncedObjects.ContainsKey(objectId))
            {
                if (syncedObjects[objectId] != syncable) syncedObjects[objectId].Cleanup();
                syncedObjects.Remove(objectId);
            }
            syncedObjects.Add(objectId, syncable);
            lastId = objectId;
        }

        public static ushort QueueSyncable(Syncable syncable)
        {
            if (isTransitioning) return 0; // Drop if network is locked
            
            int index = queuedSyncs.IndexOf(syncable);
            if (index >= 0)
            {
                queuedSyncs.RemoveAt(index);
            }
            queuedSyncs.Add(syncable);
            return (ushort)(queuedSyncs.Count - 1);
        }

        public static bool TryGetSyncable(ushort id, out Syncable syncable)
        {
            // FIX: This ensures all incoming TransformSync packets are cleanly ignored while loading!
            if (isTransitioning) 
            {
                syncable = null;
                return false;
            }
            
            return syncedObjects.TryGetValue(id, out syncable);
        }

        public static void GetPooleeData(Transform obj, out Rigidbody[] rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime)
        {
            overrideRootName = null;
            spawnIndex = -1;
            spawnTime = -1f;
            rigidbodies = default;

            if (!obj) return;
            Transform objRoot = obj.transform.root;

            Magazine magazine = Magazine.Cache.Get(objRoot.gameObject);
            if (magazine)
            {
                SpawnableObject spawnable = magazine.magazineData.spawnableObject;
                if (!spawnable) return;

                spawnTime = 0f;
                spawnIndex = 0;
                overrideRootName = spawnable.title;
                rigidbodies = objRoot.GetChildBodies();
                return;
            }

            Poolee objPoolee = Poolee.Cache.Get(objRoot.gameObject);
            if (objPoolee)
            {
                Pool objPool = objPoolee.pool;
                if (objPool)
                {
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

        public static bool CheckForInstantiation(GameObject prefab, string poolName)
        {
            if (string.Equals(poolName, "nimbus gun", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(poolName, "utility gun", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Magazine magazineScript = prefab.GetComponent<Magazine>();
            return magazineScript != null;
        }

        public static void OnGripAttached(GameObject grip)
        {
            if (!SteamIntegration.hasLobby) return;
            MelonCoroutines.Start(OnGripValid(grip));
        }

        public static IEnumerator OnGripValid(GameObject grip)
        {
            yield return null;
            yield return null;

            if (!grip || !grip.activeInHierarchy) yield break;
            if (grip.IsBlacklisted()) yield break;

            Rigidbody[] rigidbodies = null;
            GetPooleeData(grip.transform, out rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime);

            for (int i = 0; i < rigidbodies.Length; i++) SyncUtilities.UpdateBodyAttached(rigidbodies[i], overrideRootName, spawnIndex, spawnTime);
        }

        public static void OnGripDetached(Hand __instance)
        {
            if (!SteamIntegration.hasLobby) return;

            GameObject currentObject = __instance.m_CurrentAttachedObject;
            if (!currentObject) return;
            if (currentObject.IsBlacklisted()) return;

            Rigidbody[] rigidbodies = currentObject.transform.GetJointedBodies();
            Rigidbody otherRb = __instance.otherHand.GetHeldObject();
            if (otherRb && rigidbodies.Has(otherRb)) return;

            for (int i = 0; i < rigidbodies.Length; i++)
                SyncUtilities.UpdateBodyDetached(rigidbodies[i]);
        }

        public static void OnForcePullCancelled(GameObject grip)
        {
            if (!SteamIntegration.hasLobby) return;
            if (!grip || grip.IsBlacklisted()) return;

            Rigidbody[] rigidbodies = grip.transform.GetJointedBodies();
            for (int i = 0; i < rigidbodies.Length; i++)
                SyncUtilities.UpdateBodyDetached(rigidbodies[i]);
        }
    }
}