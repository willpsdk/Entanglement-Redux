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
using Entanglement.Entities;

using UnityEngine;

using MelonLoader;

using TMPro;

using UnityEngine.UI;

using Entanglement.Data;

using StressLevelZero;
using StressLevelZero.VRMK;
using StressLevelZero.AI;
using StressLevelZero.Combat;
using StressLevelZero.Player;
using StressLevelZero.SFX;
using StressLevelZero.Interaction;

namespace Entanglement.Representation
{
    public class PlayerRepresentation {
        public static float legJitter = 10f;

        public static Dictionary<long, PlayerRepresentation> representations = new Dictionary<long, PlayerRepresentation>();

        /// <summary>Alias used across the codebase (same dictionary as <see cref="representations"/>).</summary>
        public static Dictionary<long, PlayerRepresentation> reps { get { return representations; } set { representations = value; } }

        public static Transform[] syncedPoints = new Transform[3];
        public static Transform syncedRoot;

        public Transform[] repTransforms = new Transform[3];
        public Transform repRoot;

        public GameObject repFord;
        public Material repHologram;
        public GameObject repCanvas;
        public Canvas repCanvasComponent;
        public Transform repCanvasTransform;
        CanvasGroup repCanvasGroup;
        public TextMeshProUGUI repNameText;
        public RawImage repTalkingIcon;
        public Transform repSkull;

        public Transform repGeo;
        public Transform repSHJnt;

        public Collider[] colliders = new Collider[0];

        Collider[] modelColliders = new Collider[0];
        bool enemyTargetReady;
        GameObject enemyTarget;
        TriggerRefProxy enemyTargetProxy;

        /// <summary>This rep's target proxy, what NPC senses report as their target.</summary>
        public TriggerRefProxy EnemyProxy => enemyTargetReady && enemyTarget ? enemyTargetProxy : null;

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
        // A model being loaded in the background (PlayerSkinLoader), and which request is newest.
        public string loadingSkinPath;
        public int skinLoadGeneration;

        public bool isCustomSkinned;

        public Vector3 repInputVel = Vector3.zero;
        public Vector3 repSavedVel = Vector3.zero;
        public Vector3 prevRepRootPos = Vector3.zero;

        public string playerName;
        public long playerId;
        public bool isGrounded;

        // Stub hands used to hold props on the puppet. Never call Hand.AttachObject on these.

        // Buffered network targets, applied with smoothing every tick instead of snapping on packet arrival
        public bool hasNetTarget = false;
        public Vector3 netRootPosition;
        public Vector3 netRootVelocity;
        public float netReceiveTime;
        public Vector3[] netPositions = new Vector3[3];
        public Quaternion[] netRotations = new Quaternion[3];
        public Vector3[] netLimbVelocities = new Vector3[3];

        Transform[] posedBones;
        Vector3[] posePrev;
        Vector3[] poseCurr;
        Quaternion[] rotPrev;
        Quaternion[] rotCurr;
        bool hasSolvedPose;

        // Local grab of this remote player - hard-track + damp launches (Fusion-style locked body)
        bool heldByLocalPlayer;
        bool grabCollisionIgnored;
        float heldReleaseUntil; // Brief post-release window that keeps Y extrapolation off

        public const float repFollowSharpness = 28f;   // Exponential smoothing rate for the root
        public const float repLimbSharpness = 48f;
        public const float repExtrapolationLimit = 0.28f; // Never predict further than this past the last packet
        public const float repSnapDistance = 2.5f;        // Teleport instead of chasing when further than this
        public const float repMaxPredictedSpeed = 18f;  // Caps dead reckoning speed so a teleport can't fling the rep
        public const float crouchVerticalDelta = 0.06f;
        public const float crouchLateralMax = 0.45f;
        public const float heldMaxPlanarStep = 0.10f;
        public const float heldMaxVerticalStep = 0.018f; // Crouch ΔY through a grab joint — keep tiny to stop launches
        public const float heldReleaseCooldown = 0.18f;
        public const float localHoldVerticalDamp = 2.5f; // Clamp our own PhysBody Y speed while jointed to a puppet

        public static PlayerRepresentation debugRepresentation;

        // Solo visual-only held ghost. Does NOT touch TransformSyncable / ObjectSync.
        public static float representationTestOffset = 1.5f;
        public const long TesterRepId = -2L;

        class GhostPart {
            public Transform source;
            public Transform part;
            public MeshFilter filter;
            public SkinnedMeshRenderer skinned;
            public Mesh baked;
            public Vector3 localPos;
            public Quaternion localRot = Quaternion.identity;
        }

        class HeldGhost {
            public GameObject proxy;
            public GameObject source;
            public readonly List<GhostPart> parts = new List<GhostPart>();
        }

        const int MaxGhostParts = 64;

        // Indexed 1 = left, 2 = right, to match repTransforms. Slot 0 is unused.
        static readonly HeldGhost[] debugGhosts = { null, new HeldGhost(), new HeldGhost() };

        static bool AnyDebugGhost => debugGhosts[1].proxy != null || debugGhosts[2].proxy != null;
        static readonly Vector3[] debugPosScratch = new Vector3[3];
        static readonly Quaternion[] debugRotScratch = new Quaternion[3];

        public static bool RepresentationTestActive => debugRepresentation != null;

        public static int RemoteCount => representations.Count + (debugRepresentation != null ? 1 : 0);

        public static IEnumerable<KeyValuePair<long, PlayerRepresentation>> Remotes() {
            foreach (KeyValuePair<long, PlayerRepresentation> pair in representations)
                yield return pair;
            if (debugRepresentation != null)
                yield return new KeyValuePair<long, PlayerRepresentation>(TesterRepId, debugRepresentation);
        }

        public static bool IsRemote(long id) => representations.ContainsKey(id) || (debugRepresentation != null && id == TesterRepId);

        public static void StartRepresentationTest() {
            EnsureBundle();
            StopRepresentationTest();
            debugRepresentation = new PlayerRepresentation("Tester", TesterRepId);
            SetDebugTracing(true);
        }

        public static void StopRepresentationTest() {
            try { DestroyDebugLoopback(); } catch { }
            try { debugRepresentation?.DeleteRepresentations(); } catch { }
            debugRepresentation = null;
            debugFrozen = false;
            SetDebugTracing(false);
        }

        public static bool debugFrozen;

        public static int SendNpcsAtDummy(float range = 25f) {
            if (debugRepresentation == null)
                return 0;
            TriggerRefProxy proxy = debugRepresentation.EnemyProxy;
            if (proxy == null || !debugRepresentation.repRoot)
                return 0;

            Vector3 at = debugRepresentation.repRoot.position;
            int sent = 0;
            AIBrain[] brains;
            try { brains = UnityEngine.Object.FindObjectsOfType<AIBrain>(); }
            catch { return 0; }

            for (int i = 0; i < brains.Length; i++) {
                try {
                    AIBrain brain = brains[i];
                    if (!brain || brain.isDead || brain.behaviour == null)
                        continue;
                    if ((brain.behaviour.transform.position - at).sqrMagnitude > range * range)
                        continue;
                    brain.behaviour.SetAgro(proxy);
                    sent++;
                    EntangleLogger.Debug("[Attack] Sent " + brain.transform.root.name + "/" + brain.name + " at the dummy");
                }
                catch (Exception e) { EntangleLogger.Debug("[Attack] SetAgro failed: " + e.Message); }
            }
            return sent;
        }

        static void SetDebugTracing(bool on) {
            DebugLog.Apply();
#if DEBUG
            try { Entanglement.Voice.VoiceManager.debugVoiceOnRep = on; } catch { }
#endif
        }

        static void FeedDebugMirror() {
            if (debugRepresentation == null || !debugRepresentation.repRoot || debugFrozen)
                return;

            PlayerRepSyncData data = BuildPlayerSyncData();
            if (data == null)
                return;

            try { UpdateDebugHeldLoopback(); } catch { }

            Vector3 offset = Vector3.forward * representationTestOffset;
            for (int r = 0; r < 3; r++) {
                debugPosScratch[r] = data.simplifiedTransforms[r].position + offset;
                debugRotScratch[r] = data.simplifiedTransforms[r].rotation.ExpandQuat();
            }

            debugRepresentation.isGrounded = data.isGrounded;
            debugRepresentation.SetNetTargets(data.rootPosition + offset, debugPosScratch, debugRotScratch);
            debugRepresentation.UpdateFingers(Handedness.LEFT, data.simplifiedLeftHand);
            debugRepresentation.UpdateFingers(Handedness.RIGHT, data.simplifiedRightHand);
        }

        static void UpdateDebugHeldLoopback() {
            if (debugRepresentation == null) {
                DestroyDebugLoopback();
                return;
            }

            UpdateGhostForHand(1, PlayerScripts.playerLeftHand);
            UpdateGhostForHand(2, PlayerScripts.playerRightHand);
        }

        static void UpdateGhostForHand(int handIdx, Hand hand) {
            HeldGhost ghost = debugGhosts[handIdx];

            GameObject held = null;
            if (hand && hand.m_CurrentAttachedObject) {
                try { held = ResolveHeldRoot(hand.m_CurrentAttachedObject); }
                catch { held = null; }
            }

            if (held != ghost.source) {
                DestroyGhost(ghost);
                ghost.source = held;
                if (held)
                    CreateGhost(ghost, held);
            }

            if (!held || !hand || !ghost.proxy)
                return;

            Transform handTransform = hand.transform;
            for (int i = ghost.parts.Count - 1; i >= 0; i--) {
                GhostPart gp = ghost.parts[i];
                try {
                    if (gp.source == null || gp.part == null) {
                        ghost.parts.RemoveAt(i);
                        continue;
                    }

                    gp.localPos = handTransform.InverseTransformPoint(gp.source.position);
                    gp.localRot = Quaternion.Inverse(handTransform.rotation) * gp.source.rotation;
                }
                catch {
                    ghost.parts.RemoveAt(i);
                }
            }
        }

        static GameObject ResolveHeldRoot(GameObject attached) {
            if (attached == null)
                return null;

            try {
                StressLevelZero.Pool.Poolee poolee = attached.transform.root
                    ? StressLevelZero.Pool.Poolee.Cache.Get(attached.transform.root.gameObject)
                    : null;
                if (poolee)
                    return attached.transform.root.gameObject;
            }
            catch { }

            try {
                Rigidbody body = attached.GetComponentInParent<Rigidbody>();
                if (body)
                    return body.gameObject;
            }
            catch { }

            return attached;
        }

        // Apply after PresentInterpolatedPose so the ghosts stick to the visible dummy hands.
        static void PresentDebugHeldLoopback() {
            if (debugRepresentation == null)
                return;

            for (int handIdx = 1; handIdx <= 2; handIdx++) {
                HeldGhost ghost = debugGhosts[handIdx];
                if (!ghost.proxy)
                    continue;

                Transform dummyHand = debugRepresentation.repTransforms[handIdx];
                if (!dummyHand)
                    continue;

                for (int i = 0; i < ghost.parts.Count; i++) {
                    GhostPart gp = ghost.parts[i];
                    try {
                        if (gp.part == null)
                            continue;

                        gp.part.SetPositionAndRotation(
                            dummyHand.TransformPoint(gp.localPos),
                            dummyHand.rotation * gp.localRot);

                        if (gp.skinned != null && gp.filter != null && gp.baked != null)
                            gp.skinned.BakeMesh(gp.baked);
                    }
                    catch { }
                }
            }
        }

        static void CreateGhost(HeldGhost ghost, GameObject held) {
            try {
                ghost.proxy = new GameObject("DebugHeldLoopback");

                foreach (MeshFilter filter in held.GetComponentsInChildren<MeshFilter>(false)) {
                    MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || sourceRenderer == null)
                        continue;
                    AddGhostPart(ghost, filter.transform, filter.sharedMesh, sourceRenderer.sharedMaterials, null);
                }

                foreach (SkinnedMeshRenderer skinned in held.GetComponentsInChildren<SkinnedMeshRenderer>(false)) {
                    if (skinned == null || skinned.sharedMesh == null)
                        continue;
                    AddGhostPart(ghost, skinned.transform, skinned.sharedMesh, skinned.sharedMaterials, skinned);
                }
            }
            catch {
                DestroyGhost(ghost);
            }
        }

