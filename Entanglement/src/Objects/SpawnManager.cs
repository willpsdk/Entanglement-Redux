using System.Collections.Generic;

using StressLevelZero.Pool;

using UnityEngine;
using UnhollowerRuntimeLib;

namespace Entanglement.Objects
{
    public static class SpawnManager
    {
        internal static bool SpawnOverride = false;

        private static readonly HashSet<string> BlacklistedPools = new HashSet<string>() {
            "ProjectilePool",
            "AudioPlayer",
            "Utility Gun",
            "Magazine",      // Was previously caught via HasBlacklistedComponent
        };

        public static string GetPoolTitle(Pool pool) => pool.name.Remove(0, 7);

        public static bool IsBlacklisted(this Pool pool)
        {
            string title = GetPoolTitle(pool);

            if (BlacklistedPools.Contains(title)) return true;

            // Guard: skip pools with no valid prefab or no rigidbody (not a physics object)
            if (!pool.Prefab) return true;

            return pool.Prefab.GetComponentInChildren(Il2CppType.Of<Rigidbody>()) == null;
        }
    }
}