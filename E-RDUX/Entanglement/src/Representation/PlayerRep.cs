using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnhollowerBaseLib;

using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.Compat.Playermodels;
using Entanglement.Managers;
using Entanglement.Objects;

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
    public class PlayerRepresentation {
        // The velocity calculated for the legs can be jittery at times. To solve this the value is smoothed out. The higher this value the more precision, but the more jitter.
        // A value of 10 is smooth, and with no acceleration value it looks best.
        public static float legJitter = 10f;

        public static Dictionary<long, PlayerRepresentation> representations = new Dictionary<long, PlayerRepresentation>();

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

        Renderer[] cachedRenderers;
        public bool IsEliminated { get; private set; }

        // Hides/shows this rep for everyone else - used by gamemode elimination, doesn't touch
        // colliders or transform sync, only what's rendered
        public void SetEliminated(bool eliminated) {
            if (IsEliminated == eliminated) return;
            IsEliminated = eliminated;

            if (!repRoot) return;

            if (cachedRenderers == null)
                cachedRenderers = repRoot.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in cachedRenderers) {
                if (renderer) renderer.enabled = !eliminated;
            }

            if (repCanvas) repCanvas.SetActive(!eliminated);
        }

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
        public long playerId;
        public bool isGrounded;

        // Buffered network targets, applied with smoothing every tick instead of snapping on packet arrival
        public bool hasNetTarget = false;
        public Vector3 netRootPosition;
        public Vector3 netRootVelocity;
        public float netReceiveTime;
        public Vector3[] netPositions = new Vector3[3];
        public Quaternion[] netRotations = new Quaternion[3];
        public Vector3[] netLimbVelocities = new Vector3[3];

        public const float repFollowSharpness = 35f;   // Exponential smoothing rate for the root
        public const float repLimbSharpness = 60f;      // Head/hands track much harder, they are what pushes other players
        public const float repExtrapolationLimit = 0.2f; // Never predict further than this past the last packet
        public const float repSnapDistance = 2f;        // Teleport instead of chasing when further than this
        public const float repMaxPredictedSpeed = 25f;  // Caps dead reckoning speed so a teleport can't fling the rep


