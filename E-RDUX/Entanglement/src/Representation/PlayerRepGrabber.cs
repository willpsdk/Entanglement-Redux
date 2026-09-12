using System;

using StressLevelZero;
using StressLevelZero.Interaction;

using UnityEngine;

using Entanglement.Objects;
using Entanglement.Network;

using MelonLoader;

namespace Entanglement.Representation
{
    /// <summary>
    /// Fusion-style remote grip host for a PlayerRep.
    /// PlayerReps are IK puppets with no physics RigManager — we stub kinematic Hands on the
    /// hand IK targets and call Hand.AttachObject / DetachObject so grips joint to the visible
    /// remote hands the same way Fusion's RigGrabber does on a full networked rig.
    /// Soft hand-relative TransformSyncable drive remains the fallback if AttachObject fails.
    /// </summary>
    public class PlayerRepGrabber
    {
        public PlayerRepresentation rep;
        public Hand leftHand;
        public Hand rightHand;

        ushort leftObjectId;
        ushort rightObjectId;
        byte leftGripIndex;
        byte rightGripIndex;
        bool leftAttached;
        bool rightAttached;

        public static bool IsRemoteRepHand(Hand hand) {
            if (!hand)
                return false;
            Transform root = hand.transform.root;
            return root && root.name.StartsWith("PlayerRep.");
        }

        public static PlayerRepGrabber Create(PlayerRepresentation rep) {
            PlayerRepGrabber grabber = new PlayerRepGrabber();
            grabber.rep = rep;
            grabber.leftHand = CreateStubHand(rep.repTransforms[1], Handedness.LEFT);
            grabber.rightHand = CreateStubHand(rep.repTransforms[2], Handedness.RIGHT);

            // Cross-link like a real physics pair (some Grip paths read otherHand)
            if (grabber.leftHand && grabber.rightHand) {
                try {
                    grabber.leftHand.otherHand = grabber.rightHand;
                    grabber.rightHand.otherHand = grabber.leftHand;
                }
                catch { }
            }

            return grabber;
        }

        static Hand CreateStubHand(Transform handTarget, Handedness handedness) {
            if (!handTarget)
                return null;

            GameObject go = handTarget.gameObject;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (!rb)
                rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = false;

            Hand hand = go.GetComponent<Hand>();
            if (!hand)
                hand = go.AddComponent<Hand>();

            try {
                hand.handedness = handedness;
            }
            catch { }

            // Tag for ownership-patch filtering
            if (go.GetComponent<PlayerRepHandMarker>() == null)
                go.AddComponent<PlayerRepHandMarker>();

            return hand;
        }

        public Hand GetHand(byte handedness) {
            if (handedness == 1)
                return leftHand;
            if (handedness == 2)
                return rightHand;
            return null;
        }

        public Hand GetHand(Handedness handedness) {
            if (handedness == Handedness.LEFT)
                return leftHand;
            if (handedness == Handedness.RIGHT)
                return rightHand;
            return null;
        }

        public void Attach(byte handedness, ushort objectId, byte gripIndex) {
            if (!ObjectSync.TryGetSyncable(objectId, out Syncable syncable))
                return;

            TransformSyncable sync = syncable.TryCast<TransformSyncable>();
            if (!sync)
                return;

            Grip grip = ResolveGrip(sync, gripIndex);
            if (!grip)
                return;

            Hand hand = GetHand(handedness);
            if (!hand)
                return;

            // Remember for re-attach after cull / mag eject
            if (handedness == 1) {
                leftObjectId = objectId;
                leftGripIndex = gripIndex;
            }
            else if (handedness == 2) {
                rightObjectId = objectId;
                rightGripIndex = gripIndex;
            }

            try {
                // Drop whatever this stub hand was holding
                if (hand.m_CurrentAttachedObject)
                    hand.DetachObject(hand.m_CurrentAttachedObject);

                hand.AttachObject(grip.gameObject);

                bool attached = hand.m_CurrentAttachedObject;
                if (handedness == 1)
                    leftAttached = attached;
                else if (handedness == 2)
                    rightAttached = attached;

                sync.SetRemoteGripAttached(attached);
            }
            catch (Exception e) {
#if DEBUG
                EntangleLogger.Warn($"PlayerRep grab attach failed: {e.Message}");
#endif
                // Leave hard-track fallback enabled
                sync.SetRemoteGripAttached(false);
            }
        }

