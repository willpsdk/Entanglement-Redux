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

            // PlayerRep stays out of TransformSync/ObjectSync (owned by PlayerRepresentation + PD body).
            // Local grab/shove still work via grips + the force-driven hit body.
            Transform root = obj.transform.root;
            if (root && root.name.StartsWith("PlayerRep."))
                return true;

            if (IsAmmoPickup(obj))
                return true;

            for (int i = 0; i < blacklistedObjects.Length; i++)
                if (obj.transform.InHierarchyOf(blacklistedObjects[i])) return true;
            return false;
        }

        /// <summary>True when this object is another player's representation (grab/shove path).</summary>
        public static bool IsPlayerRep(this GameObject obj) {
            if (!obj)
                return false;
            Transform root = obj.transform.root;
            return root && root.name.StartsWith("PlayerRep.");
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
