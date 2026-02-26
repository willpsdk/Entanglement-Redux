using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnhollowerBaseLib;
using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.Compat.Playermodels;
using Entanglement.Managers;
using UnityEngine;
using MelonLoader;
using TMPro;
using Entanglement.Data;
using StressLevelZero;
using StressLevelZero.VRMK;
using StressLevelZero.Player;
using StressLevelZero.SFX;
using StressLevelZero.Combat;
using ModThatIsNotMod;

namespace Entanglement.Representation
{
    public class PlayerRepresentation
    {
        public static float legJitter = 10f;
        public static Dictionary<ulong, PlayerRepresentation> representations = new Dictionary<ulong, PlayerRepresentation>();

        public static Transform[] syncedPoints = new Transform[3];
        public static Transform syncedRoot;

        public Transform[] repTransforms = new Transform[3];
        public Transform repRoot;

        public GameObject repFord;
        public Material repHologram;
        public GameObject repCanvas;
        public Canvas repCanvasComponent;
        public Transform repCanvasTransform;
        public TextMeshProUGUI repNameText;

        public Transform repGeo;
        public Transform repSHJnt;

        public Collider[] colliders = new Collider[0];

        public SLZ_Body repBody;
        public SLZ_Body ragdollBody;
        public CharacterAnimationManager repAnimationManager;

        public GunSFX repGunSFX;
        public GunSFX repBalloonSFX;
        public GunSFX repStabSFX;
        public GravGunSFX repPowerPunchSFX;

        public Animator repAnimator;
        public Animator skinAnimator;
        public Animator activeAnimator;

        public GameObject currentSkinObject;
        public AssetBundle currentSkinBundle;
        public string currentSkinPath;

        public bool isCustomSkinned;

        public Vector3 repInputVel = Vector3.zero;
        public Vector3 repSavedVel = Vector3.zero;
        public Vector3 prevRepRootPos = Vector3.zero;

        public string playerName;
        public ulong playerId;
        public bool isGrounded;

#if DEBUG
        public static PlayerRepresentation debugRepresentation;
#endif

        public static AssetBundle playerRepBundle;

        public static void LoadBundle()
        {
            playerRepBundle = EmebeddedAssetBundle.LoadFromAssembly(EntanglementMod.entanglementAssembly, "Entanglement.resources.playerrep.eres");

            if (playerRepBundle == null)
                throw new NullReferenceException("playerRepBundle is null! Did you forget to compile the player bundle into the dll?");
        }

        public PlayerRepresentation(string playerName, ulong playerId)
        {
            this.playerName = playerName;
            this.playerId = playerId;
            RecreateRepresentations();
        }

        public void DeleteRepresentations()
        {
            if (repFord) GameObject.Destroy(repFord);
            if (repCanvas) GameObject.Destroy(repCanvas);
            if (currentSkinObject) GameObject.Destroy(currentSkinObject);
        }

        public void RecreateRepresentations()
        {
            try
            {
                repCanvas = new GameObject("RepCanvas");
                repCanvasComponent = repCanvas.AddComponent<Canvas>();
                repCanvasComponent.renderMode = RenderMode.WorldSpace;
                repCanvasTransform = repCanvas.transform;
                repCanvasTransform.localScale = Vector3.one / 200.0f;

                repNameText = repCanvas.AddComponent<TextMeshProUGUI>();
                repNameText.alignment = TextAlignmentOptions.Midline;
                repNameText.enableAutoSizing = true;
                repNameText.text = playerName;

                repHologram = Material.Instantiate(playerRepBundle.LoadAsset<Material>("PlayerHolographic"));

                repFord = GameObject.Instantiate(playerRepBundle.LoadAsset<GameObject>("PlayerRep"));
                repFord.name = $"PlayerRep.{playerId}";

                repRoot = repFord.transform;

                repGunSFX = repRoot.Find("GunSFX").GetComponent<GunSFX>();
                repBalloonSFX = repRoot.Find("BalloonSFX").GetComponent<GunSFX>();
                repStabSFX = repRoot.Find("StabSFX").GetComponent<GunSFX>();
                repPowerPunchSFX = repRoot.Find("PuncherSFX").GetComponent<GravGunSFX>();

                Transform repBodyBody = repRoot.Find("Body");
                repBody = repBodyBody.GetComponent<SLZ_Body>();
                repBody.OnStart();

                ragdollBody = repRoot.Find("Ragdoll").GetComponent<SLZ_Body>();

                Transform repAnimatorBody = repRoot.Find("Brett@neutral");
                repAnimator = repAnimatorBody.GetComponent<Animator>();
                repAnimator.runtimeAnimatorController = PlayerScripts.playerAnimatorController;
                activeAnimator = repAnimator;
                repAnimationManager = repAnimatorBody.GetComponent<CharacterAnimationManager>();
                repGeo = repAnimatorBody.Find("geoGrp");
                repSHJnt = repAnimatorBody.Find("SHJntGrp");

                repTransforms[0] = repRoot.Find("Head");
                repTransforms[1] = repRoot.Find("Hand (left)");
                repTransforms[2] = repRoot.Find("Hand (right)");

                colliders = repRoot.GetComponentsInChildren<Collider>();

                if (isCustomSkinned && currentSkinPath != null)
                    PlayerSkinLoader.ApplyPlayermodel(this, currentSkinPath);
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"Error caught creating rep from user {playerId}: {e.Message}");
            }
        }

