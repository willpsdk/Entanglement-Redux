using System;

using UnhollowerBaseLib;

using StressLevelZero.Rig;
using StressLevelZero.Interaction;
using StressLevelZero.VRMK;
using StressLevelZero.Player;
using StressLevelZero.AI;
using StressLevelZero.Zones;

using UnityEngine;

using System.Collections.Generic;

using Entanglement.Extensions;
using Entanglement.Network;

namespace Entanglement.Data
{
    public static class PlayerScripts {
        public static RigManager playerRig;
        public static PhysBody playerPhysBody;
        public static Player_Health playerHealth;
        public static PhysGrounder playerGrounder;
        public static Hand playerLeftHand;
        public static Hand playerRightHand;
        public static bool reloadLevelOnDeath;
        public static RuntimeAnimatorController playerAnimatorController;
        public static Il2CppStringArray playerHandPoses = null;

        public static void GetPlayerScripts() {
            GameObject rigGo = LocalPlayer.GetRigManager();
            playerRig = rigGo ? rigGo.GetComponent<RigManager>() : null;
            if (playerRig == null)
                return;
            playerHealth = playerRig.playerHealth;

            reloadLevelOnDeath = playerHealth.reloadLevelOnDeath;

            if (SteamIntegration.hasLobby)
                playerHealth.reloadLevelOnDeath = false;

            PhysicsRig physicsRig = playerRig.physicsRig;
            playerPhysBody = physicsRig.physBody;
            playerGrounder = playerPhysBody.physG;
            playerLeftHand = physicsRig.leftHand;
            playerRightHand = physicsRig.rightHand;
            playerAnimatorController = playerRig.gameWorldSkeletonRig.characterAnimationManager.animator.runtimeAnimatorController;
            GetHandPoses();

            Entanglement.Objects.LocalBodyContact.AttachTo(physicsRig.gameObject);
        }

        public static TriggerRefProxy LocalProxy() {
            try {
                GameObject playerObject = SceneZone.PlayerObject;
                if (playerObject) {
                    TriggerRefProxy proxy = TriggerRefProxy.Cache.Get(playerObject);
                    if (proxy != null)
                        return proxy;
                }
            }
            catch { }

            try { return playerRig ? playerRig.GetComponentInChildren<TriggerRefProxy>(true) : null; }
            catch { return null; }
        }

        public static void GetHandPoses() {
            // Checks if we already got the hand poses to prevent crashes
            if (playerHandPoses == null)
                CharacterAnimationManager.FetchHandPoseList(out playerHandPoses); // Lets hope this is constant!
        }

        public static Rigidbody GetHeldObject(this Hand hand) {
            if (!hand.m_CurrentAttachedObject) return null;
            return Grip.Cache.Get(hand.m_CurrentAttachedObject).host.rb;
        }

        public static bool IsHolding(this Rigidbody[] rigidbodies) {
            Rigidbody leftObject = playerLeftHand.GetHeldObject();
            Rigidbody rightObject = playerRightHand.GetHeldObject();
            if ((leftObject && rigidbodies.Has(leftObject)) || (rightObject && rigidbodies.Has(rightObject)))
                return true;
            return false;
        }
    }
}
