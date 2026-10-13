using UnityEngine;

using Entanglement.Extensions;

namespace Entanglement.Objects
{
    public static class ObjectBlacklist {
        private static string[] blacklistedObjects = new string[1] {
            "[RigManager (Default Brett)]",
        };

        public static bool IsBlacklisted(this GameObject obj) {
            if (!obj) return false;

            Transform root = obj.transform.root;
            if (root && root.name.StartsWith("PlayerRep."))
                return true;

            if (IsAmmoPickup(obj))
                return true;

            for (int i = 0; i < blacklistedObjects.Length; i++)
                if (obj.transform.InHierarchyOf(blacklistedObjects[i])) return true;
            return false;
        }

        public static bool IsAmmoPickup(GameObject obj) {
            if (!obj)
                return false;
            try {
                return obj.GetComponentInParent<StressLevelZero.AmmoPickup>()
                    || obj.GetComponentInParent<StressLevelZero.AmmoPickupProxy>();
            }
            catch { return false; }
        }
    }
}