        public void Detach(byte handedness) {
            Hand hand = GetHand(handedness);
            if (!hand)
                return;

            ushort objectId = handedness == 1 ? leftObjectId : rightObjectId;

            try {
                if (hand.m_CurrentAttachedObject)
                    hand.DetachObject(hand.m_CurrentAttachedObject);
            }
            catch { }

            if (handedness == 1) {
                leftAttached = false;
                leftObjectId = 0;
            }
            else if (handedness == 2) {
                rightAttached = false;
                rightObjectId = 0;
            }

            if (objectId != 0 && ObjectSync.TryGetSyncable(objectId, out Syncable syncable)) {
                TransformSyncable sync = syncable.TryCast<TransformSyncable>();
                if (sync)
                    sync.SetRemoteGripAttached(false);
            }
        }

        public void DetachAll() {
            Detach(1);
            Detach(2);
        }

        /// <summary>
        /// Fusion MagazineEject re-Attaches the mag grip after ForceEject — same idea here.
        /// </summary>
        public void Reattach(byte handedness) {
            ushort objectId = handedness == 1 ? leftObjectId : rightObjectId;
            byte gripIndex = handedness == 1 ? leftGripIndex : rightGripIndex;
            if (objectId == 0)
                return;
            Attach(handedness, objectId, gripIndex);
        }

        public static Grip ResolveGrip(TransformSyncable sync, byte gripIndex) {
            if (!sync)
                return null;

            Grip[] grips = sync.GetComponentsInChildren<Grip>(true);
            if (grips == null || grips.Length == 0)
                return null;

            if (gripIndex < grips.Length && grips[gripIndex])
                return grips[gripIndex];

            return grips[0];
        }

        public static byte ResolveLocalGripIndex(GameObject attachedObject, TransformSyncable sync) {
            if (!attachedObject || !sync)
                return 0;

            Grip attachedGrip = null;
            try {
                attachedGrip = Grip.Cache.Get(attachedObject);
            }
            catch { }

            Grip[] grips = sync.GetComponentsInChildren<Grip>(true);
            if (grips == null || grips.Length == 0)
                return 0;

            if (attachedGrip) {
                for (byte i = 0; i < grips.Length && i < 255; i++) {
                    if (grips[i] == attachedGrip)
                        return i;
                }
            }

            // Fall back: grip under the same root as the attached object
            Transform attachedRoot = attachedObject.transform.root;
            for (byte i = 0; i < grips.Length && i < 255; i++) {
                if (grips[i] && grips[i].transform.root == attachedRoot)
                    return i;
            }

            return 0;
        }

        public static void SendGrab(ushort objectId, byte handedness, byte gripIndex) {
            if (!SteamIntegration.hasLobby)
                return;

            PlayerRepGrabMessageData data = new PlayerRepGrabMessageData() {
                userId = SteamIntegration.currentUserId,
                objectId = objectId,
                handedness = handedness,
                gripIndex = gripIndex,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerRepGrab, data);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        public static void SendRelease(byte handedness) {
            if (!SteamIntegration.hasLobby)
                return;

            PlayerRepReleaseMessageData data = new PlayerRepReleaseMessageData() {
                userId = SteamIntegration.currentUserId,
                handedness = handedness,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerRepRelease, data);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        public static bool TryGetGrabber(long playerId, out PlayerRepGrabber grabber) {
            grabber = null;
            if (!PlayerRepresentation.representations.TryGetValue(playerId, out PlayerRepresentation rep) || rep == null)
                return false;
            grabber = rep.grabber;
            return grabber != null;
        }
    }

    // Marker component so patches can cheaply detect stub Hands without string checks every time
    [RegisterTypeInIl2Cpp]
    public class PlayerRepHandMarker : MonoBehaviour
    {
        public PlayerRepHandMarker(IntPtr ptr) : base(ptr) { }
    }
}
