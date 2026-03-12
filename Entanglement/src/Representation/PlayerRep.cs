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

        // FIX: Increase sync rate to 60Hz for buttery smooth movement (1/60 = 0.0167 seconds between syncs)
        private static float lastPlayerSyncTime = 0f;
        private const float PLAYER_SYNC_INTERVAL = 1f / 60f;

        // FIX: Increase animation sync to 60Hz for smooth animation matching body movement (1/60 = 0.0167 seconds between syncs)
        private static float lastAnimationSyncTime = 0f;
        private const float ANIMATION_SYNC_INTERVAL = 1f / 60f;

        public Transform[] repTransforms = new Transform[3];
        public Transform repRoot;

        public GameObject repFord;
        public Material repHologram;
        public GameObject repCanvas;
        public Canvas repCanvasComponent;
        public Transform repCanvasTransform;
        public TMP_Text repNameText;

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

        // Interpolation values for smooth movement
        public Vector3 lastSyncPosition = Vector3.zero;
        public Vector3 targetSyncPosition = Vector3.zero;
        public Vector3 prevTargetSyncPosition = Vector3.zero;
        public Quaternion lastSyncRotation = Quaternion.identity;
        public Quaternion targetSyncRotation = Quaternion.identity;
        public float interpolationAlpha = 1f;
        public float interpolationSpeed = 10f;
        public float lastSyncReceiveTime = 0f;
        public float syncPacketInterval = 1f / 60f;
        public Vector3 targetSyncVelocity = Vector3.zero;

        public string playerName;
        public ulong playerId;
        public bool isGrounded;

        // FIX: Cache renderers to avoid expensive GetComponentsInChildren every frame
        private Renderer[] _cachedRenderers = null;
        private float _rendererCheckTimer = 0f;
        private float rendererCheckInterval = 0.1f; // Check frequently to counter aggressive zone culling

        // FIX: Talking animation state
        private bool isTalking = false;
        private float talkingAnimationBlend = 0f;
        private float groundedGraceTimer = 0f;

#if DEBUG
        public static PlayerRepresentation debugRepresentation;
