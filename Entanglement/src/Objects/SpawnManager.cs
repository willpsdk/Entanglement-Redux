using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StressLevelZero.Pool;
using StressLevelZero.Props.Weapons;

using UnityEngine;

namespace Entanglement.Objects {
    public static class SpawnManager {
        internal static bool SpawnOverride = false;

        // FIX: HashSet makes string lookups instantaneous (O(1)) instead of looping through an array
        private static readonly HashSet<string> BlacklistedPools = new HashSet<string>() {
            "ProjectilePool",
            "AudioPlayer",
            "Utility Gun",
        };

        public static string GetPoolTitle(Pool pool) => pool.name.Remove(0, 7);

        public static bool IsBlacklisted(this Pool pool) {
            string title = GetPoolTitle(pool);
            
            if (BlacklistedPools.Contains(title)) return true;
            
            return !(pool.Prefab && pool.Prefab.GetComponentInChildren<Rigidbody>() && !pool.Prefab.HasBlacklistedComponent());
        }

        public static bool HasBlacklistedComponent(this GameObject prefab) {
            return prefab.GetComponentInChildren<Magazine>();
        }
    }
}