        static void AddGhostPart(HeldGhost ghost, Transform source, Mesh mesh,
                                 Il2CppReferenceArray<Material> materials, SkinnedMeshRenderer skinned) {
            if (ghost.parts.Count >= MaxGhostParts)
                return;

            GameObject part = new GameObject("mesh");
            part.transform.SetParent(ghost.proxy.transform, true);
            part.transform.localScale = source.lossyScale;

            GhostPart gp = new GhostPart {
                source = source,
                part = part.transform,
                filter = part.AddComponent<MeshFilter>(),
                skinned = skinned,
            };

            if (skinned != null) {
                gp.baked = new Mesh();
                gp.filter.sharedMesh = gp.baked;
            }
            else {
                gp.filter.sharedMesh = mesh;
            }

            part.AddComponent<MeshRenderer>().sharedMaterials = materials;
            ghost.parts.Add(gp);
        }

        static void DestroyGhost(HeldGhost ghost) {
            for (int i = 0; i < ghost.parts.Count; i++) {
                try {
                    if (ghost.parts[i].baked != null)
                        GameObject.Destroy(ghost.parts[i].baked);
                }
                catch { }
            }
            ghost.parts.Clear();

            if (ghost.proxy) {
                GameObject.Destroy(ghost.proxy);
                ghost.proxy = null;
            }
            ghost.source = null;
        }

        static void DestroyDebugLoopback() {
            for (int handIdx = 1; handIdx <= 2; handIdx++)
                DestroyGhost(debugGhosts[handIdx]);
        }
        public static AssetBundle playerRepBundle;

        public static void LoadBundle() {
            if (playerRepBundle != null)
                return;

            playerRepBundle = EmebeddedAssetBundle.LoadFromAssembly(EntanglementMod.entanglementAssembly, "Entanglement.resources.playerrep.eres");

            if (playerRepBundle == null)
                throw new NullReferenceException("playerRepBundle is null! Did you forget to compile the player bundle into the dll?");
        }

        public static void EnsureBundle() {
            if (playerRepBundle != null)
                return;
            try { LoadBundle(); }
            catch (Exception e) {
                EntangleLogger.Error("EnsureBundle: " + e.Message);
            }
        }

        public bool previewOnly;
        public string lastCreateError;

        public PlayerRepresentation(string playerName, long playerId, bool preview = false) {
            this.playerName = playerName;
            this.playerId = playerId;
            previewOnly = preview;
            RecreateRepresentations();
            if (!previewOnly)
                NetworkPlayer.Register(this);
        }

        /// <summary>Physics rig puppet (Fusion RigPuppet / RigManager-shaped) for this remote.</summary>
        public PhysicsRigPuppet PhysicsPuppet => physicsPuppet;

        public void DeleteRepresentations(bool fadeAway = false) {
            if (grabCollisionIgnored)
                SetGrabCollisionIgnored(false);
            heldByLocalPlayer = false;

            ClearPoseCache();
            StopFade();

            if (!previewOnly)
                NetworkPlayer.Unregister(this);

            GameObject.Destroy(repCanvas);
            DestroyEnemyTarget();

            // Parked or already gone from sight (dead, left the level): nothing to see go.
            if (fadeAway && !previewOnly && !parked && !Hidden && repFord) {
                try {
                    RepFade.FadeAway(LeaveFade, repFord, currentSkinObject);
                    return;
                }
                catch (Exception e) { EntangleLogger.Debug("[Rep] Could not fade out: " + e.Message); }
            }

            GameObject.Destroy(repFord);
            if (currentSkinObject) GameObject.Destroy(currentSkinObject);
        }

        public void RecreateRepresentations() {
            if (grabCollisionIgnored)
                SetGrabCollisionIgnored(false);
            heldByLocalPlayer = false;
            heldReleaseUntil = 0f;
            DestroyEnemyTarget();
            // NetworkPlayer stays registered across recreate; rebind puppet after Ford is built.
            hasNetTarget = false;
            ClearPoseCache();
            overlapIgnored = false;
            graceMine.Clear();
            graceTheirs.Clear();
            overlapRig = 0;
            appearedAt = -100f;
            parked = false;
            // Whatever was hidden went with the old model; the new one starts out showing.
            StopFade();
            deadHidden = null;
            deadUntil = 0f;
            awayHidden = null;
            awayForLoad = false;


            if (repFord)
                GameObject.Destroy(repFord);
            if (repCanvas)
                GameObject.Destroy(repCanvas);
            repFord = null;
            repRoot = null;
            repCanvas = null;
            repCanvasTransform = null;
            repCanvasGroup = null;
            repNameText = null;
            repSkull = null;
            repTalkingIcon = null;

            string step = "bundle";
            try {
                EnsureBundle();
                if (playerRepBundle == null) {
                    lastCreateError = "playerRepBundle missing";
                    return;
                }

                step = "instantiate";
                GameObject prefab = playerRepBundle.LoadAsset<GameObject>("PlayerRep");
                repFord = GameObject.Instantiate(prefab);
                repFord.name = "PlayerRep." + IdText(playerId);
                repRoot = repFord.transform;
                repFord.SetActive(true);

                step = "material";
                try {
                    repHologram = Material.Instantiate(playerRepBundle.LoadAsset<Material>("PlayerHolographic"));
                } catch { }

                step = "parts";
                repTransforms[0] = repRoot.Find("Head");
                repTransforms[1] = repRoot.Find("Hand (left)");
                repTransforms[2] = repRoot.Find("Hand (right)");
                colliders = repRoot.GetComponentsInChildren<Collider>();
                modelColliders = colliders;
                if (!previewOnly) {
                    CentreHeadColliders();
                    NoSpeculativeContacts();
                    SilenceKinematicModelPhysics();
                    DescribeModelColliders();
                }

                try { repGunSFX = repRoot.Find("GunSFX").GetComponent<GunSFX>(); } catch { }
                try { repBalloonSFX = repRoot.Find("BalloonSFX").GetComponent<GunSFX>(); } catch { }
                try { repStabSFX = repRoot.Find("StabSFX").GetComponent<GunSFX>(); } catch { }
                try { repPowerPunchSFX = repRoot.Find("PuncherSFX").GetComponent<GravGunSFX>(); } catch { }

                Transform body = repRoot.Find("Body");
                if (body) {
                    repBody = body.GetComponent<SLZ_Body>();
                    try { if (repBody) repBody.OnStart(); } catch { }
                }
                Transform ragdoll = repRoot.Find("Ragdoll");
                if (ragdoll)
                    ragdollBody = ragdoll.GetComponent<SLZ_Body>();

                Transform anim = repRoot.Find("Brett@neutral");
                if (anim) {
                    repAnimator = anim.GetComponent<Animator>();
                    activeAnimator = repAnimator;
                    if (repAnimator && PlayerScripts.playerAnimatorController)
                        repAnimator.runtimeAnimatorController = PlayerScripts.playerAnimatorController;
                    repAnimationManager = anim.GetComponent<CharacterAnimationManager>();
                    repGeo = anim.Find("geoGrp");
                    repSHJnt = anim.Find("SHJntGrp");
                }

                step = "hands";
                if (previewOnly)
                    DisableColliders();
                else {
                    try { EnsureEnemyTarget(); } catch { }
                    SilenceKinematicModelPhysics();
                }

                if (isCustomSkinned && currentSkinPath != null && loadingSkinPath == null)
                    try { PlayerSkinLoader.ApplyPlayermodel(this, currentSkinPath); } catch { }

                step = "nametag";
                try { BuildNametag(); }
                catch (Exception nametag) {
                    EntangleLogger.Warn("Nametag failed: " + nametag.Message);
                }

                // Out of the world until this player's first update here; see Park.
                if (!previewOnly)
                    Park();
            }
            catch (Exception e) {
                lastCreateError = step + ": " + e.Message;
                EntangleLogger.Error("Error caught creating rep at " + step + ": " + e.Message);
            }
        }

        static readonly Vector3 ParkSpot = new Vector3(0f, -10000f, 0f);
        const float StaleAfter = 3f;
        bool parked;
        float lastPacketReal;

        public bool Parked => parked;

        void Park() {
            parked = true;
            hasNetTarget = false;
            ClearPoseCache();
            try {
                if (repRoot)
                    repRoot.position = ParkSpot;
                for (int r = 0; r < repTransforms.Length; r++) {
                    if (repTransforms[r])
                        repTransforms[r].position = ParkSpot;
                }
                if (enemyTargetBody) {
                    enemyTargetBody.position = ParkSpot;
                    enemyTargetBody.velocity = Vector3.zero;
                }
                if (repCanvas)
                    repCanvas.SetActive(false);
            }
            catch { }
        }

        void ParkIfStale() {
            if (parked || !hasNetTarget || previewOnly || IsTestDummy)
                return;
            if (Time.realtimeSinceStartup - lastPacketReal > StaleAfter)
                FadeOutForLoad(false);
        }

        const float LoadFade = 0.8f;
        const float LoadQuiet = 1f;       // this long silent: their loading screen is up
        const float LoadHoldMax = 20f;    // said they were loading but never did
        List<Renderer> awayHidden;
        bool awayForLoad;
        float awaySince;

        /// <summary>Out of sight: dead, or gone from this level.</summary>
        public bool Hidden => deadHidden != null || awayHidden != null;

        public void FadeOutForLoad(bool announced) {
            if (previewOnly || parked)
                return;
            if (announced) {
                awayForLoad = true;
                awaySince = Time.realtimeSinceStartup;
            }
            if (awayHidden != null)
                return;

            // The nametag fades with the body (StepFade).
            awayHidden = new List<Renderer>();

            // Dead: the body is already on its way out of sight; finish that now and go.
            if (deadHidden != null) {
                StopFade();
                for (int i = 0; i < deadHidden.Count; i++) {
                    try {
                        if (deadHidden[i])
                            deadHidden[i].enabled = false;
                    }
                    catch { }
                }
                Park();
                return;
            }

            float from = 1f;
            if (fade != null) {
                from = fade.Alpha;
                StopFade();
            }
            CollectShown(repFord, awayHidden);
            CollectShown(currentSkinObject, awayHidden);
            fade = RepFade.Start(awayHidden, false, LoadFade, from);
        }

        void UpdateAway() {
            // Faded out: out of the world, and out of every NPC's and zone's sight, until they speak.
            if (awayHidden != null && !parked && fade == null)
                Park();

            if (awayForLoad) {
                float now = Time.realtimeSinceStartup;
                if (now - lastPacketReal > LoadQuiet || now - awaySince > LoadHoldMax)
                    awayForLoad = false;
            }
        }

        static float lastLocalLoad = -10f;

        public static void OnLocalLoadStarting() {
            if (!SteamIntegration.hasLobby)
                return;
            if (Time.realtimeSinceStartup - lastLocalLoad < 3f)
                return;
            lastLocalLoad = Time.realtimeSinceStartup;

            foreach (PlayerRepresentation rep in representations.Values) {
                try { rep?.FadeOutForLoad(true); }
                catch { }
            }

            try { PlayerEventMessageHandler.Send(PlayerEventType.Loading); }
            catch (Exception e) { EntangleLogger.Debug("[Rep] Could not say we are loading: " + e.Message); }
        }

        const float DeadHoldMax = 6f;
        const float DeathFade = 1f;     // out, while the body drops and their eyes close
        const float BackFade = 0.8f;    // in, on the respawn
        const float ArriveFade = 1f;    // in, on joining or arriving in the level
        const float LeaveFade = 1f;     // out, on leaving the lobby
        float deadUntil;
        List<Renderer> deadHidden;
        RepFade fade;

        public bool Dead => deadHidden != null;

        public void HideWhileDead() {
            if (previewOnly)
                return;
            deadUntil = Time.realtimeSinceStartup + DeadHoldMax;
            if (deadHidden != null)
                return;

            // An arrival still fading in goes out from wherever it had got to.
            float from = 1f;
            if (fade != null) {
                from = fade.Alpha;
                StopFade();
            }

            deadHidden = new List<Renderer>();
            CollectShown(repFord, deadHidden);
            CollectShown(currentSkinObject, deadHidden);

            // Switched off by the fade once it is done, and the nametag with it (StepFade).
            fade = RepFade.Start(deadHidden, false, DeathFade, from);

            EntangleLogger.Debug("[Death] Fading out the rep of " + IdText(playerId) + " while their body is down ("
                + deadHidden.Count + " renderers)");
        }