        public void CreateRagdoll()
        {
            if (!activeAnimator || !ragdollBody)
                return;

            GameObject ragdollRoot = new GameObject($"Ragdoll {Time.realtimeSinceStartup}");

            GameObject newRagdoll = GameObject.Instantiate(ragdollBody.gameObject);
            newRagdoll.transform.parent = ragdollRoot.transform;

            Collider[] ragdollCols = newRagdoll.GetComponentsInChildren<Collider>(true);
            foreach (Collider col1 in ragdollCols)
            {
                foreach (Collider col2 in ragdollCols)
                {
                    if (col1 == col2) continue;
                    Physics.IgnoreCollision(col1, col2, true);
                }
            }

            newRagdoll.gameObject.SetActive(true);

            foreach (Rigidbody rb in newRagdoll.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.velocity = repSavedVel;
                rb.angularVelocity = Vector3.zero;
            }

            CopyBone(repBody.transform, newRagdoll.transform);
            CopyBones(repBody.references, newRagdoll.GetComponent<SLZ_Body>().references);
            newRagdoll.gameObject.AddComponent<RagdollBehaviour>();
        }

        public void CopyBones(SLZ_Body.References from, SLZ_Body.References to)
        {
            CopyBone(from.skull, to.skull);
            CopyBone(from.c4Vertebra, to.c4Vertebra);
            CopyBone(from.t1Offset, to.t1Offset);
            CopyBone(from.t7Vertebra, to.t7Vertebra);
            CopyBone(from.l1Vertebra, to.l1Vertebra);
            CopyBone(from.l3Vertebra, to.l3Vertebra);
            CopyBone(from.sacrum, to.sacrum);

            CopyBone(from.leftHip, to.leftHip);
            CopyBone(from.leftKnee, to.leftKnee);
            CopyBone(from.leftAnkle, to.leftAnkle);

            CopyBone(from.rightHip, to.rightHip);
            CopyBone(from.rightKnee, to.rightKnee);
            CopyBone(from.rightAnkle, to.rightAnkle);

            CopyBone(from.leftShoulder, to.leftShoulder);
            CopyBone(from.leftElbow, to.leftElbow);
            CopyBone(from.leftWrist, to.leftWrist);

            CopyBone(from.rightShoulder, to.rightShoulder);
            CopyBone(from.rightElbow, to.rightElbow);
            CopyBone(from.rightWrist, to.rightWrist);
        }

        public void CopyBone(Transform from, Transform to)
        {
            to.position = from.position;
            to.rotation = from.rotation;
        }

        public void SaveVelocity()
        {
            Vector3 currentPosition = repRoot.position;
            float dt = Time.fixedDeltaTime;
            Vector3 targetVel = PhysicsData.GetVelocity(currentPosition, prevRepRootPos, dt);

            if (targetVel.sqrMagnitude < 0.01f) targetVel = Vector3.zero;
            repSavedVel = Vector3.Slerp(repInputVel, targetVel, dt * legJitter);

            if (isGrounded) repInputVel = repSavedVel;
            else repInputVel = Vector3.zero;

            prevRepRootPos = currentPosition;
        }

