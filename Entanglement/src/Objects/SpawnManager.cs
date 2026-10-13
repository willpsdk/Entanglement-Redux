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

        private static string[] BlacklistedPools = new string[3] {
            "ProjectilePool",
            "AudioPlayer",
            "Utility Gun",
        };

        public static string GetPoolTitle(Pool pool) => pool.name.Remove(0, 7);

        public static bool IsBlacklisted(this Pool pool) {
            string title = GetPoolTitle(pool);
            for (int i = 0; i < BlacklistedPools.Length; i++) {
                if (title == BlacklistedPools[i])
                    return true;
            }
            if (IsDebris(pool, title))
                return true;
            return !(pool.Prefab && pool.Prefab.GetComponentInChildren<Rigidbody>() && !pool.Prefab.HasBlacklistedComponent());
        }

        /// <summary>A piece of a broken object: synced only when someone grabs it.</summary>
        public static bool IsDebris(Transform t) {
            // A crate's own pieces (Prop_Health bits) aren't pooled, and aren't synced at all.
            if (BreakPieces.IsPiece(t))
                return true;
            try {
                for (Transform current = t; current; current = current.parent) {
                    Poolee poolee = Poolee.Cache.Get(current.gameObject);
                    if (!poolee)
                        continue;
                    Pool pool = poolee.pool;
                    return pool && IsDebris(pool, GetPoolTitle(pool));
                }
            }
            catch { }
            return false;
        }

        static bool IsDebris(Pool pool, string title) {
            if (title.StartsWith("frag_", StringComparison.OrdinalIgnoreCase))
                return true;
            try { return pool.Prefab && pool.Prefab.name.StartsWith("frag_", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static bool HasBlacklistedComponent(this GameObject prefab) {
            return prefab.GetComponentInChildren<Magazine>()
                || prefab.GetComponentInChildren<StressLevelZero.AmmoPickup>(true)
                || prefab.GetComponentInChildren<StressLevelZero.AmmoPickupProxy>(true);
        }
    }
}
