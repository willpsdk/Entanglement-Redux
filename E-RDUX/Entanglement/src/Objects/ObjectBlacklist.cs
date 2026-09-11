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

            // Live remote players must never be treated as grab-syncable props.
            // Grabbing a PlayerRep used to enqueue TransformSyncable ownership on their bones,
            // which fought pose sync and caused trailing / launchy mutual grabs (Fusion keeps
            // player body authority locked instead).
            Transform root = obj.transform.root;
            if (root && root.name.StartsWith("PlayerRep."))
                return true;

            for (int i = 0; i < blacklistedObjects.Length; i++)
                if (obj.transform.InHierarchyOf(blacklistedObjects[i])) return true;
            return false;
        }
    }
}