#if DEBUG
        public static PlayerRepresentation debugRepresentation;

        // Feeds a mesh-only copy of the held item through the receiver-side sync path at a
        // throttled rate, a solo stand-in for how a remote client sees your carried items
        public static float debugLoopbackHz = 18f;
        static readonly long debugLoopbackFakeOwner = 1L;

        static TransformSyncable debugLoopSyncable;
        static GameObject debugLoopProxy;
        static GameObject debugLoopSource;
        static float debugLoopTimer;

        static void UpdateDebugHeldLoopback() {
            GameObject held = null;

            if (debugRepresentation != null) {
                if (PlayerScripts.playerRightHand && PlayerScripts.playerRightHand.m_CurrentAttachedObject)
                    held = PlayerScripts.playerRightHand.m_CurrentAttachedObject.transform.GetJointedRoot().gameObject;
                else if (PlayerScripts.playerLeftHand && PlayerScripts.playerLeftHand.m_CurrentAttachedObject)
                    held = PlayerScripts.playerLeftHand.m_CurrentAttachedObject.transform.GetJointedRoot().gameObject;
            }

            if (held != debugLoopSource || (held != null && debugLoopSyncable == null)) {
                DestroyDebugLoopback();
                debugLoopSource = held;
                if (held) CreateDebugLoopback(held);
            }

            if (!held || debugLoopSyncable == null)
                return;

            float interval = debugLoopbackHz > 0f ? 1f / debugLoopbackHz : 0f;
            debugLoopTimer += Time.deltaTime;
            if (debugLoopTimer < interval)
                return;
            debugLoopTimer = 0f;

            Rigidbody heldRb = held.GetComponent<Rigidbody>();
            if (!heldRb) heldRb = held.GetComponentInChildren<Rigidbody>();

            Vector3 velocity = heldRb ? heldRb.velocity : Vector3.zero;
            Vector3 angularVelocity = heldRb ? heldRb.angularVelocity : Vector3.zero;

            // Same forward offset the debug rep's hands use, so the proxy sits beside the dummy
            SimplifiedTransform state = new SimplifiedTransform(held.transform.position + Vector3.forward, held.transform.rotation);

            try { debugLoopSyncable.ApplyTransform(state, velocity, angularVelocity); }
            catch { DestroyDebugLoopback(); }
        }

        static void CreateDebugLoopback(GameObject held) {
            try {
                debugLoopProxy = new GameObject($"DebugHeldLoopback {held.name}");
                debugLoopProxy.transform.position = held.transform.position;
                debugLoopProxy.transform.rotation = held.transform.rotation;

                // Copy the item's meshes only - no game scripts, colliders or joints of its own
                foreach (MeshFilter filter in held.GetComponentsInChildren<MeshFilter>(false)) {
                    MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || sourceRenderer == null)
                        continue;

                    GameObject part = new GameObject("mesh");
                    part.transform.SetParent(debugLoopProxy.transform, false);
                    part.transform.position = filter.transform.position;
                    part.transform.rotation = filter.transform.rotation;
                    part.transform.localScale = filter.transform.lossyScale;

                    part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    part.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
                }

                debugLoopProxy.transform.position += Vector3.forward;

                Rigidbody rb = debugLoopProxy.AddComponent<Rigidbody>();
                rb.useGravity = false;

                // Register as a fake remote object: not owned locally (so it takes the receiver
                // path) and isValid set directly, which skips id/dictionary registration entirely
                debugLoopSyncable = TransformSyncable.CreateSync(debugLoopbackFakeOwner, rb) as TransformSyncable;
                if (debugLoopSyncable == null) {
                    DestroyDebugLoopback();
                    return;
                }

                debugLoopSyncable.isValid = true;
                debugLoopSyncable.EnqueueOwner(debugLoopbackFakeOwner); // ownerQueue.Count > 0 => held-item branch
            }
            catch {
                DestroyDebugLoopback();
            }
        }

        static void DestroyDebugLoopback() {
            if (debugLoopSyncable != null) {
                try {
                    if (debugLoopProxy) TransformSyncable.cache.Remove(debugLoopProxy);
                    debugLoopSyncable.Cleanup();
                }
                catch { }
                debugLoopSyncable = null;
            }

            if (debugLoopProxy) {
                GameObject.Destroy(debugLoopProxy);
                debugLoopProxy = null;
            }

            debugLoopSource = null;
            debugLoopTimer = 0f;
        }
