using UnityEngine;

namespace Entanglement.Data
{
    /// <summary>
    /// Minimal Boneworks player lookup (from ModThatIsNotMod.Player — no MTINM dependency).
    /// </summary>
    public static class LocalPlayer
    {
        const string RigManagerName = "[RigManager (Default Brett)]";
        const string PlayerHeadPath = "[PhysicsRig]/Head/PlayerTrigger";

        static GameObject rigManager;
        static GameObject playerHead;

        /// <summary>Root GameObject of the local RigManager, or null if not in a level yet.</summary>
        public static GameObject GetRigManager() {
            if (rigManager == null)
                rigManager = GameObject.Find(RigManagerName);
            return rigManager;
        }

        public static GameObject GetPlayerHead() {
            if (playerHead == null) {
                GameObject rig = GetRigManager();
                if (rig != null) {
                    Transform head = rig.transform.Find(PlayerHeadPath);
                    if (head)
                        playerHead = head.gameObject;
                }
            }
            return playerHead;
        }

        public static void ClearSceneCache() {
            rigManager = null;
            playerHead = null;
        }
    }
}