        /// <summary>Their player is alive again.</summary>
        public void ShowAfterDeath() {
            deadUntil = 0f;
            UpdateDead();
        }

        static void CollectShown(GameObject root, List<Renderer> into) {
            if (!root)
                return;
            try {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false)) {
                    if (renderer && renderer.enabled)
                        into.Add(renderer);
                }
            }
            catch { }
        }

        void UpdateDead() {
            if (deadHidden == null || Time.realtimeSinceStartup < deadUntil)
                return;

            // Back up, but gone from this level meanwhile: it comes back with them (FadeIn).
            if (awayHidden != null) {
                awayHidden.AddRange(deadHidden);
                deadHidden = null;
                return;
            }

            // Back from wherever the fade out had got to, if it hadn't finished.
            float from = 0f;
            if (fade != null) {
                from = fade.Alpha;
                StopFade();
            }

            // Only what was switched off here; the playermodel's own hidden parts stay hidden.
            List<Renderer> back = deadHidden;
            deadHidden = null;
            for (int i = back.Count - 1; i >= 0; i--) {
                try {
                    if (back[i])
                        back[i].enabled = true;
                    else
                        back.RemoveAt(i);
                }
                catch { back.RemoveAt(i); }
            }
            fade = RepFade.Start(back, true, BackFade, from);
            EntangleLogger.Debug("[Death] " + IdText(playerId) + " is back");
        }

        void FadeIn() {
            if (previewOnly)
                return;

            List<Renderer> shown = awayHidden;
            awayHidden = null;

            // Dead: what is switched off is theirs, and comes back when they respawn (UpdateDead).
            if (IsTestDummy || deadHidden != null)
                return;

            // What leaving the level switched off comes back first.
            if (shown != null) {
                for (int i = shown.Count - 1; i >= 0; i--) {
                    try {
                        if (shown[i])
                            shown[i].enabled = true;
                        else
                            shown.RemoveAt(i);
                    }
                    catch { shown.RemoveAt(i); }
                }
            }

            float from = 0f;
            if (fade != null) {
                from = fade.Alpha;
                StopFade();
            }
            if (shown == null || shown.Count == 0) {
                shown = new List<Renderer>();
                CollectShown(repFord, shown);
                CollectShown(currentSkinObject, shown);
            }
            fade = RepFade.Start(shown, true, ArriveFade, from);
        }

        void StopFade() {
            if (fade == null)
                return;
            try { fade.Stop(); }
            catch { }
            fade = null;
        }

        void StepFade() {
            if (fade != null) {
                bool running;
                try { running = fade.Step(); }
                catch { running = false; }
                if (!running)
                    fade = null;
            }

            try {
                if (repCanvasGroup)
                    repCanvasGroup.alpha = fade != null ? fade.Alpha : Hidden ? 0f : 1f;
            }
            catch { }
        }

        public void OnModelChanged() {
            if (previewOnly)
                return;
            try {
                List<Renderer> shown = new List<Renderer>();
                CollectShown(repFord, shown);
                CollectShown(currentSkinObject, shown);

                List<Renderer> hidden = deadHidden ?? awayHidden;
                if (hidden != null) {
                    for (int i = 0; i < shown.Count; i++) {
                        if (!shown[i] || hidden.Contains(shown[i]))
                            continue;
                        shown[i].enabled = false;
                        hidden.Add(shown[i]);
                    }
                    return;
                }

                if (fade != null && fade.FadingIn) {
                    float from = fade.Alpha;
                    StopFade();
                    fade = RepFade.Start(shown, true, Mathf.Max(0.2f, ArriveFade * (1f - from)), from);
                }
            }
            catch { }
        }

        /// <summary>Going out of sight and not there yet: the nametag stays up, fading.</summary>
        bool FadingOut => fade != null && !fade.FadingIn;

        /// <summary>Whether the nametag shows, with the player's setting for nametags.</summary>
        bool NametagShown => !parked && (!Hidden || FadingOut);

        const float OverlapEnter = 0.35f;   // heads closer than this, side to side
        const float OverlapLeave = 1.1f;
        const float OverlapHeight = 1.8f;
        const float AppearWindow = 1f;      // a rep that just arrived within OverlapLeave counts too

        const float SolidRecheck = 4f;

        bool overlapIgnored;
        int overlapRig;
        float appearedAt = -100f;
        float solidCheckAt;

        void UpdateSpawnOverlap() {
            if (previewOnly || parked || !hasNetTarget || modelColliders == null || modelColliders.Length == 0)
                return;
            if (repTransforms[0] == null || syncedPoints[0] == null)
                return;

            int rig = LocalRigId();
            if (rig == 0)
                return;
            // The local rig was rebuilt (a respawn): the old pairs went with it.
            if (overlapIgnored && rig != overlapRig) {
                overlapIgnored = false;
                graceMine.Clear();
                graceTheirs.Clear();
            }

            Vector3 a = repTransforms[0].position;
            Vector3 b = syncedPoints[0].position;
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            float planar = dx * dx + dz * dz;
            bool level = Mathf.Abs(a.y - b.y) < OverlapHeight;

            if (!overlapIgnored) {
                bool inside = level && planar < OverlapEnter * OverlapEnter;
                bool landed = level && planar < OverlapLeave * OverlapLeave
                    && Time.realtimeSinceStartup - appearedAt < AppearWindow;
                if (inside || landed) {
                    BeginGrace(rig);
                }
                else if (!grabCollisionIgnored && Time.realtimeSinceStartup >= solidCheckAt) {
                    // Whatever else switched us off from each other (a load, a mix-up) doesn't last.
                    solidCheckAt = Time.realtimeSinceStartup + SolidRecheck;
                    PairWithLocal(false, true);
                }
            }
            else if (!level || planar > OverlapLeave * OverlapLeave) {
                EndGrace(rig);
            }
            else if (Time.realtimeSinceStartup >= graceCheckAt) {
                graceCheckAt = Time.realtimeSinceStartup + GraceCheckEvery;
                TickGrace(rig);
            }
        }

        // Arriving inside or right next to someone switched off every pair of colliders between the two
        // until their heads were 1.1m apart, so a player who joined or teleported beside you had no
        // collision at all (hands went through) until one of you walked off. Now only the pairs that
        // actually touch when they arrive are let through each other, and each pair is solid again as soon
        // as its two colliders are clear of each other.
        const float GraceMargin = 0.1f;
        const float GraceCheckEvery = 0.1f;
        readonly List<Collider> graceMine = new List<Collider>();
        readonly List<Collider> graceTheirs = new List<Collider>();
        float graceCheckAt;

        static bool Solid(Collider collider) {
            try { return collider && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy; }
            catch { return false; }
        }

        static bool Touching(Bounds a, Bounds b) {
            a.Expand(GraceMargin * 2f);
            return a.Intersects(b);
        }

        void BeginGrace(int rig) {
            Collider[] local = LocalColliders();
            if (local == null)
                return;

            graceMine.Clear();
            graceTheirs.Clear();
            Bounds[] localBounds = new Bounds[local.Length];
            bool[] localSolid = new bool[local.Length];
            for (int l = 0; l < local.Length; l++) {
                localSolid[l] = Solid(local[l]);
                if (localSolid[l])
                    localBounds[l] = local[l].bounds;
            }

            for (int m = 0; m < modelColliders.Length; m++) {
                Collider mine = modelColliders[m];
                if (!Solid(mine))
                    continue;
                Bounds mineBounds = mine.bounds;
                for (int l = 0; l < local.Length; l++) {
                    if (!localSolid[l] || !Touching(mineBounds, localBounds[l]))
                        continue;
                    try {
                        Physics.IgnoreCollision(mine, local[l], true);
                        graceMine.Add(mine);
                        graceTheirs.Add(local[l]);
                    }
                    catch { }
                }
            }

            overlapRig = rig;
            solidCheckAt = Time.realtimeSinceStartup + SolidRecheck;
            graceCheckAt = Time.realtimeSinceStartup + GraceCheckEvery;
            if (graceMine.Count == 0)
                return;
            overlapIgnored = true;
            EntangleLogger.Debug("[Rep] " + IdText(playerId) + " arrived touching you; " + graceMine.Count
                + " collider pair(s) let through until they are apart, the rest stay solid");
        }

        void TickGrace(int rig) {
            for (int i = graceMine.Count - 1; i >= 0; i--) {
                Collider mine = graceMine[i];
                Collider theirs = graceTheirs[i];
                bool keep;
                try {
                    if (!mine || !theirs)
                        keep = false;
                    // Switched off for now: IgnoreCollision can't take it, look again later.
                    else if (!Solid(mine) || !Solid(theirs))
                        continue;
                    else
                        keep = Touching(mine.bounds, theirs.bounds);
                    if (!keep && mine && theirs)
                        Physics.IgnoreCollision(mine, theirs, false);
                }
                catch { keep = false; }
                if (!keep) {
                    graceMine.RemoveAt(i);
                    graceTheirs.RemoveAt(i);
                }
            }
            if (graceMine.Count == 0)
                EndGrace(rig);
        }

        void EndGrace(int rig) {
            for (int i = 0; i < graceMine.Count; i++) {
                try {
                    if (Solid(graceMine[i]) && Solid(graceTheirs[i]))
                        Physics.IgnoreCollision(graceMine[i], graceTheirs[i], false);
                }
                catch { }
            }
            graceMine.Clear();
            graceTheirs.Clear();
            overlapIgnored = false;
            overlapRig = rig;
            // The 4s check puts back anything still off (a pair that was switched off at the end).
            solidCheckAt = Time.realtimeSinceStartup + SolidRecheck;
            EntangleLogger.Debug("[Rep] " + IdText(playerId) + " is clear of you; solid again");
        }

        static Collider[] LocalColliders() {
            try {
                StressLevelZero.Rig.PhysicsRig physics = PlayerScripts.playerRig ? PlayerScripts.playerRig.physicsRig : null;
                return physics ? (Collider[])physics.GetComponentsInChildren<Collider>() : null;
            }
            catch { return null; }
        }

        /// <summary>We are about to land next to them: counts as them just arriving, so neither is thrown.</summary>
        public void GraceAfterTeleport() => appearedAt = Time.realtimeSinceStartup;

        static int LocalRigId() {
            try { return PlayerScripts.playerRig ? PlayerScripts.playerRig.GetInstanceID() : 0; }
            catch { return 0; }
        }

        bool PairWithLocal(bool ignore, bool solidOnly = false) {
            Collider[] local;
            try {
                StressLevelZero.Rig.PhysicsRig physics = PlayerScripts.playerRig ? PlayerScripts.playerRig.physicsRig : null;
                if (!physics)
                    return false;
                local = physics.GetComponentsInChildren<Collider>();
            }
            catch { return false; }
            if (local == null)
                return false;

            // Physics.IgnoreCollision only takes colliders that are switched on.
            for (int m = 0; m < modelColliders.Length; m++) {
                Collider mine = modelColliders[m];
                try {
                    if (!mine || !mine.enabled || !mine.gameObject.activeInHierarchy)
                        continue;
                }
                catch { continue; }
                for (int l = 0; l < local.Length; l++) {
                    Collider theirs = local[l];
                    try {
                        // Redoing a trigger pair fires its enter/exit again.
                        if (theirs && theirs.enabled && !(solidOnly && (theirs.isTrigger || mine.isTrigger)))
                            Physics.IgnoreCollision(mine, theirs, ignore);
                    }
                    catch { }
                }
            }
            return true;
        }

        // long.ToString and $"..." both hit a missing Il2Cpp string.Format and kill the spawn.
        static string IdText(long value) {
            if (value == 0)
                return "0";
            bool neg = value < 0;
            ulong mag = neg ? (ulong)(-value) : (ulong)value;
            char[] buf = new char[24];
            int i = buf.Length;
            while (mag > 0) {
                buf[--i] = (char)('0' + (int)(mag % 10));
                mag /= 10;
            }
            if (neg)
                buf[--i] = '-';
            return new string(buf, i, buf.Length - i);
        }

        void BuildNametag() {
            wasTalking = false;
            repCanvas = new GameObject("RepCanvas");
            repCanvasComponent = repCanvas.AddComponent<Canvas>();
            repCanvasComponent.renderMode = RenderMode.WorldSpace;
            repCanvasComponent.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            repCanvasTransform = repCanvas.transform;
            repCanvasTransform.localScale = Vector3.one / 200.0f;
            repCanvasGroup = repCanvas.AddComponent<CanvasGroup>();
            repCanvasGroup.interactable = false;
            repCanvasGroup.blocksRaycasts = false;

            GameObject nameGo = new GameObject("Name");
            nameGo.transform.SetParent(repCanvasTransform, false);
            RectTransform nameRect = nameGo.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0.5f, 0.5f);
            nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.sizeDelta = new Vector2(40f, 40f);

            repNameText = nameGo.AddComponent<TextMeshProUGUI>();
            repNameText.alignment = TextAlignmentOptions.Midline;
            repNameText.fontSize = 36f;
            repNameText.text = playerName ?? "";
            Color flairColor = PlayerFlair.GetNameColor(playerId);
            if (flairColor != Color.white)
                SetNameColor(flairColor);

            ContentSizeFitter fitter = nameGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            try { repTalkingIcon = CreateTalkingIcon(nameGo.transform); }
            catch (Exception talk) { EntangleLogger.Warn("Talking icon failed: " + talk.Message); }

            if (PlayerFlair.IsOwner(playerId)) {
                try { repSkull = PlayerFlair.CreateSkull(nameGo.transform); }
                catch (Exception skull) { EntangleLogger.Warn("Skull failed: " + skull.Message); }
            }
            else if (PlayerFlair.IsCube(playerId)) {
                try { repSkull = PlayerFlair.CreateCube(nameGo.transform); }
                catch (Exception cube) { EntangleLogger.Warn("Cube flair failed: " + cube.Message); }
            }
            else if (PlayerFlair.IsTester(playerId)) {
                try { repSkull = PlayerFlair.CreateTester(nameGo.transform); }
                catch (Exception tester) { EntangleLogger.Warn("Tester flair failed: " + tester.Message); }
            }

            PlaceSkull();
        }

        void PlaceSkull() {
            if (!repSkull || !repNameText)
                return;

            float width = repNameText.preferredWidth;
            if (width < 1f) {
                repNameText.ForceMeshUpdate();
                width = repNameText.preferredWidth;
            }
            if (width < 1f)
                width = 140f;

            const float iconSize = 36f;
            repSkull.localScale = Vector3.one;
            repSkull.localRotation = Quaternion.identity;
            repSkull.localPosition = new Vector3(-width * 0.5f - iconSize * 0.55f - 6f, 0f, 0f);
            PlaceTalking();
        }

        void PlaceTalking() {
            if (!repTalkingIcon || !repNameText)
                return;

            float width = repNameText.preferredWidth;
            if (width < 1f)
                width = 140f;

            const float iconSize = 36f;
            float x = -width * 0.5f - iconSize * 0.55f - 6f;
            if (repSkull && repSkull.gameObject.activeSelf)
                x -= iconSize + 4f;

            Transform talk = repTalkingIcon.transform;
            talk.localScale = Vector3.one;
            talk.localRotation = Quaternion.identity;
            talk.localPosition = new Vector3(x, 0f, 0f);
        }

        /// <summary>RigPuppet teardown entry — destroys the physics body (Fusion DestroyPuppet).</summary>
        public void TeardownPhysicsPuppet() => DestroyEnemyTarget();

        void DestroyEnemyTarget() {
            // Out of every NPC's vision first, while the collider still exists to say so.
            if (enemyTargetProxy != null) {
                try { Entanglement.Objects.RepTargetEmulator.ForgetProxy(enemyTargetProxy); }
                catch { }
            }
            enemyHead = null;
            enemyTorso = null;

            if (enemyTargetProxy != null) {
                try { Entanglement.Objects.BossClawSync.UnregisterProxy(enemyTargetProxy); }
                catch { }
                enemyTargetProxy = null;
            }

            if (physicsPuppet != null) {
                try { physicsPuppet.Destroy(); }
                catch { }
                physicsPuppet = null;
            }

            if (enemyTarget) {
                try { GameObject.Destroy(enemyTarget); }
                catch { }
            }

            enemyTarget = null;
            enemyTargetBody = null;
            enemyTargetReady = false;
        }

        static Rigidbody EnsureHandBody(Transform handTarget) {
            if (!handTarget)
                return null;

            try {
                Rigidbody rb = handTarget.GetComponent<Rigidbody>();
                if (!rb)
                    rb = handTarget.gameObject.AddComponent<Rigidbody>();

                // Driven by IK every frame, so it must never simulate or push anything.
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
                return rb;
            }
            catch {
                return null;
            }
        }

        bool IsTestDummy => playerId == TesterRepId || ReferenceEquals(this, debugRepresentation);

        public void EnsureEnemyTarget() {
            if (enemyTargetReady && enemyTarget)
                return;
            if (!repRoot || repTransforms[0] == null)
                return;

            TriggerRefProxy local = PlayerScripts.LocalProxy();
            if (local == null)
                return;

            if (enemyTarget) {
                DestroyEnemyTarget();
                return;
            }

            try {

                var host = new GameObject("PlayerRep." + IdText(playerId));

                host.SetActive(false);
                host.transform.position = repTransforms[0].position;
                host.transform.rotation = Quaternion.Euler(0f, repTransforms[0].eulerAngles.y, 0f);
                int hitLayer = ResolvePlayerHitLayer(local.gameObject.layer);

                // Full physics rig puppet (Fusion RigManager ideology on Boneworks PhysicsRig):
                // pelvis/torso + PD hands — not a lone Ford capsule.
                Vector3 skull = SkullOffset();
                physicsPuppet = new PhysicsRigPuppet();
                physicsPuppet.Build(host, PhysicalRepMass(), skull, HeadRadius, hitLayer);

                Rigidbody body = physicsPuppet.Pelvis;
                Collider[] hits = CollectPuppetColliders(physicsPuppet);
                enemyHead = physicsPuppet.HeadCollider;
                enemyTorso = physicsPuppet.TorsoCollider;
                enemyTargetBody = body;
                targetClearOfRig = 0;

                var proxy = host.AddComponent<TriggerRefProxy>();
                enemyTargetProxy = proxy;
                proxy.triggerType = TriggerRefProxy.TriggerType.Player;
                proxy.npcType = local.npcType;
                proxy.root = repRoot.gameObject;
                proxy.targetHead = body;

                Transform chest = MatchRepBone(local.chestTran);
                Transform feet = MatchRepBone(local.feetTran);
                proxy.chestTran = chest ? chest : repTransforms[0];
                proxy.feetTran = feet ? feet : repRoot;
                EntangleLogger.Debug("[Rep] Proxy for " + IdText(playerId) + ": chest " + BoneName(local.chestTran) + " -> "
                    + BoneName(proxy.chestTran) + ", feet " + BoneName(local.feetTran) + " -> " + BoneName(proxy.feetTran));

                proxy.lfHandRb = physicsPuppet.LeftHand
                    ? physicsPuppet.LeftHand
                    : EnsureHandBody(repTransforms[1]);
                proxy.rtHandRb = physicsPuppet.RightHand
                    ? physicsPuppet.RightHand
                    : EnsureHandBody(repTransforms[2]);

                if (IsTestDummy) {
                    try {
                        proxy.lfHand = local.lfHand;
                        proxy.rtHand = local.rtHand;
                        if (local.lfHandRb) proxy.lfHandRb = local.lfHandRb;
                        if (local.rtHandRb) proxy.rtHandRb = local.rtHandRb;
                    }
                    catch { }
                }


                host.SetActive(true);

                MarkHittable(host);
                for (int i = 0; i < colliders.Length; i++) {
                    if (!colliders[i])
                        continue;
                    for (int h = 0; h < hits.Length; h++)
                        Physics.IgnoreCollision(hits[h], colliders[i], true);
                    MarkHittable(colliders[i].gameObject);
                }

                // Intentionally collide with the local player (shove / melee body contact).
                // Spawn-overlap grace still soft-ignores briefly via SoftenLocalContact.

                var extra = new Collider[colliders.Length + hits.Length];
                Array.Copy(colliders, extra, colliders.Length);
                Array.Copy(hits, 0, extra, colliders.Length, hits.Length);
                colliders = extra;

                enemyTarget = host;
                enemyTargetReady = true;

                if (!IsTestDummy)
                    BossClawSync.RegisterProxy(proxy, playerId);

                RepHitProbe probe = host.AddComponent<RepHitProbe>();
                probe.isTestDummy = IsTestDummy;
            }
            catch (Exception e) {
                enemyTargetReady = false;
                EntangleLogger.Warn("Enemy target failed for " + IdText(playerId) + ": " + e.Message);
            }
        }

        public void DriveEnemyTarget() {
            if (!enemyTargetReady || !enemyTarget || repTransforms[0] == null)
                return;

            try {
                Rigidbody body = enemyTargetBody;
                if (body == null && physicsPuppet != null)
                    body = enemyTargetBody = physicsPuppet.Pelvis;
                if (body == null) {
                    body = enemyTarget.GetComponent<Rigidbody>();
                    enemyTargetBody = body;
                }
                if (body == null)
                    return;

                SoftenLocalContact();

                bool parked = deadHidden != null;
                if (parked) {
                    if ((body.position - ParkSpot).sqrMagnitude > 1f)
                        body.position = ParkSpot;
                    hasLastHead = false;
                }

                Vector3 target = parked ? ParkSpot : repTransforms[0].position;
                Quaternion rot = Quaternion.Euler(0f, repTransforms[0].eulerAngles.y, 0f);

                Vector3 headVel;
                if (!hasLastHead)
                    headVel = repSavedVel;
                else if (solvePerFrame && target == lastHeadTarget)
                    headVel = lastHeadVel;
                else
                    headVel = (target - lastHeadTarget) / Mathf.Max(Time.fixedDeltaTime, Time.fixedTime - lastHeadAt);
                if (!hasLastHead || target != lastHeadTarget)
                    lastHeadAt = Time.fixedTime;
                lastHeadVel = headVel;
                lastHeadTarget = target;
                hasLastHead = true;

                Vector3 leftHand = repTransforms[1] ? repTransforms[1].position : target + Vector3.left * 0.3f;
                Vector3 rightHand = repTransforms[2] ? repTransforms[2].position : target + Vector3.right * 0.3f;
                Vector3 chest = target + rot * new Vector3(0f, -0.35f, 0.05f);
                // Feet from visual Ford when present; otherwise under the pelvis.
                Vector3 leftFoot = target + new Vector3(-0.12f, -1.55f, 0f);
                Vector3 rightFoot = target + new Vector3(0.12f, -1.55f, 0f);
                try {
                    if (repRoot) {
                        Transform localChest = null;
                        try {
                            TriggerRefProxy local = PlayerScripts.LocalProxy();
                            if (local != null)
                                localChest = local.chestTran;
                        }
                        catch { }
                        Transform chestBone = MatchRepBone(localChest);
                        if (!chestBone) chestBone = FindDeep(repRoot, "Chest");
                        if (!chestBone) chestBone = FindDeep(repRoot, "Spine");
                        if (chestBone) chest = chestBone.position;

                        Transform lNamed = FindDeep(repRoot, "Foot (left)");
                        Transform rNamed = FindDeep(repRoot, "Foot (right)");
                        if (!lNamed) lNamed = FindDeep(repRoot, "heel.L");
                        if (!rNamed) rNamed = FindDeep(repRoot, "heel.R");
                        if (lNamed) leftFoot = lNamed.position;
                        if (rNamed) rightFoot = rNamed.position;
                        else {
                            Transform lf = FindDeep(repRoot, "Foot");
                            if (lf) {
                                leftFoot = lf.position;
                                rightFoot = lf.position + Vector3.right * 0.2f;
                            }
                        }
                    }
                }
                catch { }

                if (physicsPuppet == null || !physicsPuppet.IsValid) {
                    EntangleLogger.Warn("[Rep] PhysicsRigPuppet missing for " + IdText(playerId) + " — rebuilding");
                    enemyTargetReady = false;
                    EnsureEnemyTarget();
                    return;
                }

                var pose = new PhysicsRigPuppet.PoseTargets {
                    pelvis = target,
                    pelvisRot = rot,
                    pelvisVel = headVel,
                    chest = chest,
                    leftHand = leftHand,
                    rightHand = rightHand,
                    leftFoot = leftFoot,
                    rightFoot = rightFoot,
                };
                physicsPuppet.Drive(pose, pressingLocal, parked);
                if (!parked && repRoot)
                    physicsPuppet.FollowVisual(repRoot, 0.4f);
            }
            catch { }
        }

        static Collider[] CollectPuppetColliders(PhysicsRigPuppet puppet) {
            if (puppet == null)
                return Array.Empty<Collider>();
            var list = new System.Collections.Generic.List<Collider>(8);
            void Add(Collider c) { if (c) list.Add(c); }
            Add(puppet.HeadCollider);
            Add(puppet.TorsoCollider);
            if (puppet.Chest) Add(puppet.Chest.GetComponent<Collider>());
            if (puppet.LeftHand) Add(puppet.LeftHand.GetComponent<Collider>());
            if (puppet.RightHand) Add(puppet.RightHand.GetComponent<Collider>());
            if (puppet.LeftFoot) Add(puppet.LeftFoot.GetComponent<Collider>());
            if (puppet.RightFoot) Add(puppet.RightFoot.GetComponent<Collider>());
            return list.ToArray();
        }

        PhysicsRigPuppet physicsPuppet;
        Rigidbody enemyTargetBody;
        Collider enemyHead;
        Collider enemyTorso;
        int targetClearOfRig;
        bool localContactIgnored;
        bool pressingLocal;
        float contactCheckAt;
        readonly Entanglement.Data.PDController pelvisPD = new Entanglement.Data.PDController();

        const float FreeAccelCap = 120f;
        const float ContactAccelCap = 36f;
        const float RepSpeedLimit = 12f;
        const float ContactCheckEvery = 0.05f;

        /// <summary>
        /// Fusion-style: remotes collide with you. Only ignore during spawn-overlap grace so you don't
        /// launch each other when joining on top of someone.
        /// </summary>
        void SoftenLocalContact() {
            bool wantIgnore = overlapIgnored || (Time.time < heldReleaseUntil);
            int rig = LocalRigId();
            if (rig == 0)
                return;

            if (rig == targetClearOfRig && wantIgnore == localContactIgnored && Time.realtimeSinceStartup < contactCheckAt)
                return;
            targetClearOfRig = rig;
            contactCheckAt = Time.realtimeSinceStartup + ContactCheckEvery;

            Collider[] local;
            try {
                StressLevelZero.Rig.PhysicsRig physics = PlayerScripts.playerRig.physicsRig;
                if (!physics)
                    return;
                local = physics.GetComponentsInChildren<Collider>();
            }
            catch { return; }
            if (local == null)
                return;

            Collider[] target = CollectPuppetColliders(physicsPuppet);
            if (target.Length == 0)
                target = new Collider[] { enemyHead, enemyTorso };
            for (int t = 0; t < target.Length; t++) {
                Collider mine = target[t];
                if (!mine)
                    continue;
                for (int l = 0; l < local.Length; l++) {
                    Collider theirs = local[l];
                    try {
                        if (theirs && theirs.enabled && !theirs.isTrigger)
                            Physics.IgnoreCollision(mine, theirs, wantIgnore);
                    }
                    catch { }
                }
            }

            if (wantIgnore != localContactIgnored) {
                localContactIgnored = wantIgnore;
                EntangleLogger.Debug("[Rep] " + IdText(playerId) + ": local contact "
                    + (wantIgnore ? "soft (spawn grace)" : "solid (shove/melee on)"));
            }

            // Cheap proximity test for accel softening without allocating contact arrays every step.
            pressingLocal = false;
            if (!wantIgnore && syncedRoot) {
                Vector3 delta = enemyTarget.transform.position - syncedRoot.position;
                delta.y *= 0.35f;
                pressingLocal = delta.sqrMagnitude < 1.1f * 1.1f;
            }
        }
        Vector3 lastHeadTarget;
        Vector3 lastHeadVel;
        float lastHeadAt;
        bool hasLastHead;

        static Vector3 skullOffset;
        static bool skullMeasured;
        static readonly Vector3 DefaultSkull = new Vector3(0f, -0.05f, -0.09f);

        const float HeadRadius = 0.11f;

        const float SkullBehindMin = 0.06f;
        const float SkullBehindMax = 0.14f;

        static Vector3 SkullOffset() {
            if (skullMeasured)
                return skullOffset;
            try {
                Transform eyes = syncedPoints[0];
                Transform physHead = PlayerScripts.playerRig ? PlayerScripts.playerRig.transform.Find("[PhysicsRig]/Head") : null;
                if (eyes && physHead) {
                    Vector3 centre = physHead.position;
                    SphereCollider sphere = physHead.GetComponent<SphereCollider>();
                    if (sphere)
                        centre = physHead.TransformPoint(sphere.center);
                    Quaternion heading = Quaternion.Euler(0f, eyes.eulerAngles.y, 0f);
                    Vector3 offset = Quaternion.Inverse(heading) * (centre - eyes.position);
                    // A rig caught mid-teleport or not yet settled gives nonsense; keep the default.
                    if (offset.sqrMagnitude <= 0.25f * 0.25f) {
                        // Side to side it is centred; up and down and behind are kept to a head.
                        offset.x = 0f;
                        offset.y = Mathf.Clamp(offset.y, -0.10f, 0.03f);
                        offset.z = Mathf.Clamp(offset.z, -SkullBehindMax, -SkullBehindMin);
                        skullOffset = offset;
                        skullMeasured = true;
                        EntangleLogger.Log("[Rep] Head hitbox offset from the eyes: " + offset.ToString("F3"));
                        return skullOffset;
                    }
                }
            }
            catch { }
            return DefaultSkull;
        }

        /// <summary>The body shapes an NPC's vision sphere can overlap, or null before they exist.</summary>
        public Collider EnemyHead => enemyTargetReady && enemyTarget ? enemyHead : null;
        public Collider EnemyTorso => enemyTargetReady && enemyTarget ? enemyTorso : null;

        /// <summary>True for any collider that is part of this rep: its model or its hit body.</summary>
        public bool OwnsCollider(Collider col) {
            if (!col)
                return false;
            try {
                if (enemyTargetBody && col.attachedRigidbody == enemyTargetBody)
                    return true;
                if (repRoot && col.transform.IsChildOf(repRoot))
                    return true;
                for (int i = 0; i < colliders.Length; i++) {
                    if (colliders[i] == col)
                        return true;
                }
            }
            catch { }
            return false;
        }

        static string BoneName(Transform t) {
            try { return t ? t.name : "none"; }
            catch { return "?"; }
        }

        // The bone on this rep with the same name as one on the local rig.
        Transform MatchRepBone(Transform local) {
            if (!local || !repRoot)
                return null;
            string name;
            try { name = local.name; }
            catch { return null; }
            return FindDeep(repRoot, name);
        }

        static Transform FindDeep(Transform parent, string name) {
            if (parent.name == name)
                return parent;
            for (int i = 0; i < parent.childCount; i++) {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found)
                    return found;
            }
            return null;
        }

        const float RepMaxDownAccel = 19.6f;   // twice gravity: enough to follow a crouch or a fall
        const float RepMassMin = 28f;
        const float RepMassMax = 55f;
        const float RepMassShare = 0.4f;       // fraction of full local rig mass — pelvis-like, not bowling ball

        // Full local rig mass (all phys bodies). Used as a reference only.
        static float LocalPlayerMass() {
            float mass = 0f;
            try {
                if (PlayerScripts.playerPhysBody) {
                    Rigidbody[] bodies = PlayerScripts.playerPhysBody.GetComponentsInChildren<Rigidbody>(true);
                    for (int i = 0; i < bodies.Length; i++) {
                        if (bodies[i] && !bodies[i].isKinematic)
                            mass += bodies[i].mass;
                    }
                }
            }
            catch { }

            if (mass <= 1f) {
                EntangleLogger.Warn("[Rep] Could not read the local player's weight, using 75kg for the rep.");
                return 75f;
            }
            return mass;
        }

        /// <summary>Mass for the force-driven remote body. Clamped so shove works without ragdoll launches.</summary>
        static float PhysicalRepMass() {
            return Mathf.Clamp(LocalPlayerMass() * RepMassShare, RepMassMin, RepMassMax);
        }

        void CentreHeadColliders() {
            Transform headPoint = repTransforms[0];
            if (!headPoint)
                return;
            Vector3 skull = SkullOffset();
            Vector3 scale = headPoint.lossyScale;
            Vector3 local = new Vector3(
                Mathf.Abs(scale.x) > 0.0001f ? skull.x / scale.x : 0f,
                Mathf.Abs(scale.y) > 0.0001f ? skull.y / scale.y : 0f,
                Mathf.Abs(scale.z) > 0.0001f ? skull.z / scale.z : 0f);

            Collider[] own;
            try { own = headPoint.GetComponents<Collider>(); }
            catch { return; }
            for (int i = 0; i < own.Length; i++) {
                Collider col = own[i];
                try {
                    if (!col || col.isTrigger)
                        continue;
                    SphereCollider sphere = col.TryCast<SphereCollider>();
                    if (sphere != null && sphere.center.sqrMagnitude < 0.0004f) {
                        sphere.center = local;
                        continue;
                    }
                    CapsuleCollider capsule = col.TryCast<CapsuleCollider>();
                    if (capsule != null && capsule.center.sqrMagnitude < 0.0004f) {
                        capsule.center = local;
                        continue;
                    }
                    BoxCollider box = col.TryCast<BoxCollider>();
                    if (box != null && box.center.sqrMagnitude < 0.0004f)
                        box.center = local;
                }
                catch { }
            }
        }

        void NoSpeculativeContacts() {
            try {
                Rigidbody[] bodies = repRoot.GetComponentsInChildren<Rigidbody>(true);
                int changed = 0;
                for (int i = 0; i < bodies.Length; i++) {
                    Rigidbody rb = bodies[i];
                    if (!rb || rb.collisionDetectionMode == CollisionDetectionMode.Discrete)
                        continue;
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    changed++;
                }
                if (changed > 0)
                    EntangleLogger.Debug("[Rep] " + changed + " model bodies switched to discrete contacts");
            }
            catch { }
        }

        /// <summary>
        /// Visual/IK model must not act as kinematic physics (doors/lifts break). Physical presence
        /// is the PD-driven hit body only. Grips still work via Interaction components.
        /// </summary>
        void SilenceKinematicModelPhysics() {
            try {
                Rigidbody[] bodies = repRoot.GetComponentsInChildren<Rigidbody>(true);
                int silenced = 0;
                for (int i = 0; i < bodies.Length; i++) {
                    Rigidbody rb = bodies[i];
                    if (!rb || !rb.isKinematic)
                        continue;
                    if (enemyTarget && rb.transform.IsChildOf(enemyTarget.transform))
                        continue;
                    rb.detectCollisions = false;
                    silenced++;
                }
                if (silenced > 0)
                    EntangleLogger.Debug("[Rep] " + IdText(playerId) + ": silenced " + silenced
                        + " kinematic model bodies (PD hit body owns physics)");
            }
            catch { }
        }

        static bool describedOnce;

        void DescribeModelColliders() {
            try {
                int none = 0, kinematic = 0, dynamic = 0, triggers = 0, noContact = 0;
                StringBuilder sample = new StringBuilder();
                for (int i = 0; i < colliders.Length; i++) {
                    Collider col = colliders[i];
                    if (!col)
                        continue;
                    Rigidbody rb = col.attachedRigidbody;
                    if (col.isTrigger) triggers++;
                    if (rb == null) none++;
                    else if (!rb.detectCollisions) noContact++;
                    else if (rb.isKinematic) kinematic++;
                    else dynamic++;
                    if (i < 12)
                        sample.Append(col.name).Append('(').Append(col.GetType().Name).Append(" layer ").Append(col.gameObject.layer)
                            .Append(rb == null ? " no body" : rb.isKinematic ? " kinematic" : " dynamic").Append(") ");
                }
                string line = "[Rep] Model colliders for " + IdText(playerId) + ": " + colliders.Length
                    + " total, no body " + none + ", kinematic " + kinematic + ", dynamic " + dynamic
                    + ", no contact " + noContact + ", triggers " + triggers + ". " + sample;
                // Once per session in the normal log, so a hitbox problem can be read off any log.
                if (!describedOnce) {
                    describedOnce = true;
                    EntangleLogger.Log(line);
                }
                else
                    EntangleLogger.Debug(line);
            }
            catch (Exception e) { EntangleLogger.Warn("[Rep] Could not describe model colliders: " + e.Message); }
        }

        // The layer the local player actually gets hit on.
        static int ResolvePlayerHitLayer(int fallback) {
            try {
                if (PlayerScripts.playerPhysBody) {
                    Collider[] cols = PlayerScripts.playerPhysBody.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < cols.Length; i++) {
                        if (cols[i] && !cols[i].isTrigger)
                            return cols[i].gameObject.layer;
                    }
                }
            }
            catch { }

            return fallback;
        }

        static void MarkHittable(GameObject go) {
            if (!go)
                return;
            ImpactProperties impact = go.GetComponent<ImpactProperties>();
            if (!impact)
                impact = go.AddComponent<ImpactProperties>();
            impact.FireResistance = 1f;
        }

        /// <summary>Drops this player's body where the rep stands. False if there is none.</summary>
        public bool CreateRagdoll() {
            // Nothing said why a death left no body, so every early out is logged now.
            if (!activeAnimator) {
                EntangleLogger.Warn("[Death] No ragdoll for " + IdText(playerId) + ": the rep has no animator");
                return false;
            }
            if (!ragdollBody) {
                EntangleLogger.Warn("[Death] No ragdoll for " + IdText(playerId) + ": the rep has no ragdoll template");
                return false;
            }

            return BuildRagdoll(ragdollBody, repBody, playerId, repSavedVel, colliders) != null;
        }

        static SLZ_Body localRagdollTemplate;

        public static void CreateLocalRagdoll() {
            try {
                SLZ_Body pose = null;
                try { pose = PlayerScripts.playerRig.gameWorldSkeletonRig.body; } catch { }
                if (!pose) {
                    EntangleLogger.Warn("[Death] No ragdoll for you: could not read your body pose");
                    return;
                }

                if (!localRagdollTemplate) {
                    EnsureBundle();
                    GameObject prefab = playerRepBundle ? playerRepBundle.LoadAsset<GameObject>("PlayerRep") : null;
                    Transform template = prefab ? prefab.transform.Find("Ragdoll") : null;
                    localRagdollTemplate = template ? template.GetComponent<SLZ_Body>() : null;
                }
                if (!localRagdollTemplate) {
                    EntangleLogger.Warn("[Death] No ragdoll for you: the ragdoll template is missing from the bundle");
                    return;
                }

                Collider[] mine = PlayerScripts.playerPhysBody
                    ? PlayerScripts.playerPhysBody.GetComponentsInChildren<Collider>(true)
                    : null;

                BuildRagdoll(localRagdollTemplate, pose, SteamIntegration.currentUserId, Vector3.zero, mine);
            }
            catch (Exception e) { EntangleLogger.Warn("[Death] Could not make your ragdoll: " + e.Message); }
        }

        static GameObject BuildRagdoll(SLZ_Body template, SLZ_Body pose, long ownerId, Vector3 velocity, Collider[] keepClearOf) {
            GameObject ragdollRoot = new GameObject("Ragdoll." + IdText(ownerId));

            GameObject newRagdoll = GameObject.Instantiate(template.gameObject);
            newRagdoll.transform.parent = ragdollRoot.transform;

            Collider[] ragdollCols = newRagdoll.GetComponentsInChildren<Collider>(true);

            int templateLayer = ragdollCols.Length > 0 && ragdollCols[0] ? ragdollCols[0].gameObject.layer : newRagdoll.layer;
            if (Physics.GetIgnoreLayerCollision(templateLayer, 0)) {
                int worldLayer = ResolvePlayerHitLayer(0);
                EntangleLogger.Warn("[Death] Ragdoll layer " + templateLayer + " ignores the level, moving it to " + worldLayer);
                foreach (Collider col in ragdollCols) {
                    if (col)
                        col.gameObject.layer = worldLayer;
                }
            }

            newRagdoll.gameObject.SetActive(true);

            foreach (Collider col1 in ragdollCols)
            {
                foreach (Collider col2 in ragdollCols)
                {
                    if (col1 == col2)
                        continue;

                    Physics.IgnoreCollision(col1, col2, true);
                }
            }

            if (keepClearOf != null) {
                foreach (Collider col in ragdollCols) {
                    if (!col)
                        continue;
                    for (int i = 0; i < keepClearOf.Length; i++) {
                        if (keepClearOf[i])
                            Physics.IgnoreCollision(col, keepClearOf[i], true);
                    }
                }
            }

            EntangleLogger.Debug("[Death] Ragdoll created for " + IdText(ownerId));

            foreach (Rigidbody rb in newRagdoll.GetComponentsInChildren<Rigidbody>(true)) {
                rb.velocity = velocity;
                rb.angularVelocity = Vector3.zero;
            }

            CopyBone(pose.transform, newRagdoll.transform);
            CopyBones(pose.references, newRagdoll.GetComponent<SLZ_Body>().references);

            newRagdoll.gameObject.AddComponent<RagdollBehaviour>();

            if (Node.isServer && SteamIntegration.hasLobby)
                MelonCoroutines.Start(SyncRagdollBones(newRagdoll));

            return ragdollRoot;
        }

        static IEnumerator SyncRagdollBones(GameObject ragdoll) {
            yield return new WaitForSeconds(0.5f);

            if (!ragdoll || !SteamIntegration.hasLobby || !Node.isServer)
                yield break;

            Rigidbody[] bones;
            try { bones = ragdoll.GetComponentsInChildren<Rigidbody>(true); }
            catch { yield break; }

            if (bones == null)
                yield break;

            foreach (Rigidbody rb in bones) {
                bool skip = false;
                try {
                    if (!rb || rb.isKinematic)
                        skip = true;
                    else if (TransformSyncable.cache.Get(rb.gameObject))
                        skip = true;
                }
                catch { skip = true; }

                if (skip)
                    continue;

                SyncUtilities.UpdateBodyAttached(rb, null, -1, -1f);
                SyncUtilities.UpdateBodyDetached(rb);
            }
        }

        public static void CopyBones(SLZ_Body.References from, SLZ_Body.References to) {
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

        public static void CopyBone(Transform from, Transform to) {
            if (!from || !to)
                return;
            to.position = from.position;
            to.rotation = from.rotation;
        }

        // Feeds the rep new network data, velocities are estimated from consecutive packets for dead reckoning
        public void SetNetTargets(Vector3 rootPosition, Vector3[] positions, Quaternion[] rotations) {
            float now = Time.time;
            lastPacketReal = Time.realtimeSinceStartup;

            // They are loading into another level: this is them in the old one, on the way out.
            if (awayForLoad)
                return;

            if (parked) {
                parked = false;
                appearedAt = Time.realtimeSinceStartup;
                FadeIn();
            }
            else if (awayHidden != null) {
                // Went quiet long enough to start fading, then spoke again: back from where it got to.
                FadeIn();
            }

            if (hasNetTarget && (rootPosition - netRootPosition).sqrMagnitude > repSnapDistance * repSnapDistance) {
                hasNetTarget = false;
                appearedAt = Time.realtimeSinceStartup;
            }

            if (hasNetTarget) {
                float packetDelta = Mathf.Clamp(now - netReceiveTime, 0.008f, 0.5f);

                Vector3 delta = rootPosition - netRootPosition;
                Vector3 rawVel = delta / packetDelta;

                bool suppressVertical = heldByLocalPlayer || now < heldReleaseUntil;

                netRootVelocity = Vector3.ClampMagnitude(rawVel, repMaxPredictedSpeed);

                float lateral = new Vector2(delta.x, delta.z).magnitude;
                bool looksLikeCrouch = Mathf.Abs(delta.y) > crouchVerticalDelta && lateral < crouchLateralMax;
                if ((isGrounded || suppressVertical) && looksLikeCrouch)
                    rawVel.y = 0f;

                if (suppressVertical) {
                    netRootVelocity = Vector3.zero;
                    for (int r = 0; r < netLimbVelocities.Length; r++)
                        netLimbVelocities[r] = Vector3.zero;
                }
                else {
                    rawVel.y = 0f;
                    netRootVelocity = Vector3.ClampMagnitude(rawVel, repMaxPredictedSpeed);

                    for (int r = 0; r < netPositions.Length; r++) {
                        Vector3 limbVel = (positions[r] - netPositions[r]) / packetDelta;
                        limbVel.y = 0f;
                        netLimbVelocities[r] = Vector3.ClampMagnitude(limbVel, repMaxPredictedSpeed);
                    }
                }
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

        // Glides the rep toward the latest network state, run once per tick.
        // Fusion ideology: root follows the PD physics body when present; limbs still track net pose.
        public void ApplyNetSmoothing(float dt) {
            if (!hasNetTarget || !repRoot) return;

            float age = Mathf.Min(Time.time - netReceiveTime, repExtrapolationLimit);
            Vector3 predictedRoot = netRootPosition + netRootVelocity * age;

            // Physical lead: visual root tracks the PD hit body (not pure transform teleport).
            if (enemyTargetBody && !parked && deadHidden == null) {
                Vector3 phys = enemyTargetBody.position;
                // Head proxy sits above root; map back toward feet/root.
                Vector3 physRoot = phys;
                physRoot.y -= 1.6f;
                predictedRoot = Vector3.Lerp(predictedRoot, physRoot, 0.65f);
            }

            float ride = RideOffset(predictedRoot, dt);
            predictedRoot.y += ride;
            Vector3 rideLift = new Vector3(0f, ride, 0f);

            // Remote networked grab: soft-follow grabber hand (Fusion grab ideology).
            if (remoteGrabberId != 0 && TryRemoteGrabTarget(out Vector3 grabTarget)) {
                predictedRoot = Vector3.Lerp(predictedRoot, grabTarget, 0.55f);
                if (enemyTargetBody)
                    enemyTargetBody.MovePosition(Vector3.Lerp(enemyTargetBody.position, grabTarget + Vector3.up * 1.6f, 0.35f));
            }

            // Local grab: clamp steps so network pose doesn't rip the joint apart.
            if (heldByLocalPlayer) {
                ApplyHeldStep(repRoot, predictedRoot, true);
                for (int r = 0; r < repTransforms.Length; r++) {
                    if (!repTransforms[r]) continue;
                    ApplyHeldStep(repTransforms[r], netPositions[r] + netLimbVelocities[r] * age + rideLift, r == 0);
                    repTransforms[r].rotation = Quaternion.Slerp(repTransforms[r].rotation, netRotations[r], 1f - Mathf.Exp(-repLimbSharpness * dt));
                }
                UpdateNametagPosition();
                UpdateTalkingIndicator();
                return;
            }

            float t = 1f - Mathf.Exp(-repFollowSharpness * dt);
            float limbT = 1f - Mathf.Exp(-repLimbSharpness * dt);
            if ((repRoot.position - predictedRoot).sqrMagnitude > repSnapDistance * repSnapDistance)
                t = limbT = 1f;

            repRoot.position = Vector3.Lerp(repRoot.position, predictedRoot, t);

            for (int r = 0; r < repTransforms.Length; r++) {
                if (!repTransforms[r]) continue;

                repTransforms[r].position = Vector3.Lerp(repTransforms[r].position, netPositions[r] + netLimbVelocities[r] * age + rideLift, limbT);
                repTransforms[r].rotation = Quaternion.Slerp(repTransforms[r].rotation, netRotations[r], limbT);
            }

            UpdateNametagPosition();
            UpdateTalkingIndicator();
        }

        long remoteGrabberId;
        byte remoteGrabHand;
        byte remoteGrabAttach;
        static long localGrabberId;
        static byte localGrabHand;

        public void ApplyRemoteGrab(long grabberId, byte hand, byte attach) {
            remoteGrabberId = grabberId;
            remoteGrabHand = hand;
            remoteGrabAttach = attach;
        }

        public void ClearRemoteGrab(long grabberId, byte hand) {
            if (remoteGrabberId == grabberId && (hand == 255 || hand == remoteGrabHand))
                remoteGrabberId = 0;
        }

        public static void ApplyRemoteGrabOnLocal(long grabberId, byte hand, byte attach) {
            localGrabberId = grabberId;
            localGrabHand = hand;
            // Soft pull the local phys body toward the grabber's hand each FixedUpdate via DriveRepPhysics.
        }

        public static void ClearRemoteGrabOnLocal(long grabberId, byte hand) {
            if (localGrabberId == grabberId && (hand == 255 || hand == localGrabHand))
                localGrabberId = 0;
        }

        bool TryRemoteGrabTarget(out Vector3 target) {
            target = default(Vector3);
            if (remoteGrabberId == 0)
                return false;
            if (!representations.TryGetValue(remoteGrabberId, out PlayerRepresentation grabber) || grabber == null)
                return false;
            int limb = remoteGrabHand == 0 ? 1 : 2; // left/right hand transforms
            if (grabber.repTransforms == null || limb >= grabber.repTransforms.Length || !grabber.repTransforms[limb])
                return false;
            target = grabber.repTransforms[limb].position;
            return true;
        }

        static void TickLocalGrabPull() {
            if (localGrabberId == 0 || !PlayerScripts.playerPhysBody)
                return;
            if (!representations.TryGetValue(localGrabberId, out PlayerRepresentation grabber) || grabber == null)
                return;
            int limb = localGrabHand == 0 ? 1 : 2;
            if (grabber.repTransforms == null || limb >= grabber.repTransforms.Length || !grabber.repTransforms[limb])
                return;
            try {
                Vector3 to = grabber.repTransforms[limb].position - PlayerScripts.playerPhysBody.transform.position;
                if (to.sqrMagnitude > 0.01f)
                    PlayerScripts.playerPhysBody.AddImpulseForce(Vector3.ClampMagnitude(to, 2.5f) * 8f);
            }
            catch { }
        }

        const float RideProbeUp = 0.5f;
        const float RideTolerance = 0.4f;
        const float RideEase = 2.5f;            // metres per second the correction may change by
        static readonly Il2CppStructArray<RaycastHit> rideHits = new Il2CppStructArray<RaycastHit>(16);

        float rideOffset;
        float standHeight;

        float RideOffset(Vector3 feet, float dt) {
            float want = 0f;
            if (isGrounded && !heldByLocalPlayer && deadHidden == null) {
                try { want = PlatformGap(feet); }
                catch { want = 0f; }
            }
            rideOffset = Mathf.MoveTowards(rideOffset, want, RideEase * dt);
            return rideOffset;
        }

        float PlatformGap(Vector3 feet) {
            Vector3 origin = feet + Vector3.up * RideProbeUp;
            // Every layer but Ignore Raycast (2).
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, rideHits, RideProbeUp + RideTolerance,
                ~(1 << 2), QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            Collider floor = null;
            for (int i = 0; i < count; i++) {
                RaycastHit hit = rideHits[i];
                Collider col = hit.collider;
                if (!col || hit.distance >= nearest)
                    continue;
                Rigidbody body = col.attachedRigidbody;
                if (body && (RepCollisionGuard.IsRepOwned(body) || RepCollisionGuard.IsLocalPlayer(body)))
                    continue;
                if (repRoot && col.transform.IsChildOf(repRoot))
                    continue;
                nearest = hit.distance;
                floor = col;
            }
            if (!floor)
                return 0f;

            float surface = origin.y - nearest;
            Rigidbody under = floor.attachedRigidbody;
            if (!under) {
                // Plain floor: both ends agree where it is, so learn how high their root stands.
                float stand = feet.y - surface;
                if (Mathf.Abs(stand) <= RideTolerance)
                    standHeight = Mathf.Lerp(standHeight, stand, 0.05f);
                return 0f;
            }
            if (!RepCollisionGuard.IsPlatform(under) && under.velocity.sqrMagnitude < 0.04f)
                return 0f;

            float gap = surface + standHeight - feet.y;
            return Mathf.Abs(gap) <= RideTolerance ? gap : 0f;
        }

        void ApplyHeldStep(Transform target, Vector3 desired, bool clampVerticalHard) {
            Vector3 delta = desired - target.position;
            Vector3 planar = new Vector3(delta.x, 0f, delta.z);
            float maxPlanar = heldMaxPlanarStep;
            float maxY = clampVerticalHard ? heldMaxVerticalStep : heldMaxPlanarStep;

            if (planar.sqrMagnitude > maxPlanar * maxPlanar)
                planar = planar.normalized * maxPlanar;

            float y = Mathf.Clamp(delta.y, -maxY, maxY);
            target.position = target.position + planar + Vector3.up * y;
        }

        void ZeroRepRigidbodyVelocities() {
            if (!repFord) return;
            Rigidbody[] bodies = repFord.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++) {
                Rigidbody body = bodies[i];
                if (!body || body.isKinematic) continue;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        void DampLocalHoldVertical() {
            if (!PlayerScripts.playerPhysBody) return;
            Rigidbody[] localBodies = PlayerScripts.playerPhysBody.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < localBodies.Length; i++) {
                Rigidbody body = localBodies[i];
                if (!body || body.isKinematic) continue;
                Vector3 v = body.velocity;
                if (Mathf.Abs(v.y) > localHoldVerticalDamp)
                    v.y = Mathf.Sign(v.y) * localHoldVerticalDamp;
                body.velocity = v;
            }
        }

        void UpdateLocalGrabState() {
            bool held = IsHeldByLocalHand(PlayerScripts.playerLeftHand) || IsHeldByLocalHand(PlayerScripts.playerRightHand);
            if (held == heldByLocalPlayer && grabCollisionIgnored == held)
                return;

            if (heldByLocalPlayer && !held)
                heldReleaseUntil = Time.time + heldReleaseCooldown;

            heldByLocalPlayer = held;
            SetGrabCollisionIgnored(held);
        }

        bool IsHeldByLocalHand(Hand hand) {
            if (!hand || !hand.m_CurrentAttachedObject || !repFord)
                return false;
            Transform root = hand.m_CurrentAttachedObject.transform.root;
            return root && root.name == "PlayerRep." + IdText(playerId);
        }

        void SetGrabCollisionIgnored(bool ignore) {
            grabCollisionIgnored = ignore;
            if (!PlayerScripts.playerPhysBody)
                return;

            // The body only: the NPC target body stays clear of us either way.
            Collider[] local = PlayerScripts.playerPhysBody.GetComponentsInChildren<Collider>(true);
            for (int m = 0; m < modelColliders.Length; m++) {
                Collider mine = modelColliders[m];
                if (!mine)
                    continue;
                for (int l = 0; l < local.Length; l++) {
                    try {
                        if (local[l])
                            Physics.IgnoreCollision(mine, local[l], ignore);
                    }
                    catch { }
                }
            }
        }

        void DisableColliders() {
            if (colliders == null)
                return;
            for (int i = 0; i < colliders.Length; i++) {
                if (colliders[i])
                    colliders[i].enabled = false;
            }
        }

        public void UpdateNametagPosition() {
            if (!repCanvasTransform || repTransforms[0] == null)
                return;

            repCanvasTransform.position = repTransforms[0].position + Vector3.up * 0.4f;

            Transform view = Headset();
            if (view)
                repCanvasTransform.rotation = Quaternion.LookRotation(repCanvasTransform.position - view.position, Vector3.up);
            if (repCanvasComponent && cachedEye)
                repCanvasComponent.worldCamera = cachedEye;

            PlaceSkull();
            PlaceTalking();
        }

        static Camera cachedEye;

        static Transform Headset() {
            if (Camera.current && Camera.current.enabled)
                return Camera.current.transform;
            if (cachedEye)
                return cachedEye.transform;
            GetPlayerTransforms();
            if (syncedRoot) {
                Camera eye = syncedRoot.GetComponentInChildren<Camera>();
                if (eye) {
                    cachedEye = eye;
                    return eye.transform;
                }
            }
            if (syncedPoints[0])
                return syncedPoints[0];
            if (Camera.main)
                return Camera.main.transform;
            return null;
        }

        bool wasTalking;
        Color baseNameColor = Color.white;

        public void SetNameColor(Color color) {
            baseNameColor = color;
            if (!wasTalking && repNameText)
                repNameText.color = baseNameColor;
        }

        static RawImage CreateTalkingIcon(Transform parent) {
            if (!parent)
                return null;
            Texture2D tex = TalkTexture();
            if (tex == null)
                return null;

            var go = new GameObject("TalkingIcon");
            go.transform.SetParent(parent, false);
            RawImage image = go.AddComponent<RawImage>();
            image.texture = tex;
            image.color = Color.white;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(36f, 36f);
            go.SetActive(false);
            return image;
        }

        static Texture2D talkTex;

        static Texture2D TalkTexture() {
            if (talkTex != null)
                return talkTex;

            byte[] bytes = PlayerFlair.ReadEmbedded("Entanglement.resources.microphone.rgba");
            Texture2D tex = IconFromRgba(bytes);
            if (tex == null)
                return null;
            tex.filterMode = FilterMode.Bilinear;
            talkTex = tex;
            return talkTex;
        }

        static Texture2D IconFromRgba(byte[] bytes) {
            if (bytes == null || bytes.Length < 12)
                return null;

            int width = System.BitConverter.ToInt32(bytes, 0);
            int height = System.BitConverter.ToInt32(bytes, 4);
            if (width < 1 || height < 1 || width > 256 || height > 256 || bytes.Length < 8 + width * height * 4)
                return null;

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int o = 8 + (y * width + x) * 4;
                    tex.SetPixel(x, y, new Color(bytes[o] / 255f, bytes[o + 1] / 255f, bytes[o + 2] / 255f, bytes[o + 3] / 255f));
                }
            }
            tex.Apply(false, false);
            return tex;
        }

        // Tints the nametag green and shows a speaker icon beside it while this player is talking.
        public void UpdateTalkingIndicator() {
            if (!repNameText)
                return;

            bool talking = Entanglement.Voice.VoiceManager.IsSpeaking(playerId);
            if (talking == wasTalking && repTalkingIcon && repTalkingIcon.gameObject.activeSelf == talking)
                return;

            wasTalking = talking;
            repNameText.color = talking && !PlayerFlair.HasFlair(playerId)
                ? new Color(0.4f, 1f, 0.5f)
                : baseNameColor;

            if (repTalkingIcon)
                repTalkingIcon.gameObject.SetActive(talking);

            PlaceSkull();
            PlaceTalking();
        }

        // This calculates the velocity on the client side for leg prediction
        public void SaveVelocity(float dt) {
            //Get Velocities
            Vector3 currentPosition = repRoot.position;
            //Ground Check
            repSavedVel = Vector3.Slerp(repInputVel, PhysicsData.GetVelocity(currentPosition, prevRepRootPos, dt), dt * legJitter);

            if (isGrounded) {
                repInputVel = repSavedVel;
            }
            else
                repInputVel = Vector3.zero;
            //Cache
            prevRepRootPos = currentPosition;
        }

        public void UpdateIK(float dt)
        {
            // Catch errors so other players arent broken
            try {
                //Re-Apply playermodel if unloaded
                if ((!currentSkinBundle || !currentSkinObject) && isCustomSkinned && loadingSkinPath == null)
                    PlayerSkinLoader.ApplyPlayermodel(this, currentSkinPath);
                // Prevent exceptions if we are in the middle of deleting a playermodel
                if (!activeAnimator)
                    return;

                activeAnimator.Update(dt);
                repAnimationManager.OnLateUpdate();
                SaveVelocity(dt);
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

        public void UpdatePose(Handedness hand, string pose) {
            try {
                if (repAnimationManager)
                    repAnimationManager.SetHandPose(hand, pose);
            }
            catch { }
        }

        public void UpdatePoseRadius(Handedness hand, float radius) {
            try {
                if (repAnimationManager)
                    repAnimationManager.SetCylinderRadius(hand, radius);
            }
            catch { }
        }

        public void UpdateFingers(Handedness hand, float indexCurl = 1f, float middleCurl = 1f, float ringCurl = 1f, float pinkyCurl = 1f, float thumbCurl = 1f) {
            try {
                if (repAnimationManager)
                    repAnimationManager.ApplyFingerCurl(hand, 1f - thumbCurl, 1f - indexCurl, 1f - middleCurl, 1f - ringCurl, 1f - pinkyCurl);
            }
            catch { }
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
            return BuildPlayerSyncData();
        }

        static PlayerRepSyncData BuildPlayerSyncData() {
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

            return data;
        }

        const float PoseRate = 60f;
        const float PoseInterval = 1f / PoseRate;
        static float nextPoseSend;

        public static void SyncPlayerReps() {
            if (SteamIntegration.hasLobby) {
                if (Entanglement.Network.DownloadGate.IsGated)
                    return;

                if (SceneLoader.loading)
                    return;

                float now = Time.realtimeSinceStartup;
                if (now >= nextPoseSend) {
                    nextPoseSend = Mathf.Max(nextPoseSend + PoseInterval, now - PoseInterval);

                    var syncData = GetPlayerSyncData();

                    if (syncData != null) {
                        NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerRepSync, syncData);
                        Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
                    }
                    else
                        GetPlayerTransforms();
                }
            }

            // Solo (or with lobby): keep feeding the representation-test dummy.
            if (debugRepresentation != null || AnyDebugGhost) {
                if (syncedPoints[0] == null)
                    GetPlayerTransforms();
                FeedDebugMirror();
            }
        }

        // Reps are solved (animator and full-body IK, the costly part) every physics step and drawn
        // between the last two solves. A PC that can't keep up runs several steps per frame and solved
        // each rep that many times for one picture. Then they are solved once per frame in Update
        // instead, at the frame's own time, and drawn as solved. Hysteresis keeps it from flapping.
        static float stepsPerFrame = 1f;
        static bool solvePerFrame;
        static bool solvedInSteps;
        static bool presentAsSolved;

        /// <summary>Physics step (OnFixedUpdate).</summary>
        public static void UpdatePlayerReps() {
            if (solvePerFrame || !syncedRoot)
                return;

            solvedInSteps = true;
            foreach (PlayerRepresentation rep in representations.Values)
                UpdateOneRep(rep, Time.fixedDeltaTime);

            if (debugRepresentation != null)
                UpdateOneRep(debugRepresentation, Time.fixedDeltaTime);
        }

        /// <summary>Once per frame (OnUpdate), after the frame's physics steps.</summary>
        public static void UpdatePlayerRepsPerFrame() {
            stepsPerFrame = Mathf.Lerp(stepsPerFrame, Entanglement.Network.NetStats.FixedStepsThisFrame, 0.1f);
            if (!solvePerFrame && stepsPerFrame > 1.3f)
                solvePerFrame = true;
            else if (solvePerFrame && stepsPerFrame < 1.05f)
                solvePerFrame = false;

            // The frame it switched on, the steps already solved: drawn between solves as usual.
            presentAsSolved = solvePerFrame && !solvedInSteps;
            solvedInSteps = false;
            if (!presentAsSolved || !syncedRoot)
                return;

            foreach (PlayerRepresentation rep in representations.Values)
                UpdateOneRep(rep, -1f);

            if (debugRepresentation != null)
                UpdateOneRep(debugRepresentation, -1f);
        }

        float lastSolveAt = -1f;

        // dt below zero: the time since this rep was last solved (per-frame solving).
        static void UpdateOneRep(PlayerRepresentation rep, float dt) {
            if (rep == null || !rep.repRoot)
                return;

            float now = Time.time;
            if (dt < 0f)
                dt = rep.lastSolveAt >= 0f ? Mathf.Clamp(now - rep.lastSolveAt, 0.001f, 0.1f) : Time.deltaTime;
            rep.lastSolveAt = now;

            // Silent for a while (loading into a level): out of the world until it speaks again.
            rep.ParkIfStale();
            rep.UpdateAway();
            rep.UpdateDead();

            // LateUpdate left the bones mid-glide. Put the last solve back before the next one.
            rep.RestoreSolvedPose();
            rep.ApplyNetSmoothing(dt);
            rep.UpdateSpawnOverlap();

            float dist = (syncedRoot.position - rep.repRoot.position).sqrMagnitude;

            if (dist < 1000000f) {
                rep.UpdateIK(dt);
                rep.RememberSolvedPose();
                rep.repCanvasTransform?.gameObject?.SetActive(Client.nameTagsVisible && rep.NametagShown);
            }
            rep.EnsureEnemyTarget();
        }

        /// <summary>Runs in FixedUpdate so the rep physics bodies move on the physics clock.</summary>
        public static void DriveRepPhysics() {
            foreach (PlayerRepresentation rep in representations.Values) {
                if (rep == null)
                    continue;
                // Wire the existing grab helpers (were dead code). Holding a rep softens net fight.
                try { rep.UpdateLocalGrabState(); }
                catch { }
                if (rep.heldByLocalPlayer) {
                    try { rep.DampLocalHoldVertical(); }
                    catch { }
                }
                // Registered NetworkPlayers are PD-driven by NetworkEntityManager → RigPuppet.
                if (!NetworkPlayer.IsRegistered(rep.playerId))
                    rep.DriveEnemyTarget();
            }

            if (debugRepresentation != null) {
                try { debugRepresentation.UpdateLocalGrabState(); }
                catch { }
                if (!NetworkPlayer.IsRegistered(debugRepresentation.playerId))
                    debugRepresentation.DriveEnemyTarget();
            }

            TickLocalGrabPull();
            try { Entanglement.Network.PlayerRepGrabNet.Tick(); }
            catch { }
        }

        public static void PresentPlayerReps() {
            foreach (PlayerRepresentation rep in representations.Values) {
                if (rep == null || !rep.repRoot)
                    continue;
                if (presentAsSolved)
                    rep.UpdateNametagPosition();
                else
                    rep.PresentInterpolatedPose();
                rep.StepFade();
            }
            if (debugRepresentation != null && debugRepresentation.repRoot) {
                if (presentAsSolved)
                    debugRepresentation.UpdateNametagPosition();
                else
                    debugRepresentation.PresentInterpolatedPose();
                debugRepresentation.StepFade();
            }
            try { PresentDebugHeldLoopback(); } catch { }
        }

        void ClearPoseCache() {
            posedBones = null;
            posePrev = null;
            poseCurr = null;
            rotPrev = null;
            rotCurr = null;
            hasSolvedPose = false;
        }

        void EnsurePoseCache() {
            if (posedBones != null || !repRoot)
                return;

            var list = new List<Transform>();
            list.Add(repRoot);
            if (repAnimator)
                CollectBones(repAnimator.transform, list);

            posedBones = list.ToArray();
            int n = posedBones.Length;
            posePrev = new Vector3[n];
            poseCurr = new Vector3[n];
            rotPrev = new Quaternion[n];
            rotCurr = new Quaternion[n];
        }

        static void CollectBones(Transform bone, List<Transform> list) {
            if (!bone)
                return;
            list.Add(bone);
            int count = bone.childCount;
            for (int i = 0; i < count; i++)
                CollectBones(bone.GetChild(i), list);
        }

        void RememberSolvedPose() {
            EnsurePoseCache();
            if (posedBones == null)
                return;

            if (hasSolvedPose) {
                Array.Copy(poseCurr, posePrev, poseCurr.Length);
                Array.Copy(rotCurr, rotPrev, rotCurr.Length);
            }

            for (int i = 0; i < posedBones.Length; i++) {
                Transform bone = posedBones[i];
                if (!bone)
                    continue;
                poseCurr[i] = bone.position;
                rotCurr[i] = bone.rotation;
                if (!hasSolvedPose) {
                    posePrev[i] = poseCurr[i];
                    rotPrev[i] = rotCurr[i];
                }
            }
            hasSolvedPose = true;
        }

        void RestoreSolvedPose() {
            if (!hasSolvedPose || posedBones == null)
                return;
            WritePose(poseCurr, rotCurr);
        }

        void PresentInterpolatedPose() {
            if (!hasSolvedPose || posedBones == null)
                return;

            float step = Time.fixedDeltaTime;
            float t = step > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / step) : 1f;
            int n = posedBones.Length;
            for (int i = 0; i < n; i++) {
                Transform bone = posedBones[i];
                if (!bone)
                    continue;
                bone.SetPositionAndRotation(
                    Vector3.Lerp(posePrev[i], poseCurr[i], t),
                    Quaternion.Slerp(rotPrev[i], rotCurr[i], t));
            }
            UpdateNametagPosition();
        }

        void WritePose(Vector3[] positions, Quaternion[] rotations) {
            for (int i = 0; i < posedBones.Length; i++) {
                Transform bone = posedBones[i];
                if (!bone)
                    continue;
                bone.SetPositionAndRotation(positions[i], rotations[i]);
            }
        }

    }

    public static class PlayerFlair {
        // STEAM_0:0:574510147 and STEAM_0:1:870298979
        public const long OwnerSteamId = 76561199109286022L;
        public const long FriendSteamId = 76561199700863687L;
        // STEAM_0:1:69569060
        public const long CubeSteamId = 76561198099403849L;

        public static readonly Color NameYellow = new Color(1f, 0.92f, 0.1f, 1f);
        public static readonly Color NameLightBlue = new Color(0.45f, 0.82f, 1f, 1f);
        public static readonly Color NameTesterBlue = NameLightBlue;

        static readonly long[] ownerIds = { OwnerSteamId, FriendSteamId };

        public static bool IsOwner(long steamId) {
            for (int i = 0; i < ownerIds.Length; i++) {
                if (ownerIds[i] == steamId)
                    return true;
            }
            return false;
        }

        public static bool IsCube(long steamId) => steamId == CubeSteamId;

        public static bool IsTester(long steamId) => steamId == PlayerRepresentation.TesterRepId;

        public static bool HasFlair(long steamId) => IsOwner(steamId) || IsCube(steamId) || IsTester(steamId);

        public static Color GetNameColor(long steamId) {
            if (IsOwner(steamId))
                return NameYellow;
            if (IsCube(steamId))
                return NameLightBlue;
            if (IsTester(steamId))
                return NameTesterBlue;
            return Color.white;
        }

        static Texture2D skullTex;
        static Texture2D cubeTex;
        static Texture2D testerTex;

        public static Transform CreateSkull(Transform parent) {
            return CreateBadge(parent, "Skull", SkullTexture());
        }

        public static Transform CreateCube(Transform parent) {
            return CreateBadge(parent, "Cube", CubeTexture());
        }

        public static Transform CreateTester(Transform parent) {
            return CreateBadge(parent, "Tester", TesterTexture());
        }

        static Transform CreateBadge(Transform parent, string name, Texture2D tex) {
            if (!parent || tex == null)
                return null;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RawImage image = go.AddComponent<RawImage>();
            image.texture = tex;
            image.color = Color.white;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(36f, 36f);
            return go.transform;
        }

        static Texture2D SkullTexture() {
            if (skullTex != null)
                return skullTex;
            skullTex = LoadRgbaTexture("Entanglement.resources.skull.rgba");
            return skullTex;
        }

        static Texture2D CubeTexture() {
            if (cubeTex != null)
                return cubeTex;
            cubeTex = LoadRgbaTexture("Entanglement.resources.cube.rgba");
            return cubeTex;
        }

        static Texture2D TesterTexture() {
            if (testerTex != null)
                return testerTex;
            testerTex = LoadRgbaTexture("Entanglement.resources.tester.rgba");
            return testerTex;
        }

        static Texture2D LoadRgbaTexture(string resourceName, bool flipY = false) {
            byte[] bytes = ReadEmbedded(resourceName);
            if (bytes == null || bytes.Length < 12)
                return null;

            int width = System.BitConverter.ToInt32(bytes, 0);
            int height = System.BitConverter.ToInt32(bytes, 4);
            if (width < 1 || height < 1 || bytes.Length < 8 + width * height * 4)
                return null;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++) {
                int srcY = flipY ? (size - 1 - y) : y;
                int sy = srcY * height / size;
                for (int x = 0; x < size; x++) {
                    int sx = x * width / size;
                    int o = 8 + (sy * width + sx) * 4;
                    tex.SetPixel(x, y, new Color(bytes[o] / 255f, bytes[o + 1] / 255f, bytes[o + 2] / 255f, bytes[o + 3] / 255f));
                }
            }
            tex.Apply(false, false);
            return tex;
        }

        internal static byte[] ReadEmbedded(string name) {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using (System.IO.Stream stream = assembly.GetManifestResourceStream(name)) {
                if (stream == null)
                    return null;
                byte[] bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length) {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0)
                        break;
                    read += n;
                }
                return bytes;
            }
        }
    }
}