        public void UpdateIK()
        {
            try
            {
                if ((!currentSkinBundle || !currentSkinObject) && isCustomSkinned)
                    PlayerSkinLoader.ApplyPlayermodel(this, currentSkinPath);

                if (!activeAnimator || !repAnimationManager || !repBody)
                    return;

                activeAnimator.Update(Time.fixedDeltaTime);
                repAnimationManager.OnLateUpdate();
                SaveVelocity();
                repBody.FullBodyUpdate(repInputVel, Vector3.zero);
                if (repBody.ArtToBlender != null) repBody.ArtToBlender.UpdateBlender();
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"UpdateIK Error for {playerId}: {e.Message}");
            }
        }

        public void UpdatePose(Handedness hand, int index)
        {
            Il2CppStringArray handPoses = PlayerScripts.playerHandPoses;
            if (handPoses.Count < index + 1) return;
            UpdatePose(hand, handPoses[index]);
        }

        public void UpdatePose(Handedness hand, string pose) => repAnimationManager?.SetHandPose(hand, pose);
        public void UpdatePoseRadius(Handedness hand, float radius) => repAnimationManager?.SetCylinderRadius(hand, radius);

        public void UpdateFingers(Handedness hand, float indexCurl = 1f, float middleCurl = 1f, float ringCurl = 1f, float pinkyCurl = 1f, float thumbCurl = 1f)
        {
            repAnimationManager?.ApplyFingerCurl(hand, 1f - thumbCurl, 1f - indexCurl, 1f - middleCurl, 1f - ringCurl, 1f - pinkyCurl);
        }

        public void UpdateFingers(Handedness hand, SimplifiedHand handData) => UpdateFingers(hand, handData.indexCurl, handData.middleCurl, handData.ringCurl, handData.pinkyCurl, handData.thumbCurl);

        public void IgnoreCollision(Rigidbody otherBody, bool ignore)
        {
            if (!otherBody) return;
            Collider[] otherColliders = otherBody.GetComponentsInChildren<Collider>();
            foreach (Collider col1 in colliders)
                foreach (Collider col2 in otherColliders) Physics.IgnoreCollision(col1, col2, ignore);
        }

        public static void GetPlayerTransforms()
        {
            if (syncedRoot == null)
            {
                var rigManager = GameObject.FindObjectOfType<StressLevelZero.Rig.RigManager>();
                if (rigManager != null)
                {
                    Transform t = rigManager.transform.Find("[SkeletonRig (GameWorld Brett)]");
                    syncedRoot = t != null ? t : rigManager.transform;
                }
                else
                {
                    GameObject fallbackRig = GameObject.Find("[RigManager (Default Brett)]/[SkeletonRig (GameWorld Brett)]");
                    if (fallbackRig != null) syncedRoot = fallbackRig.transform;
                }
            }

            if (syncedRoot != null)
            {
                if (syncedPoints[0] == null) syncedPoints[0] = GetTransformDeep(syncedRoot, "Head");
                if (syncedPoints[1] == null) syncedPoints[1] = GetTransformDeep(syncedRoot, "Hand (left)");
                if (syncedPoints[2] == null) syncedPoints[2] = GetTransformDeep(syncedRoot, "Hand (right)");
            }
        }

        private static Transform GetTransformDeep(Transform parent, string name)
        {
            Transform direct = parent.Find(name);
            if (direct != null) return direct;

            Transform[] children = parent.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == name) return child;
            }
            return null;
        }

        public static PlayerRepSyncData GetPlayerSyncData()
        {
            foreach (var syncPoint in syncedPoints)
                if (syncPoint == null) return null;

            PlayerRepSyncData data = new PlayerRepSyncData();
            data.userId = (ulong)SteamIntegration.currentUser.m_SteamID;

            for (int r = 0; r < data.simplifiedTransforms.Length; r++)
            {
                data.simplifiedTransforms[r].position = syncedPoints[r].position;
                data.simplifiedTransforms[r].rotation = SimplifiedQuaternion.SimplifyQuat(syncedPoints[r].rotation);
            }

            data.rootPosition = syncedRoot.position;
            data.isGrounded = PlayerScripts.playerGrounder.isGrounded;

            data.simplifiedLeftHand = new SimplifiedHand(PlayerScripts.playerLeftHand.fingerCurl);
            data.simplifiedRightHand = new SimplifiedHand(PlayerScripts.playerRightHand.fingerCurl);

            return data;
        }

        public static void SyncPlayerReps()
        {
            if (SteamIntegration.hasLobby)
            {
                var syncData = GetPlayerSyncData();

                if (syncData != null)
                {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerRepSync, syncData);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
                }
                else
                    GetPlayerTransforms();
            }
        }

        public static void UpdatePlayerReps()
        {
            Vector3 centerPos = Vector3.zero;
            if (syncedRoot != null) centerPos = syncedRoot.position;
            else if (Camera.main != null) centerPos = Camera.main.transform.position;

            foreach (PlayerRepresentation rep in representations.Values)
            {
                if (rep == null || rep.repRoot == null) continue;

                float dist = (centerPos - rep.repRoot.position).sqrMagnitude;

                // Continue executing IK even if centerPos is Vector3.zero to prevent freezing
                if (dist < 1000000f || centerPos == Vector3.zero)
                {
                    rep.UpdateIK();
                    if (rep.repCanvasTransform != null && rep.repCanvasTransform.gameObject != null)
                        rep.repCanvasTransform.gameObject.SetActive(Client.nameTagsVisible);
                }
            }
        }
    }
}