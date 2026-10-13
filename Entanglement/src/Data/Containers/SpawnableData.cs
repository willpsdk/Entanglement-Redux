using System.Collections.Generic;

using StressLevelZero.Data;
using StressLevelZero.Pool;
using StressLevelZero.Props.Weapons;

using UnhollowerBaseLib;
using UnhollowerRuntimeLib;

using UnityEngine;

using UnityObject = UnityEngine.Object;

namespace Entanglement.Data
{
    public static class SpawnableData
    {
        public static readonly Dictionary<string, SpawnableObject> spawnableObjects = new Dictionary<string, SpawnableObject>();

        public static void GetData() {
            spawnableObjects.Clear();
            
            Il2CppReferenceArray<UnityObject> foundSpawnables = UnityObject.FindObjectsOfTypeIncludingAssets(Il2CppType.Of<SpawnableObject>());
            foreach (UnityObject obj in foundSpawnables) {
                SpawnableObject spawnable = obj.Cast<SpawnableObject>();
                if (!spawnableObjects.ContainsKey(spawnable.title))
                    spawnableObjects.Add(spawnable.title, spawnable);
            }

            AbsorbMasterLists();
        }

        static void AbsorbMasterLists() {
            try {
                SpawnGun[] guns = UnityObject.FindObjectsOfType<SpawnGun>();
                if (guns != null) {
                    for (int i = 0; i < guns.Length; i++) {
                        if (guns[i] && guns[i].masterList)
                            AbsorbList(guns[i].masterList);
                    }
                }
            }
            catch { }

            try {
                CheatTool[] tools = UnityObject.FindObjectsOfType<CheatTool>();
                if (tools != null) {
                    for (int i = 0; i < tools.Length; i++) {
                        if (tools[i] && tools[i].masterList)
                            AbsorbList(tools[i].masterList);
                    }
                }
            }
            catch { }
        }

        static void AbsorbList(SpawnableMasterListData list) {
            if (!list || list.objects == null)
                return;

            for (int i = 0; i < list.objects.Count; i++) {
                SpawnableObject spawnable = list.objects[i];
                if (!spawnable)
                    continue;
                if (!spawnableObjects.ContainsKey(spawnable.title))
                    spawnableObjects.Add(spawnable.title, spawnable);
            }
        }

        public static void EnsureLobbyUnlocks() {
            GetData();

            foreach (KeyValuePair<string, SpawnableObject> pair in spawnableObjects) {
                SpawnableObject spawnable = pair.Value;
                if (!spawnable)
                    continue;

                try {
                    PoolManager.RegisterPool(spawnable);
                }
                catch { }

                // Same filters as CheatTool.UnlockEverything: skip OTHER (0) and isHidden.
                if (spawnable.category == CategoryFilters.OTHER || spawnable.isHidden)
                    continue;

                try {
                    string uuid = spawnable.UUID;
                    if (!string.IsNullOrEmpty(uuid) && !ReclaimerData.HasObject(uuid))
                        ReclaimerData.AddObject(uuid);
                }
                catch { }
            }
        }

        public static void EnsureSpawnableForRemote(SpawnableObject spawnable) {
            if (!spawnable)
                return;

            try {
                PoolManager.RegisterPool(spawnable);
            }
            catch { }

            try {
                string uuid = spawnable.UUID;
                if (!string.IsNullOrEmpty(uuid) && !ReclaimerData.HasObject(uuid))
                    ReclaimerData.AddObject(uuid);
            }
            catch { }

            if (!string.IsNullOrEmpty(spawnable.title) && !spawnableObjects.ContainsKey(spawnable.title))
                spawnableObjects.Add(spawnable.title, spawnable);
        }

        public static bool TryRegister(string title, out SpawnableObject spawnable) {
            bool found = false;
            if (found = spawnableObjects.TryGetValue(title, out spawnable))
                PoolManager.RegisterPool(spawnable);
                
            return found;
        }

        // Tries to get a spawnable, if it can't it tries to register it
        public static SpawnableObject TryGetSpawnable(string title)
        {
            SpawnableObject registered = PoolManager.GetRegisteredSpawnable(title);
            
            if (!registered)
                TryRegister(title, out registered);

            if (!registered && !string.IsNullOrEmpty(title) && spawnableObjects.TryGetValue(title, out registered) && registered)
                PoolManager.RegisterPool(registered);

            if (!registered) {
                GetData();
                if (spawnableObjects.TryGetValue(title, out registered) && registered)
                    PoolManager.RegisterPool(registered);
            }
            
            return registered;
        }
        
        public static Pool GetSpawnablePool(string title) {
            if (!PoolManager.GetRegisteredSpawnable(title))
                TryRegister(title, out SpawnableObject registered);

            if (!PoolManager.DynamicPools.ContainsKey(title)) {
                SpawnableObject spawnable = TryGetSpawnable(title);
                if (spawnable)
                    PoolManager.RegisterPool(spawnable);
            }

            if (!PoolManager.DynamicPools.ContainsKey(title)) return null;

            return PoolManager.DynamicPools[title];
        }
    }
}