#endif

        public static AssetBundle playerRepBundle;

        public static void LoadBundle() {
            playerRepBundle = EmebeddedAssetBundle.LoadFromAssembly(EntanglementMod.entanglementAssembly, "Entanglement.resources.playerrep.eres");

            if (playerRepBundle == null)
                throw new NullReferenceException("playerRepBundle is null! Did you forget to compile the player bundle into the dll?");
        }

        public PlayerRepresentation(string playerName, long playerId) {
            this.playerName = playerName;
            this.playerId = playerId;
            RecreateRepresentations();
        }

        public void DeleteRepresentations() {
            GameObject.Destroy(repFord);
            GameObject.Destroy(repCanvas);
            if (currentSkinObject) GameObject.Destroy(currentSkinObject);
        }

        public void RecreateRepresentations() {
            // Catch errors with creating so it doesnt prevent others from being created
            try {
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
                repFord.name = $"PlayerRep.{playerId}"; // Store the id in the root for combat purposes (GetComponent is ugly)

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
            catch {
                EntangleLogger.Error($"Error caught creating rep from user {playerId}");
            }
        }

        // Create a ragdoll of this PlayerRep when the player dies
        public void CreateRagdoll() {
            if (!activeAnimator)
                return;

            // Deterministic name: a timestamped one differs per machine, which broke the
            // object path resolution whenever someone grabbed or dragged a corpse
            GameObject ragdollRoot = new GameObject($"Ragdoll {playerId}");

            GameObject newRagdoll = GameObject.Instantiate(ragdollBody.gameObject);
            newRagdoll.transform.parent = ragdollRoot.transform;

            // Ignore colliders under the body
            Collider[] ragdollCols = newRagdoll.GetComponentsInChildren<Collider>(true);
            foreach (Collider col1 in ragdollCols)
            {
                foreach (Collider col2 in ragdollCols)
                {
                    if (col1 == col2)
                        continue;

                    Physics.IgnoreCollision(col1, col2, true);
                }
            }

            newRagdoll.gameObject.SetActive(true);

            // Now send velocity
            foreach (Rigidbody rb in newRagdoll.GetComponentsInChildren<Rigidbody>(true)) {
                rb.velocity = repSavedVel;
                rb.angularVelocity = Vector3.zero;
            }

            // Copy positions
            CopyBone(repBody.transform, newRagdoll.transform);
            CopyBones(repBody.references, newRagdoll.GetComponent<SLZ_Body>().references);

            // Add ragdoll script
            newRagdoll.gameObject.AddComponent<RagdollBehaviour>();

            if (Node.isServer && SteamIntegration.hasLobby)
                MelonCoroutines.Start(SyncRagdollBones(newRagdoll));
        }

        // The host owns corpse physics from the start, so every machine watches the same fall
        // instead of simulating its own. Registration waits half a second so slower clients
        // have spawned their copy before the creates arrive.
        static IEnumerator SyncRagdollBones(GameObject ragdoll) {
            yield return new WaitForSeconds(0.5f);

            if (!ragdoll || !SteamIntegration.hasLobby || !Node.isServer)
                yield break;

            foreach (Rigidbody rb in ragdoll.GetComponentsInChildren<Rigidbody>(true)) {
                if (!rb || rb.isKinematic)
                    continue;

                if (TransformSyncable.cache.Get(rb.gameObject))
                    continue;

                SyncUtilities.UpdateBodyAttached(rb, null, -1, -1f);
                SyncUtilities.UpdateBodyDetached(rb);
            }
        }

        public void CopyBones(SLZ_Body.References from, SLZ_Body.References to) {
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

        public void CopyBone(Transform from, Transform to) {
            to.position = from.position;
            to.rotation = from.rotation;
        }

        // Feeds the rep new network data, velocities are estimated from consecutive packets for dead reckoning
        public void SetNetTargets(Vector3 rootPosition, Vector3[] positions, Quaternion[] rotations) {
            float now = Time.time;

            if (hasNetTarget) {
                float packetDelta = Mathf.Clamp(now - netReceiveTime, 0.008f, 0.5f);
                netRootVelocity = Vector3.ClampMagnitude((rootPosition - netRootPosition) / packetDelta, repMaxPredictedSpeed);

                for (int r = 0; r < netPositions.Length; r++)
                    netLimbVelocities[r] = Vector3.ClampMagnitude((positions[r] - netPositions[r]) / packetDelta, repMaxPredictedSpeed);
            }
            else {
                netRootVelocity = Vector3.zero;
                for (int r = 0; r < netLimbVelocities.Length; r++)
                    netLimbVelocities[r] = Vector3.zero;
            }

            netRootPosition = rootPosition;
            netReceiveTime = now;

            for (int r = 0; r < netPositions.Length; r++) {
                netPositions[r] = positions[r];
                netRotations[r] = rotations[r];
            }

            hasNetTarget = true;
        }

        // Glides the rep toward the latest network state, run once per tick
        public void ApplyNetSmoothing(float dt) {
            if (!hasNetTarget || !repRoot) return;

            float age = Mathf.Min(Time.time - netReceiveTime, repExtrapolationLimit);
            Vector3 predictedRoot = netRootPosition + netRootVelocity * age;

            float t = 1f - Mathf.Exp(-repFollowSharpness * dt);
            float limbT = 1f - Mathf.Exp(-repLimbSharpness * dt);
            if ((repRoot.position - predictedRoot).sqrMagnitude > repSnapDistance * repSnapDistance)
                t = limbT = 1f;

            repRoot.position = Vector3.Lerp(repRoot.position, predictedRoot, t);

            // Limbs dead reckon with their own velocity, root drift alone left fast hand
            // motion a packet behind and made player-on-player pushes feel delayed
            for (int r = 0; r < repTransforms.Length; r++) {
                if (!repTransforms[r]) continue;

                repTransforms[r].position = Vector3.Lerp(repTransforms[r].position, netPositions[r] + netLimbVelocities[r] * age, limbT);
                repTransforms[r].rotation = Quaternion.Slerp(repTransforms[r].rotation, netRotations[r], limbT);
            }

            if (repCanvasTransform && repTransforms[0]) {
                repCanvasTransform.position = repTransforms[0].position + Vector3.up * 0.4f;

                if (Camera.current)
                    repCanvasTransform.rotation = Quaternion.LookRotation(Vector3.Normalize(repCanvasTransform.position - Camera.current.transform.position), Vector3.up);
            }

            UpdateTalkingIndicator();
        }

        bool wasTalking;
        Color baseNameColor = Color.white;

        // Lets a gamemode tint this player's nametag (team color, etc). Applies right away
        // unless they're currently talking, in which case it takes over once they stop.
        public void SetNameColor(Color color) {
            baseNameColor = color;
            if (!wasTalking && repNameText)
                repNameText.color = baseNameColor;
        }

        // Tints the nametag green and prefixes a speaker dot while this player is talking
        void UpdateTalkingIndicator() {
            if (!repNameText)
                return;

            bool talking = Entanglement.Voice.VoiceManager.IsSpeaking(playerId);
            if (talking == wasTalking)
                return;

            wasTalking = talking;
            repNameText.text = talking ? $"● {playerName}" : playerName;
            repNameText.color = talking ? new Color(0.4f, 1f, 0.5f) : baseNameColor;
        }

        // This calculates the velocity on the client side for leg prediction
        public void SaveVelocity() {
            //Get Velocities
            Vector3 currentPosition = repRoot.position;
            //Ground Check
            float dt = Time.fixedDeltaTime;
            repSavedVel = Vector3.Slerp(repInputVel, PhysicsData.GetVelocity(currentPosition, prevRepRootPos, dt), dt * legJitter);

            if (isGrounded) {
                repInputVel = repSavedVel;
            }
            else
                repInputVel = Vector3.zero;
            //Cache
            prevRepRootPos = currentPosition;
        }

        public void UpdateIK()
        {
            // Catch errors so other players arent broken
            try {
                //Re-Apply playermodel if unloaded
                if ((!currentSkinBundle || !currentSkinObject) && isCustomSkinned)
                    PlayerSkinLoader.ApplyPlayermodel(this, currentSkinPath);
                // Prevent exceptions if we are in the middle of deleting a playermodel
                if (!activeAnimator)
                    return;

                activeAnimator.Update(Time.fixedDeltaTime);
                repAnimationManager.OnLateUpdate();
                SaveVelocity();
                repBody.FullBodyUpdate(repInputVel, Vector3.zero);
                repBody.ArtToBlender.UpdateBlender();
            }
            catch { }
        }

        public void UpdatePose(Handedness hand, int index)
        {
            Il2CppStringArray handPoses = PlayerScripts.playerHandPoses;
            if (handPoses.Count < index + 1)
                return;
            UpdatePose(hand, handPoses[index]);
        }

        public void UpdatePose(Handedness hand, string pose) => repAnimationManager?.SetHandPose(hand, pose);

        public void UpdatePoseRadius(Handedness hand, float radius) => repAnimationManager?.SetCylinderRadius(hand, radius);

        public void UpdateFingers(Handedness hand, float indexCurl = 1f, float middleCurl = 1f, float ringCurl = 1f, float pinkyCurl = 1f, float thumbCurl = 1f) {
            repAnimationManager.ApplyFingerCurl(hand, 1f - thumbCurl, 1f - indexCurl, 1f - middleCurl, 1f - ringCurl, 1f - pinkyCurl);
        }

        public void UpdateFingers(Handedness hand, SimplifiedHand handData) => UpdateFingers(hand, handData.indexCurl, handData.middleCurl, handData.ringCurl, handData.pinkyCurl, handData.thumbCurl);

        public void IgnoreCollision(Rigidbody otherBody, bool ignore) {
            Collider[] otherColliders = otherBody.GetComponentsInChildren<Collider>();
            foreach (Collider col1 in colliders)
                foreach (Collider col2 in otherColliders) Physics.IgnoreCollision(col1, col2, ignore);
        }

        public static void GetPlayerTransforms() {
            GameObject skeletonRig = GameObject.Find("[RigManager (Default Brett)]/[SkeletonRig (GameWorld Brett)]");
            
            if (skeletonRig) {
                syncedRoot = skeletonRig.transform;

                syncedPoints[0] = syncedRoot.Find("Head");
                syncedPoints[1] = syncedRoot.Find("Hand (left)");
                syncedPoints[2] = syncedRoot.Find("Hand (right)");
            }
        }

        public static PlayerRepSyncData GetPlayerSyncData() {
            foreach (var syncPoint in syncedPoints)
                if (syncPoint == null)
                    return null;

            PlayerRepSyncData data = new PlayerRepSyncData();

            data.userId = SteamIntegration.currentUserId;

            for (int r = 0; r < data.simplifiedTransforms.Length; r++) {
                data.simplifiedTransforms[r].position = syncedPoints[r].position;
                data.simplifiedTransforms[r].rotation = SimplifiedQuaternion.SimplifyQuat(syncedPoints[r].rotation);
            }

            data.rootPosition = syncedRoot.position;

            data.isGrounded = PlayerScripts.playerGrounder.isGrounded;

            data.simplifiedLeftHand = new SimplifiedHand(PlayerScripts.playerLeftHand.fingerCurl);
            data.simplifiedRightHand = new SimplifiedHand(PlayerScripts.playerRightHand.fingerCurl);

#if DEBUG
            try {
                if (debugRepresentation != null) {
                    for (int l = 0; l < data.simplifiedTransforms.Length; l++) {
                        data.simplifiedTransforms[l].Apply(debugRepresentation.repTransforms[l]);
                        debugRepresentation.repTransforms[l].position += Vector3.forward;
                    }

                    // Root gets the same offset as the head/hand targets, otherwise the body IK
                    // is fed contradictory goals (feet at the player, head a meter away)
                    debugRepresentation.repRoot.position = syncedRoot.position + Vector3.forward;

                    debugRepresentation.isGrounded = data.isGrounded;

                    debugRepresentation.UpdateFingers(Handedness.LEFT, data.simplifiedLeftHand);
                    debugRepresentation.UpdateFingers(Handedness.RIGHT, data.simplifiedRightHand);
                }

                UpdateDebugHeldLoopback();
            } catch { }
#endif

            return data;
        }

        public static void SyncPlayerReps() {
            if (SteamIntegration.hasLobby) {
                var syncData = GetPlayerSyncData();

                if (syncData != null) {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerRepSync, syncData);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
                }
                else
                    GetPlayerTransforms();
            }
#if DEBUG
            // The debug dummy is usually tested solo - keep feeding it without a lobby.
            // Also runs while a loopback proxy lingers so removing the dummy tears it down.
            else if (debugRepresentation != null || debugLoopProxy != null) {
                if (GetPlayerSyncData() == null)
                    GetPlayerTransforms();
            }
#endif
        }

        public static void UpdatePlayerReps() {
            foreach (PlayerRepresentation rep in representations.Values) {
                rep.ApplyNetSmoothing(Time.fixedDeltaTime);

                float dist = (syncedRoot.position - rep.repRoot.position).sqrMagnitude;

                // Since the distance is squared its 1000 * 1000. Just some optimization, you won't be seeing the player move that far away.
                if (dist < 1000000f) {
                    rep.UpdateIK();
                    rep.repCanvasTransform?.gameObject?.SetActive(Client.nameTagsVisible && !rep.IsEliminated);
                }
            }

#if DEBUG
            try {
                if (debugRepresentation != null)
                    debugRepresentation.UpdateIK();
            } catch { }
#endif
        }
    }
}