#endif

        public static AssetBundle playerRepBundle;
        public static bool debugShowPhysics = false;

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
            if (currentSkinBundle) currentSkinBundle.Unload(false);
        }

        // FIX: Helper to set layer for all child objects (prevents zone culling)
        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            Transform root = obj.transform;
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null)
                    SetLayerRecursive(child.gameObject, layer);
            }
        }

        private static void SetTagRecursive(GameObject obj, string tag)
        {
            if (obj == null) return;
            obj.tag = tag;
            Transform root = obj.transform;
            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null)
                    SetTagRecursive(child.gameObject, tag);
            }
        }

        private static void StripZoneCullingComponents(GameObject obj)
        {
            if (obj == null)
                return;

            Component[] components = obj.GetComponentsInChildren<Component>(true);
            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                Type type = component.GetType();
                string ns = type.Namespace ?? string.Empty;
                string name = type.Name ?? string.Empty;

                bool isZoneComponent = ns.StartsWith("StressLevelZero.Zones", StringComparison.Ordinal);
                bool isCullComponent = ns.StartsWith("StressLevelZero", StringComparison.Ordinal) &&
                                       (name.IndexOf("Cull", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        name.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        name.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isZoneComponent || isCullComponent)
                    UnityEngine.Object.Destroy(component);
            }
        }

        private static void StripLODGroups(GameObject obj)
        {
            if (obj == null) return;
            LODGroup[] lodGroups = obj.GetComponentsInChildren<LODGroup>(true);
            foreach (LODGroup lod in lodGroups)
            {
                if (lod != null)
                    UnityEngine.Object.Destroy(lod);
            }
        }

        public void RecreateRepresentations()
        {
            try
            {
                EntangleLogger.Verbose($"[RepCreation] Starting for player {playerName} (ID: {playerId})");

                // FIX: Null checks for bundle
                if (playerRepBundle == null)
                {
                    EntangleLogger.Error("[RepCreation] playerRepBundle is null! Cannot recreate representations.");
                    return;
                }

                // FIX: Load holographic material with null check
                EntangleLogger.Verbose("[RepCreation]   Loading holographic material...");
                Material holographicMat = playerRepBundle.LoadAsset<Material>("PlayerHolographic");
                if (holographicMat == null)
                {
                    EntangleLogger.Warn("▌[RepCreation]   ⚠ PlayerHolographic material not found in bundle");
                    repHologram = new Material(Shader.Find("Standard"));
                }
                else
                {
                    repHologram = Material.Instantiate(holographicMat);
                }

                // FIX: Load player rep model with null check
                EntangleLogger.Verbose("[RepCreation]   Instantiating player representation model...");
                GameObject playerRepPrefab = playerRepBundle.LoadAsset<GameObject>("PlayerRep");
                if (playerRepPrefab == null)
                {
                    EntangleLogger.Error("[RepCreation] PlayerRep prefab not found in bundle!");
                    return;
                }

                repFord = GameObject.Instantiate(playerRepPrefab);
                repFord.name = $"PlayerRep.{playerId}";

                repRoot = repFord.transform;
                EntangleLogger.Verbose("[RepCreation]   ✓ Model instantiated");

                // Put reps on a layer that Boneworks zone culling never touches.
                // Boneworks NPC rigs use layer 12 (EnemyColliders) which survives SceneZone transitions.
                // We duplicate that approach: same layer so zone logic ignores them, plus strip
                // any culling components and LODGroups to make them truly non-cullable.
                int repLayer = LayerMask.NameToLayer("EnemyColliders");
                if (repLayer < 0) repLayer = 12; // Fallback to raw index if name lookup fails

                SetLayerRecursive(repRoot.gameObject, repLayer);
                SetTagRecursive(repRoot.gameObject, "Untagged");
                StripZoneCullingComponents(repRoot.gameObject);
                StripLODGroups(repRoot.gameObject);
                EntangleLogger.Verbose($"[RepCreation]   ✓ Set to layer: {repLayer}");

                EntangleLogger.Verbose("[RepCreation]   Setting up SFX components...");
                Transform gunSFXTrans = repRoot.Find("GunSFX");
                if (gunSFXTrans != null) repGunSFX = gunSFXTrans.GetComponent<GunSFX>();

                Transform balloonSFXTrans = repRoot.Find("BalloonSFX");
                if (balloonSFXTrans != null) repBalloonSFX = balloonSFXTrans.GetComponent<GunSFX>();

                Transform stabSFXTrans = repRoot.Find("StabSFX");
                if (stabSFXTrans != null) repStabSFX = stabSFXTrans.GetComponent<GunSFX>();

                Transform puncherSFXTrans = repRoot.Find("PuncherSFX");
                if (puncherSFXTrans != null)
                {
                    // Try to get GravGunSFX first, fall back to GunSFX if not available
                    GravGunSFX gravGunSFX = puncherSFXTrans.GetComponent<GravGunSFX>();
                    if (gravGunSFX != null)
                    {
                        repPowerPunchSFX = gravGunSFX;
                    }
                    else
                    {
                        EntangleLogger.Verbose("[RepCreation]   ⚠ GravGunSFX not found on PuncherSFX, attempting GunSFX fallback");
                        GunSFX gunSFX = puncherSFXTrans.GetComponent<GunSFX>();
                        if (gunSFX != null)
                        {
                            // Create a temporary GravGunSFX wrapper or skip it
                            EntangleLogger.Verbose("[RepCreation]   ⚠ Using GunSFX as fallback for PuncherSFX");
                        }

                    }
                }

                EntangleLogger.Verbose("[RepCreation]   Setting up physics body...");
                Transform repBodyBody = repRoot.Find("Body");
                if (repBodyBody != null)
                {
                    repBody = repBodyBody.GetComponent<SLZ_Body>();
                    if (repBody != null) repBody.OnStart();
                }

                Transform ragdollTrans = repRoot.Find("Ragdoll");
                if (ragdollTrans != null) ragdollBody = ragdollTrans.GetComponent<SLZ_Body>();

                EntangleLogger.Verbose("[RepCreation]   Setting up animator...");
                Transform repAnimatorBody = repRoot.Find("Brett@neutral");
                if (repAnimatorBody != null)
                {
                    repAnimator = repAnimatorBody.GetComponent<Animator>();
                    if (repAnimator != null)
                    {
                        // FIX: Null check for animator controller
                        if (PlayerScripts.playerAnimatorController != null)
                        {
                            repAnimator.runtimeAnimatorController = PlayerScripts.playerAnimatorController;
                            activeAnimator = repAnimator;
                            EntangleLogger.Verbose("[RepCreation]   ✓ Animator controller assigned");
                        }
                        else
                        {
                            EntangleLogger.Warn("[RepCreation]   ⚠ PlayerAnimatorController is null, using default");
                            activeAnimator = repAnimator;
                        }

                        repAnimationManager = repAnimatorBody.GetComponent<CharacterAnimationManager>();

                        // Initialize animator state to idle/neutral
                        try
                        {
                            repAnimator.SetFloat("Speed", 0f);
                            repAnimator.SetInteger("AnimState", 0);
                            repAnimator.SetLayerWeight(1, 0f); // Disable upper body layer if it exists
                            EntangleLogger.Verbose("[RepCreation]   ✓ Animator configured");
                        }
                        catch (Exception ex)
                        {
                            EntangleLogger.Warn($"[RepCreation]   ⚠ Error initializing animator state: {ex.Message}");
                        }
                    }
                    else
                    {
                        EntangleLogger.Verbose("[RepCreation]   ⚠ Animator component not found on Brett@neutral");
                    }

                    repGeo = repAnimatorBody.Find("geoGrp");
                    repSHJnt = repAnimatorBody.Find("SHJntGrp");
                }
                else
                {
                    EntangleLogger.Verbose("[RepCreation]   ⚠ Brett@neutral transform not found");
                }

                EntangleLogger.Verbose("[RepCreation]   Getting transform references...");
                repTransforms[0] = repRoot.Find("Head");
                repTransforms[1] = repRoot.Find("Hand (left)");
                repTransforms[2] = repRoot.Find("Hand (right)");

                colliders = repRoot.GetComponentsInChildren<Collider>();
                EntangleLogger.Verbose($"[RepCreation]   Found {colliders.Length} colliders");

                // Create and setup the nametag canvas after getting the head transform
                EntangleLogger.Verbose("[RepCreation]   Creating nametag canvas...");
                repCanvas = new GameObject("RepCanvas");
                repCanvasComponent = repCanvas.AddComponent<Canvas>();
                repCanvasComponent.renderMode = RenderMode.WorldSpace;
                repCanvasTransform = repCanvas.transform;
                repCanvasTransform.localScale = Vector3.one / 180.0f;

                // Parent canvas to head to keep it with the player representation
                if (repTransforms[0] != null)
                {
                    repCanvasTransform.SetParent(repTransforms[0], false);
                    repCanvasTransform.localPosition = Vector3.up * 0.4f;
                    EntangleLogger.Verbose("[RepCreation]   ✓ Nametag parented to head");
                }
                else
                {
                    repCanvasTransform.position = repRoot.position + Vector3.up * 0.4f;
                    EntangleLogger.Verbose("[RepCreation]   ⚠ Nametag positioned at root");
                }

                GameObject nameTextObject = new GameObject("RepNameText");
                nameTextObject.transform.SetParent(repCanvasTransform, false);

                RectTransform nameRect = nameTextObject.AddComponent<RectTransform>();
                nameTextObject.AddComponent<CanvasRenderer>();
                TextMeshProUGUI textMesh = nameTextObject.AddComponent<TextMeshProUGUI>();

                nameRect.localPosition = Vector3.zero;
                nameRect.localRotation = Quaternion.identity;
                nameRect.sizeDelta = new Vector2(480f, 120f);

                textMesh.alignment = TextAlignmentOptions.Center;
                textMesh.enableAutoSizing = true;
                textMesh.fontSizeMin = 10f;
                textMesh.fontSizeMax = 24f;
                textMesh.text = playerName;
                repNameText = textMesh;

                // FIX: Add billboard script to make nametag always face player
                repCanvas.AddComponent<global::Entanglement.Representation.NametagBillboard>();

                // FIX: Hide nametag for local player so they can't see their own name
                bool isLocalPlayer = playerId == SteamIntegration.currentUser.m_SteamID;
                if (isLocalPlayer)
                {
                    repCanvas.SetActive(false);
                    EntangleLogger.Verbose($"[RepCreation]   ⚠ Nametag DISABLED for local player: {playerName} (ID: {playerId})");
                }
                else
                {
                    repCanvas.SetActive(true);
                    EntangleLogger.Verbose($"[RepCreation]   ✓ Nametag ENABLED for remote player: {playerName} (ID: {playerId})");
                }

                // FIX: Ensure all renderers are enabled so player doesn't disappear
                foreach (Renderer renderer in repRoot.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                    renderer.allowOcclusionWhenDynamic = false;

                    if (renderer is SkinnedMeshRenderer skinned)
                        skinned.updateWhenOffscreen = true;
                }

                if (isCustomSkinned && currentSkinPath != null)
                    PlayerSkinLoader.ApplyPlayermodel(this, currentSkinPath);
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"Error caught creating rep from user {playerId}: {e.Message}\n{e.StackTrace}");
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
            float dt = Time.fixedDeltaTime;

            // Compute velocity from the NETWORK TARGET positions, not the interpolated
            // repRoot.position.  The interpolation smooths the visual root which means
            // verticalDelta becomes artificially small.  The IK solver (FullBodyUpdate)
            // needs the actual elevation change so it can retarget feet immediately on
            // stairs, ramps and ledges.
            Vector3 networkDelta = targetSyncPosition - prevTargetSyncPosition;
            float packetDt = Mathf.Max(syncPacketInterval, dt);
            Vector3 targetVel = networkDelta / packetDt;

            if (targetVel.sqrMagnitude < 0.001f) targetVel = Vector3.zero;

            float rawVerticalVel = Mathf.Clamp(networkDelta.y / packetDt, -12f, 12f);

            if (isGrounded)
            {
                groundedGraceTimer = 0.15f;

                Vector3 horizontalTarget = new Vector3(targetVel.x, 0f, targetVel.z);
                Vector3 horizontalCurrent = new Vector3(repInputVel.x, 0f, repInputVel.z);
                horizontalCurrent = Vector3.Lerp(horizontalCurrent, horizontalTarget, dt * 10f);

                repInputVel = new Vector3(horizontalCurrent.x, rawVerticalVel, horizontalCurrent.z);
            }
            else
            {
                groundedGraceTimer -= dt;
                if (groundedGraceTimer > 0f)
                {
                    Vector3 horizontalTarget = new Vector3(targetVel.x, 0f, targetVel.z);
                    Vector3 horizontalCurrent = new Vector3(repInputVel.x, 0f, repInputVel.z);
                    horizontalCurrent = Vector3.Lerp(horizontalCurrent, horizontalTarget, dt * 6f);

                    repInputVel = new Vector3(horizontalCurrent.x, rawVerticalVel, horizontalCurrent.z);
                }
                else
                {
                    repInputVel = Vector3.Lerp(repInputVel, Vector3.zero, dt * 8f);
                }
            }

            repSavedVel = repInputVel;
            prevRepRootPos = repRoot.position;
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

                // Force the entire rep hierarchy to stay active and visible.
                // Boneworks zones can SetActive(false) on objects or disable renderers.
                EnsureRepVisible();

                // FIX: Update talking animation blend smoothly
                if (isTalking)
                {
                    talkingAnimationBlend = Mathf.Lerp(talkingAnimationBlend, 1f, Time.deltaTime * 5f);
                }
                else
                {
                    talkingAnimationBlend = Mathf.Lerp(talkingAnimationBlend, 0f, Time.deltaTime * 5f);
                }

                // Apply talking parameter to animator
                if (activeAnimator != null)
                {
                    activeAnimator.SetFloat("Talk", talkingAnimationBlend);
                }

                if (debugShowPhysics)
                    DrawPhysicsDebug();
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"UpdateIK Error for {playerId}: {e.Message}");
            }
        }

        /// <summary>
        /// Forces every GameObject and Renderer in the rep hierarchy to stay active.
        /// Boneworks zones can SetActive(false) on children or disable renderers;
        /// this counteracts that every IK tick so the remote player never disappears.
        /// </summary>
        private void EnsureRepVisible()
        {
            // Re-activate the root
            if (repRoot != null && !repRoot.gameObject.activeSelf)
                repRoot.gameObject.SetActive(true);

            // Re-activate the body
            if (repBody != null && repBody.gameObject != null && !repBody.gameObject.activeSelf)
                repBody.gameObject.SetActive(true);

            // Walk every child; re-enable any that zones disabled.
            if (repRoot != null)
            {
                foreach (Transform child in repRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (child != null && !child.gameObject.activeSelf)
                        child.gameObject.SetActive(true);
                }
            }

            // Renderer pass - cached for perf, refreshed periodically.
            if (_cachedRenderers == null || _rendererCheckTimer <= 0f)
            {
                _cachedRenderers = repRoot != null ? repRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                _rendererCheckTimer = rendererCheckInterval;
            }

            for (int i = 0; i < _cachedRenderers.Length; i++)
            {
                Renderer r = _cachedRenderers[i];
                if (r == null) continue;
                if (!r.enabled) r.enabled = true;
                r.allowOcclusionWhenDynamic = false;
                if (r is SkinnedMeshRenderer smr)
                    smr.updateWhenOffscreen = true;
            }
        }

        private void DrawPhysicsDebug()
        {
            if (colliders == null || colliders.Length == 0)
                return;

            Color c = new Color(0f, 1f, 1f, 0.85f);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col == null || !col.enabled)
                    continue;

                Bounds b = col.bounds;
                Vector3 min = b.min;
                Vector3 max = b.max;

                Vector3 p0 = new Vector3(min.x, min.y, min.z);
                Vector3 p1 = new Vector3(max.x, min.y, min.z);
                Vector3 p2 = new Vector3(max.x, min.y, max.z);
                Vector3 p3 = new Vector3(min.x, min.y, max.z);
                Vector3 p4 = new Vector3(min.x, max.y, min.z);
                Vector3 p5 = new Vector3(max.x, max.y, min.z);
                Vector3 p6 = new Vector3(max.x, max.y, max.z);
                Vector3 p7 = new Vector3(min.x, max.y, max.z);

                Debug.DrawLine(p0, p1, c, 0f, false);
                Debug.DrawLine(p1, p2, c, 0f, false);
                Debug.DrawLine(p2, p3, c, 0f, false);
                Debug.DrawLine(p3, p0, c, 0f, false);

                Debug.DrawLine(p4, p5, c, 0f, false);
                Debug.DrawLine(p5, p6, c, 0f, false);
                Debug.DrawLine(p6, p7, c, 0f, false);
                Debug.DrawLine(p7, p4, c, 0f, false);

                Debug.DrawLine(p0, p4, c, 0f, false);
                Debug.DrawLine(p1, p5, c, 0f, false);
                Debug.DrawLine(p2, p6, c, 0f, false);
                Debug.DrawLine(p3, p7, c, 0f, false);
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

        // FIX: Set talking state for mouth animation
        public void SetTalking(bool talking)
        {
            isTalking = talking;
            EntangleLogger.Verbose($"[Talking] {playerName} is {(talking ? "talking" : "silent")}");
        }

        public void IgnoreCollision(Rigidbody otherBody, bool ignore)
        {
            if (!otherBody) return;
            Collider[] otherColliders = otherBody.GetComponentsInChildren<Collider>();
            foreach (Collider col1 in colliders)
                foreach (Collider col2 in otherColliders) Physics.IgnoreCollision(col1, col2, ignore);
        }

        public static void GetPlayerTransforms()
        {
            try
            {
                // FIX: Better null checking and error handling
                if (PlayerScripts.playerRig != null)
                {
                    // Set the root to the RigManager's transform
                    syncedRoot = PlayerScripts.playerRig.transform;

                    // Use the direct references from the PhysicsRig rather than string searching
                    var physRig = PlayerScripts.playerRig.physicsRig;

                    if (physRig != null)
                    {
                        syncedPoints[0] = physRig.m_head;
                        if (syncedPoints[0] == null)
                            EntangleLogger.Warn("[PlayerTransforms] Head not found on physicsRig");

                        if (PlayerScripts.playerLeftHand != null)
                        {
                            syncedPoints[1] = PlayerScripts.playerLeftHand.transform;
                        }
                        else
                        {
                            EntangleLogger.Warn("[PlayerTransforms] Left hand not initialized");
                        }

                        if (PlayerScripts.playerRightHand != null)
                        {
                            syncedPoints[2] = PlayerScripts.playerRightHand.transform;
                        }
                        else
                        {
                            EntangleLogger.Warn("[PlayerTransforms] Right hand not initialized");
                        }
                    }
                    else
                    {
                        EntangleLogger.Error("[PlayerTransforms] PhysicsRig is null!");
                    }
                }
                else
                {
                    // Fallback just in case PlayerScripts hasn't initialized it yet
                    EntangleLogger.Verbose("[PlayerTransforms] PlayerScripts.playerRig is null, using fallback");
                    var rigManager = GameObject.FindObjectOfType<StressLevelZero.Rig.RigManager>();
                    if (rigManager != null && rigManager.physicsRig != null)
                    {
                        syncedRoot = rigManager.transform;
                        syncedPoints[0] = rigManager.physicsRig.m_head;
                        syncedPoints[1] = rigManager.physicsRig.leftHand.transform;
                        syncedPoints[2] = rigManager.physicsRig.rightHand.transform;
                        EntangleLogger.Log("[PlayerTransforms] ✓ Fallback transforms initialized");
                    }
                    else
                    {
                        EntangleLogger.Error("[PlayerTransforms] Could not find RigManager or PhysicsRig!");
                    }
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"[PlayerTransforms] Error getting player transforms: {ex.Message}\n{ex.StackTrace}");
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

            // Add animation state
            if (syncedRoot != null)
            {
                data.isJumping = !data.isGrounded;
            }

            return data;
        }

        public static AnimationSyncData GetAnimationSyncData()
        {
            // Check if all required components are initialized
            if (syncedRoot == null || PlayerScripts.playerRig == null)
                return null;

            AnimationSyncData data = new AnimationSyncData();
            data.userId = (ulong)SteamIntegration.currentUser.m_SteamID;

            // Calculate movement speed based on position change
            Vector3 currentPos = syncedRoot.position;
            Vector3 posChange = currentPos - syncedRoot.position; // This frame's position change

            // Use head position for more accurate movement calculation
            if (syncedPoints[0] != null)
            {
                Vector3 headPos = syncedPoints[0].position;
                Vector3 horizontalDelta = new Vector3(headPos.x - syncedRoot.position.x, 0, headPos.z - syncedRoot.position.z);
                data.movementSpeed = horizontalDelta.magnitude / Time.deltaTime;
            }

            data.movementSpeed = Mathf.Clamp(data.movementSpeed, 0f, 10f);

            // Get grounding state
            data.isJumping = !PlayerScripts.playerGrounder.isGrounded;
            data.isFalling = !PlayerScripts.playerGrounder.isGrounded && data.jumpHeight < 0f;

            // Get jump height from head rigidbody if available
            if (PlayerScripts.playerLeftHand != null && PlayerScripts.playerLeftHand.GetComponent<Rigidbody>() != null)
            {
                Rigidbody headRb = PlayerScripts.playerLeftHand.GetComponent<Rigidbody>();
                if (headRb != null)
                {
                    data.bodyVelocity = headRb.velocity;
                    data.jumpHeight = headRb.velocity.y;
                }
            }

            // Set animation state based on movement
            if (data.movementSpeed > 2f)
                data.animState = 2; // Running
            else if (data.movementSpeed > 0.5f)
                data.animState = 1; // Walking
            else
                data.animState = 0; // Idle

            return data;
        }

        public static void SyncPlayerReps()
        {
            if (!SteamIntegration.hasLobby)
                return;

            // Ensure we have a valid node
            if (Node.activeNode == null)
                return;

            // Rate limit player syncs to configured interval
            lastPlayerSyncTime += Time.deltaTime;
            if (lastPlayerSyncTime < PLAYER_SYNC_INTERVAL)
                return;

            lastPlayerSyncTime -= PLAYER_SYNC_INTERVAL;
            if (lastPlayerSyncTime > PLAYER_SYNC_INTERVAL)
                lastPlayerSyncTime = 0f;

            var syncData = GetPlayerSyncData();

            if (syncData != null)
            {
                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerRepSync, syncData);
                if (message != null)
                {
                    Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
                }
            }
            else
                GetPlayerTransforms();
        }

        public static void SyncAnimationState()
        {
            if (!SteamIntegration.hasLobby)
                return;

            // Ensure we have a valid node
            if (Node.activeNode == null)
                return;

            // Rate limit animation syncs to configured interval
            lastAnimationSyncTime += Time.deltaTime;
            if (lastAnimationSyncTime < ANIMATION_SYNC_INTERVAL)
                return;

            lastAnimationSyncTime -= ANIMATION_SYNC_INTERVAL;
            if (lastAnimationSyncTime > ANIMATION_SYNC_INTERVAL)
                lastAnimationSyncTime = 0f;

            var animData = GetAnimationSyncData();

            if (animData != null)
            {
                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.AnimationSync, animData);
                if (message != null)
                {
                    Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
                }
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

                // FIX: Ensure player representation stays visible
                if (!rep.repRoot.gameObject.activeSelf)
                {
                    rep.repRoot.gameObject.SetActive(true);
                }

                // EnsureRepVisible is now called inside UpdateIK() every tick.
                // No separate renderer check needed here.

                // Update interpolation
                if (rep.interpolationAlpha < 1f)
                {
                    rep.interpolationAlpha += Time.fixedDeltaTime * rep.interpolationSpeed;
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rep.interpolationAlpha));
                    float sinceLastPacket = Time.realtimeSinceStartup - rep.lastSyncReceiveTime;
                    float extrapolationTime = Mathf.Clamp(sinceLastPacket, 0f, rep.syncPacketInterval * 1.5f);
                    Vector3 predictedTarget = rep.targetSyncPosition + rep.targetSyncVelocity * extrapolationTime;
                    Vector3 nextPos = Vector3.LerpUnclamped(rep.lastSyncPosition, predictedTarget, blend);

                    // When grounded, snap Y directly to the network target.
                    // Lerping Y causes the IK solver to receive a near-zero vertical
                    // velocity on stairs/ramps, which makes feet stay at the old floor level.
                    if (rep.isGrounded)
                    {
                        nextPos.y = predictedTarget.y;
                    }

                    rep.repRoot.position = nextPos;
                }

                float dist = (centerPos - rep.repRoot.position).sqrMagnitude;

                // FIX: Removed aggressive distance culling - always update IK for smooth animation
                // This prevents players from disappearing when moving to different rooms
                rep.UpdateIK();
                if (rep.repCanvasTransform != null && rep.repCanvasTransform.gameObject != null)
                    rep.repCanvasTransform.gameObject.SetActive(Client.nameTagsVisible);
            }
        }

        public static void ForceRefreshAllRemoteRepresentations()
        {
            foreach (PlayerRepresentation rep in representations.Values)
            {
                if (rep == null || rep.repRoot == null)
                    continue;

                if (rep.playerId == SteamIntegration.currentUser.m_SteamID)
                    continue;

                if (!rep.repRoot.gameObject.activeSelf)
                    rep.repRoot.gameObject.SetActive(true);

                Renderer[] renderers = rep._cachedRenderers ?? rep.repRoot.GetComponentsInChildren<Renderer>(true);
                rep._cachedRenderers = renderers;

                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    renderer.enabled = true;
                    renderer.allowOcclusionWhenDynamic = false;

                    if (renderer is SkinnedMeshRenderer skinned)
                        skinned.updateWhenOffscreen = true;
                }

                if (rep.repCanvasTransform != null && rep.repCanvasTransform.gameObject != null)
                    rep.repCanvasTransform.gameObject.SetActive(Client.nameTagsVisible);
            }
        }
    }
}