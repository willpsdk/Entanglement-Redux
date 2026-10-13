using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Representation;
using Entanglement.Patching;

using StressLevelZero.Pool;
using StressLevelZero.Interaction;
using StressLevelZero.Props.Weapons;
using StressLevelZero.Props;

using Utilties;

using MelonLoader;

namespace Entanglement.Objects
{
    [RegisterTypeInIl2Cpp]
    public partial class TransformSyncable : Syncable {
        public TransformSyncable(IntPtr intPtr) : base(intPtr) { }

        public static CustomComponentCache<TransformSyncable> cache = new CustomComponentCache<TransformSyncable>();

        public static TransformSyncable Real(TransformSyncable found) {
            try {
                if (!found)
                    return found;
                TransformSyncable real = cache.Get(found.gameObject);
                return real ? real : found;
            }
            catch { return found; }
        }

        public static TransformSyncable[] Real(TransformSyncable[] found) {
            if (found == null)
                return found;
            for (int i = 0; i < found.Length; i++)
                found[i] = Real(found[i]);
            return found;
        }

        public Rigidbody rb;

        Transform cachedTransform;
        GameObject cachedGameObject;
        Transform Tr => !ReferenceEquals(cachedTransform, null) ? cachedTransform : (cachedTransform = transform);
        GameObject Go => !ReferenceEquals(cachedGameObject, null) ? cachedGameObject : (cachedGameObject = gameObject);

        static bool Has(UnityEngine.Object obj) => !ReferenceEquals(obj, null) && obj;

        public Rigidbody[] _CachedBodies;

        public Vector3 lastPosition;
        public Quaternion lastRotation;

        public Gun _CachedGun;
        public MagazinePlug _CachedPlug;
        // Any other socket plug on it (a Time Tower cannon ball): while it enters, sits in or leaves its
        // socket the socket moves it, and steering it toward the owner's pose fought that: it shook and
        // flew out.
        public StressLevelZero.Interaction.AlignPlug _CachedSocketPlug;

        // Remote interpolation settings
        public const float extrapolationLimit = 0.25f; // Never predict further than this past the last packet
        public const float followSharpness = 24f;      // Exponential smoothing rate for kinematic followers
        public const float snapDistance = 2f;          // Teleport instead of chasing when further than this

        // Latest received network state, used to smoothly drive the object between packets
        public bool hasNetTarget = false;

        // When the owner's pose (moving or at rest) last arrived here; -1 before any.
        public float lastRemotePoseAt = -1f;
        public Vector3 netPosition;
        public Quaternion netRotation;
        public Vector3 netVelocity;
        public Vector3 netAngularVelocity;
        public float netReceiveTime;

        public bool isWorldConstrained = false;
        protected float nextConstraintCheck = 0f;

        protected RigidbodyInterpolation startInterpolation = RigidbodyInterpolation.None;
        protected bool interpolationOverridden = false;

        protected float timeOfDisable = 0f;

        // Lower bound on seconds between sends, set for NPC ragdoll bones by PooleeSyncable.
        public float minSendInterval;
        public bool isPuppetBone;

        public bool aiPart;
        public bool aiDead;
        bool LiveAi => aiPart && !aiDead;

        // The NPC this body belongs to, told about every owner change.
        public PooleeSyncable npcRoot;

        public bool followKinematic;

        // Moved by joints the owner's own script drives (the boss claw). Here nothing drives them, so
        // their joints stay relaxed and the owner's last pose is held, even while it sits still.
        public bool holdPose;

        const float ConstraintRecheck = 4f;

        public bool inSlotUi;
        public float slotUiUntil = -1f;
        public bool slotUiResend;

        public override void Cleanup() {
            RelaxOwnJoints(false);
            SetRemoteInterpolation(false);

            base.Cleanup();
        }

        private TransformSyncMessageData cachedSyncData;
        private TransformSyncMessageData cachedRestData;
        private bool wasSleeping;
        private float lastRestTime = -10f;
        private float nextSendTime;
        private float lastSentAt = -10f;

        // A still pose is sent again this often, so a copy that missed the last one catches up.
        const float NpcKeepAlive = 2f;
        const float HoldKeepAlive = 1f;

        float InterestInterval() {
            if (cachedRigAttached)
                return minSendInterval;

            RefreshRepPositions();
            if (repCount == 0)
                return Math.Max(minSendInterval, 1f / 10f); // Nobody to see it

            Vector3 pos = Tr.position;
            float nearestSqr = float.MaxValue;
            for (int i = 0; i < repCount; i++) {
                float sqr = FastMath.SqrDist(repPositions[i], pos);
                if (sqr < nearestSqr)
                    nearestSqr = sqr;
            }

            float reach = Reach();
            if (reach > 1f) {
                float edge = Math.Max(0f, (float)Math.Sqrt(nearestSqr) - reach);
                nearestSqr = edge * edge;
            }

            float tier;
            if (nearestSqr < 100f) tier = 1f / 30f;      // < 10m
            else if (nearestSqr < 400f) tier = 1f / 20f; // < 20m
            else if (nearestSqr < 900f) tier = 1f / 12f; // < 30m
            else tier = 1f / 6f;

            return Math.Max(tier, minSendInterval);
        }

        // How far this body reaches from its pivot, worked out once.
        float reach = -1f;

        float Reach() {
            if (reach >= 0f)
                return reach;
            reach = 0f;
            try {
                Vector3 pos = Tr.position;
                Collider[] cols = GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++) {
                    Collider col = cols[i];
                    if (!col || col.isTrigger)
                        continue;
                    Bounds b = col.bounds;
                    float r = (b.center - pos).magnitude + b.extents.magnitude;
                    if (r > reach)
                        reach = r;
                }
            }
            catch { }
            return reach;
        }

        static Vector3[] repPositions = new Vector3[8];
        static int repCount;
        static float repCacheTime = -1f;

        static void RefreshRepPositions() {
            float now = Time.fixedTime;
            if (now == repCacheTime)
                return;
            repCacheTime = now;
            repCount = 0;

            foreach (KeyValuePair<long, PlayerRepresentation> pair in PlayerRepresentation.Remotes()) {
                try {
                    PlayerRepresentation rep = pair.Value;
                    if (rep == null || rep.repRoot == null)
                        continue;
                    if (repCount == repPositions.Length)
                        Array.Resize(ref repPositions, repCount * 2);
                    repPositions[repCount++] = rep.repRoot.position;
                }
                catch { }
            }
        }

        const float SettleSpeedSqr = 0.05f * 0.05f;
        const float SettleAngularSqr = 0.1f * 0.1f;
        const float SettleTime = 0.5f;

        float slowSince = -1f;
        bool settled;

        void TrackSettle(bool sleeping) {
            // Held things, the claw, and NPC bones keep sending: a still pose is still live.
            if (sleeping || rb.isKinematic || isPuppetBone || LiveAi || holdPose || cachedRigAttached || ownerQueue.Count > 0) {
                slowSince = -1f;
                settled = false;
                return;
            }

            bool slow = FastMath.SqrMag(rb.velocity) < SettleSpeedSqr && FastMath.SqrMag(rb.angularVelocity) < SettleAngularSqr;
            if (!slow) {
                slowSince = -1f;
                settled = false;
                return;
            }

            if (settled)
                return;

            if (slowSince < 0f) {
                slowSince = Time.time;
                return;
            }

            if (Time.time - slowSince < SettleTime)
                return;

            settled = true;
            SendRestState();
            rb.Sleep();
        }

        void SendRestState() {
            if (cachedRestData == null)
                cachedRestData = new TransformSyncMessageData() { resting = true };

            cachedRestData.objectId = objectId;
            cachedRestData.simplifiedTransform = new SimplifiedTransform(Tr);
            cachedRestData.velocity = default(Vector3);
            cachedRestData.angularVelocity = default(Vector3);

            TransformSyncBatcher.EnqueueRest(cachedRestData);
        }

        public void ApplyRestState(SimplifiedTransform simplifiedTransform) {
            if (SteamIntegration.currentUserId == staleOwner || IsPluggedIntoGun())
                return;

            netPosition = simplifiedTransform.position;
            netRotation = simplifiedTransform.rotation.ExpandQuat();
            netVelocity = default(Vector3);
            netAngularVelocity = default(Vector3);
            // Bodies held up by their owner's joints (the claw) or muscles (a living NPC, whose
            // muscles are off here) keep being held, or they would fold up as soon as it stood still.
            hasNetTarget = holdPose || LiveAi;
            netReceiveTime = Time.time;
            lastRestTime = Time.time;
            lastRemotePoseAt = Time.time;
            quietSteps = 0;

            transform.position = netPosition;
            transform.rotation = netRotation;

            if (rb && !rb.isKinematic) {
                rb.position = netPosition;
                rb.rotation = netRotation;
                rb.velocity = default(Vector3);
                rb.angularVelocity = default(Vector3);
                rb.Sleep();
            }
        }

        /// <summary>Follower with nothing from its owner yet: hold the pose it has now, still.</summary>
        public void HoldHere() {
            if (hasNetTarget || IsOwner() || !Has(rb))
                return;
            netPosition = rb.position;
            netRotation = rb.rotation;
            netVelocity = default(Vector3);
            netAngularVelocity = default(Vector3);
            hasNetTarget = true;
            quietSteps = 0;
        }

        public override void SyncUpdate() {
            float interval = InterestInterval();
            if (interval > 0f) {
                if (Time.time < nextSendTime)
                    return;
                nextSendTime = Time.time + interval;
            }

            // Reused every step, the batcher serializes it before the next overwrite
            if (cachedSyncData == null)
                cachedSyncData = new TransformSyncMessageData();

            Transform self = Tr;
            bool hasRb = Has(rb);
            cachedSyncData.objectId = objectId;
            cachedSyncData.simplifiedTransform = new SimplifiedTransform(self);
            cachedSyncData.velocity = hasRb ? rb.velocity : default(Vector3);
            cachedSyncData.angularVelocity = hasRb ? rb.angularVelocity : default(Vector3);

            TransformSyncBatcher.Enqueue(cachedSyncData);
            lastSentAt = Time.time;

            UpdateStoredPositions();
        }

        private Transform cachedParent;
        private bool cachedRigAttached;

        public override bool ShouldSync() {
            if (inSlotUi && HolsterSync.HiddenInUi(this))
                return false;

            if (!aiPart) {
                Transform parent = Tr.parent;
                if (parent != cachedParent) {
                    cachedParent = parent;
                    cachedRigAttached = parent && Tr.GetComponentInParent<StressLevelZero.Rig.RigManager>();
                }
            }

            if (cachedRigAttached)
                return HasChangedPositions();

            if (settled && !holdPose)
                return false;

            // A living NPC's bones go out together: when any of them has moved, all of them do.
            if (npcRoot && LiveAi) {
                bool changed = HasChangedPositions();
                if (changed)
                    npcRoot.NoteBoneMoved();
                return changed || npcRoot.PoseMoving || Time.time - lastSentAt > NpcKeepAlive;
            }

            // The claw keeps telling its followers where it is while it sits still, as nothing
            // else there holds it up.
            if (holdPose)
                return HasChangedPositions() || Time.time - lastSentAt > HoldKeepAlive;

            // Sleep means nothing for a kinematic body moved by script; just send when it moves.
            if (rb && rb.isKinematic)
                return HasChangedPositions();

            return rb ? !rb.IsSleeping() && HasChangedPositions() : HasChangedPositions();
        }

        public bool HasChangedPositions() {
            Transform self = Tr;
            return FastMath.SqrDist(self.position, lastPosition) > 0.0001f || FastMath.Angle(self.rotation, lastRotation) > 0.05f;
        }

        protected override void UpdateOwner(bool checkForMag = true) {
            quietSteps = 0;

            // Drop any buffered remote state the moment we own the object, it is stale now
            if (IsOwner()) {
                hasNetTarget = false;
                heldAtRest = false;
            }

            if (npcRoot)
                npcRoot.WakeTick();

            if (TryGetRigidbody(out Rigidbody rb)) {
                if (PlayerRepresentation.representations.ContainsKey(lastOwner))
                    PlayerRepresentation.representations[lastOwner].IgnoreCollision(rb, false);

                if (PlayerRepresentation.representations.ContainsKey(staleOwner))
                    PlayerRepresentation.representations[staleOwner].IgnoreCollision(rb, true);

                // A mess, but fixes magazine offset issues
                try {
                    if (checkForMag && _CachedGun) {
                        MagazineSocket magSocket = _CachedGun.magazineSocket;
                        if (magSocket && magSocket._magazinePlug) {
                            TransformSyncable _CachedTransform = cache.GetOrAdd(magSocket._magazinePlug.magazine.gameObject);
                            if (_CachedTransform) _CachedTransform.ForceOwner(staleOwner, false);
                        }
                    }
                } catch { }
            }

            if (_CachedPlug && lastOwner != staleOwner && staleOwner == SteamIntegration.currentUserId
                && !Magazine_Settings.HeldHere(transform)) {
                Socket plugSocket = _CachedPlug._lastSocket;
                if (_CachedPlug.InGun()) {
                    _CachedPlug.ForceEject();
                    _CachedPlug.InsertPlug(plugSocket);
                }
            }
        }

        protected void OnEnable() {
            quietSteps = 0;
            if (isValid && hasNetTarget && !IsOwner()) {
                transform.position = netPosition;
                transform.rotation = netRotation;

                if (TryGetRigidbody(out Rigidbody body) && !body.isKinematic) {
                    body.position = netPosition;
                    body.rotation = netRotation;
                    body.velocity = netVelocity;
                    body.angularVelocity = netAngularVelocity;
                }
                heldAtRest = false;
            }
        }

        protected void OnDisable() {
            timeOfDisable = Time.realtimeSinceStartup;

            SendDequeue();
        }

        protected void OnDestroy() {
            try {
                Syncable current;
                if (objectId != 0 && ObjectSync.syncedObjects.TryGetValue(objectId, out current) && ReferenceEquals(current, this))
                    ObjectSync.syncedObjects.Remove(objectId);
            }
            catch { }

            // Nulled, not removed: the index is the id callback the host answers with.
            for (int i = 0; i < ObjectSync.queuedSyncs.Count; i++) {
                if (ReferenceEquals(ObjectSync.queuedSyncs[i], this))
                    ObjectSync.queuedSyncs[i] = null;
            }

            try {
                GameObject go = gameObject;
                if (ReferenceEquals(cache.Get(go), this))
                    cache.Remove(go);
                if (_CachedDestructable && ReferenceEquals(DestructCache.Get(_CachedDestructable.gameObject), this))
                    DestructCache.Remove(_CachedDestructable.gameObject);
                if (_CachedHealth && ReferenceEquals(DestructCache.Get(_CachedHealth.gameObject), this))
                    DestructCache.Remove(_CachedHealth.gameObject);
            }
            catch { }
        }

        protected void UpdateStoredPositions() {
            Transform self = Tr;
            lastPosition = self.position;
            lastRotation = self.rotation;
        }

        protected virtual void OnCollisionEnter(Collision collision) {
            try { OnCollisionEnterSafe(collision); }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("TransformSyncable.OnCollisionEnter", e); }
        }

        void OnCollisionEnterSafe(Collision collision) {
            if (!SteamIntegration.hasLobby)
                return;

            // Cheap checks first, this runs for every contact a synced body makes.
            if (!collision.rigidbody || !IsOwner()) return;

            if (collision.collider.gameObject.IsBlacklisted()) return;

            Transform targetObj = collision.rigidbody.transform;

            // Something of ours, swung or thrown, struck an NPC copy that another player runs: their
            // game ignores what its copy of this did (NpcCollisionHook), so the contact goes to them.
            // NPC bodies are never adopted from a contact, whatever touched them.
            if (PooleeSyncable.IsNpcBody(targetObj)) {
                Vector3 relative = collision.relativeVelocity;
                if (!aiPart && rb && FastMath.SqrMag(relative) > MinSwingSpeed * MinSwingSpeed && !Bladed()) {
                    float push = Math.Max(FastMath.Mag(collision.impulse), FastMath.Mag(relative) * Math.Min(rb.mass, 4f));
                    PooleeSyncable.ReportLocalHit(collision, push, rb.worldCenterOfMass);
                }
                return;
            }

            if (rb)
                if (!_CachedBodies.IsHolding()) return;

            if (SpawnManager.IsDebris(targetObj))
                return;

            Rigidbody[] rigidbodies = null;

            long ownerId = SteamIntegration.currentUserId;

            ObjectSync.GetPooleeData(targetObj, out rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime);

            for (int i = 0; i < rigidbodies.Length; i++) {
                Rigidbody rb = rigidbodies[i];
                TransformSyncable syncObj = cache.Get(rb.gameObject);
                if (!syncObj && !rb.isKinematic) {
                    ushort? objectId = null;
                    ushort callbackIndex = 0;

                    if (Server.instance != null)
                        objectId = ObjectSync.GetNextObjectId();

                    Syncable syncable = CreateSync(ownerId, rb, objectId);

                    if (Server.instance == null)
                        callbackIndex = ObjectSync.QueueSyncable(syncable);

                    TransformCreateMessageData createSync = new TransformCreateMessageData()
                    {
                        ownerId = ownerId,
                        objectId = objectId != null ? objectId.Value : (ushort)0,
                        callbackIndex = callbackIndex,
                        objectPath = rb.transform.GetFullPath(overrideRootName),
                        spawnIndex = spawnIndex,
                        spawnTime = spawnTime,
                        enqueueOwner = false
                    };

                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCreate, createSync);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Object, message.GetBytes());
                }
            }
        }

        // Slower than this against an NPC is resting it there, not a swing.
        const float MinSwingSpeed = 2f;
        int bladed = -1;

        bool Bladed() {
            if (bladed < 0) {
                try { bladed = GetComponentInChildren<StressLevelZero.Combat.StabSlash>(true) || GetComponentInParent<StressLevelZero.Combat.StabSlash>() ? 1 : 0; }
                catch { bladed = 0; }
            }
            return bladed == 1;
        }

        protected override void FixedUpdate() {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { Step(); }
            finally {
                NetStats.bodyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                NetStats.bodySteps++;
            }
        }

        // A body at rest on both ends (asleep, its rest pose sent or received) had nothing to do each
        // physics step but still paid for half a dozen calls into the game: with a thousand crate pieces
        // and props that was 15-20ms a frame on Oct 8. It now skips its steps, looking again every few;
        // news from its owner, an owner change or a grab wakes it at once.
        const int QuietSteps = 6;
        int quietSteps;
        bool quietOwner;
        int quietQueue;

        public static int quietSkips;

        /// <summary>Back to full work at the next physics step.</summary>
        public void Wake() => quietSteps = 0;

        void Step() {
            if (quietSteps > 0) {
                if (isValid && IsOwner() == quietOwner && ownerQueue.Count == quietQueue && !isPuppetBone && !LiveAi) {
                    quietSteps--;
                    quietSkips++;
                    return;
                }
                quietSteps = 0;
            }

            base.FixedUpdate();

            if (isValid) {
                JointCheck(); // Check for joint

                if (IsOwner() && rb && !rb.isKinematic) {
                    bool sleeping = rb.IsSleeping();
                    if (sleeping && !wasSleeping)
                        SendRestState();
                    wasSleeping = sleeping;
                    TrackSettle(sleeping);
                }

                if (!IsOwner()) {
                    // A plugged magazine rides its gun's sync, driving it would fight the plug joint.
                    // A body the game keeps kinematic (a holster pin, a script-moved part) stays that
                    // way and is moved, not steered.
                    if (hasNetTarget && Has(rb) && !IsPluggedIntoGun() && !IdleReleased()) {
                        if (!rb.isKinematic)
                            InterpolateRemote();
                        else if (followKinematic)
                            FollowKinematic();
                    }
                }

                MaybeGoQuiet();
            }

        }

        // Nobody is holding it and its owner has sent nothing for this long: it was let go where it is.
        // A copy kept pulling itself to that last pose, so a door someone had pushed or grabbed stayed
        // heavy for everyone else (they pushed against the pull) until they took hold of it themselves.
        // New data from the owner, or anyone holding it, ends the release.
        const float IdleRelease = 2f;

        bool IdleReleased() {
            if (holdPose || aiPart || isPuppetBone || npcRoot || followKinematic || inSlotUi || ownerQueue.Count > 0)
                return false;
            return Time.time - netReceiveTime > IdleRelease;
        }

        void MaybeGoQuiet() {
            // Living NPC bodies, the claw, held and slotted things, and script-moved bodies keep every
            // step. A dead NPC lying still is just a pile of bodies, like crate pieces.
            if (LiveAi || isPuppetBone || (npcRoot && !aiDead) || holdPose || cachedRigAttached || inSlotUi || ownerQueue.Count > 0)
                return;
            if (!Has(rb) || rb.isKinematic)
                return;

            bool owner = IsOwner();
            if (owner) {
                // Its rest pose went out when it fell asleep (wasSleeping is this step's IsSleeping).
                if (!wasSleeping)
                    return;
            }
            else {
                if (IsPluggedIntoGun())
                    return;
                if (hasNetTarget && !IdleReleased() && !(heldAtRest && netReceiveTime <= restAt))
                    return;
                if (!rb.IsSleeping())
                    return;
            }

            quietOwner = owner;
            quietQueue = ownerQueue.Count;
            quietSteps = QuietSteps + (objectId & 3);
        }
        protected bool TryGetRigidbody(out Rigidbody rigidbody) {
            if (rb) {
                rigidbody = rb;
                return true;
            }
            else {
                try { rigidbody = gameObject.GetComponent<Rigidbody>(); }
                catch { rigidbody = null; }
                rb = rigidbody;
                return rigidbody != null;
            }
        }

        public static Syncable CreateSync(long owner, Rigidbody rigidbody = null, ushort? objectId = null) {
            if (!rigidbody)
                return null;

            GameObject go = rigidbody.gameObject;
            TransformSyncable existingSync = cache.GetOrAdd(go);
            if (existingSync) {
                existingSync.ForceOwner(owner);
                if (objectId != null) {
                    ObjectSync.MoveSyncable(existingSync, objectId.Value);
                }
                return existingSync;
            }
            rigidbody.velocity = Vector3.zero;
            TransformSyncable syncObj = go.AddComponent<TransformSyncable>();
            cache.Add(go, syncObj);
            syncObj._CachedPlug = go.GetComponentInChildren<MagazinePlug>(true);
            if (!syncObj._CachedPlug) {
                try {
                    syncObj._CachedSocketPlug = go.GetComponentInChildren<StressLevelZero.Interaction.AlignPlug>(true);
                    if (syncObj._CachedSocketPlug)
                        EntangleLogger.Debug("[Socket] " + go.name + " has a socket plug (" + syncObj._CachedSocketPlug.GetIl2CppType().Name + "); not steered while it is in a socket");
                }
                catch { }
            }
            syncObj._CachedGun = go.GetComponentInChildren<Gun>(true);
            syncObj._CachedDestructable = go.GetComponentInChildren<ObjectDestructable>(true);
            syncObj._CachedHealth = go.GetComponentInChildren<Prop_Health>(true);

            if (syncObj._CachedDestructable)
                DestructCache.Add(syncObj._CachedDestructable.gameObject, syncObj);
            if (syncObj._CachedHealth)
                DestructCache.Add(syncObj._CachedHealth.gameObject, syncObj);

            syncObj.events = syncObj.GetComponentsInChildren<GripEvents>(true);
            syncObj.SetupEvents();

            syncObj.rb = rigidbody;
            syncObj._CachedBodies = syncObj.transform.GetJointedBodies();
            syncObj.objectHealth = syncObj.GetHealth();
            syncObj.healthCaptured = true;
            syncObj.ForceOwner(owner);
            if (objectId != null) {
                ushort id = objectId.Value;
                syncObj.objectId = id;
                syncObj.isValid = true;
                ObjectSync.RegisterSyncable(syncObj, id);
            }
            return syncObj;
        }

        public override void SendEnqueue() => MelonCoroutines.Start(WaitUntilValid(OnValidEnqueue));

        public override void SendDequeue() => MelonCoroutines.Start(WaitUntilValid(OnValidDequeue));

        public void OnValidEnqueue() {
            long userId = SteamIntegration.currentUserId;
            if (ownerQueue.Contains(userId))
                return;

            if (Server.instance != null || aiPart) {
                EnqueueOwner(userId);
            }

            TransformQueueMessageData queueData = new TransformQueueMessageData()
            {
                userId = userId,
                objectId = objectId,
                isAdd = true
            };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformQueue, queueData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Object, message.GetBytes());
        }

        public void OnValidDequeue() {
            long userId = SteamIntegration.currentUserId;
            if (!ownerQueue.Contains(userId))
                return;

            if (Server.instance != null || aiPart) {
                DequeueOwner(userId);
            }

            TransformQueueMessageData queueData = new TransformQueueMessageData()
            {
                userId = userId,
                objectId = objectId,
                isAdd = false
            };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformQueue, queueData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Object, message.GetBytes());
        }

        public IEnumerator WaitUntilValid(Action onFinish) {
            while (!isValid)
                yield return null;

            onFinish?.Invoke();
        }

        public void ApplyTransform(SimplifiedTransform simplifiedTransform) => ApplyTransform(simplifiedTransform, default(Vector3), default(Vector3));

        public void ApplyTransform(SimplifiedTransform simplifiedTransform, Vector3 velocity, Vector3 angularVelocity) {
            if (SteamIntegration.currentUserId == staleOwner || IsPluggedIntoGun()) return;

            if (Time.time - lastRestTime < 0.35f)
                return;

            netPosition = simplifiedTransform.position;
            netRotation = simplifiedTransform.rotation.ExpandQuat();
            netVelocity = velocity;
            netAngularVelocity = angularVelocity;
            netReceiveTime = Time.time;
            hasNetTarget = true;
            lastRemotePoseAt = Time.time;
            quietSteps = 0;

            if (!Has(rb)) simplifiedTransform.Apply(Tr);

            if (!Go.activeSelf && timeOfDisable > 0f && Math.Abs(Time.realtimeSinceStartup - timeOfDisable) >= 2f && MayRevive())
                transform.gameObject.SetActive(true);
        }

        bool neverRevive;

        bool MayRevive() {
            if (neverRevive)
                return false;
            // An NPC's bones are switched off and on by its puppet (frozen while it rests); the
            // PooleeSyncable wakes the whole puppet when its owner's bones start coming.
            if (aiPart || isPuppetBone || npcRoot)
                return false;
            try {
                if (Has(_CachedHealth) || BreakPieces.IsBreakable(Go) || BreakPieces.IsPiece(Tr)) {
                    neverRevive = true;
                    return false;
                }

                StressLevelZero.Props.ObjectDestructable destructable = _CachedDestructable ? _CachedDestructable : GetComponent<StressLevelZero.Props.ObjectDestructable>();
                if (destructable && destructable._isDead)
                    return false;

                StressLevelZero.Pool.Poolee poolee = GetComponent<StressLevelZero.Pool.Poolee>();
                if (poolee && poolee.pool && poolee.pool._spawnedObjects != null && !poolee.pool._spawnedObjects.Contains(poolee))
                    return false;
            }
            catch { }
            return true;
        }

        // Nothing from the owner for this long: it has stopped (owners send on change), so the body
        // is held where it was last seen and may sleep there.
        public const float SilenceSeconds = 0.5f;

        // On its last pose and this still: asleep until something moves it or news comes.
        const float RestErrorSqr = 0.005f * 0.005f;
        const float RestAngle = 1.5f;
        const float RestSpeedSqr = 0.03f * 0.03f;
        const float RestSpinSqr = 0.1f * 0.1f;
        const float StillTargetSqr = 0.02f * 0.02f;

        bool heldAtRest;
        float restAt;

        /// <summary>Bodies steered by forces this physics step, for the debug readout.</summary>
        public static int drivenThisStep;
        static float drivenStepTime = -1f;

        static void CountDriven() {
            if (Time.fixedTime != drivenStepTime) {
                drivenStepTime = Time.fixedTime;
                drivenThisStep = 0;
            }
            drivenThisStep++;
        }

        /// <summary>
        /// Steers this copy toward the owner's pose with forces (FollowDrive). The body stays dynamic:
        /// walls stop it, it rests on the floor and shoves what it touches, instead of being dragged
        /// through everything. Far off, it is moved there. Once it sits still on a still pose it is
        /// left asleep until the owner says something new or something knocks it.
        /// </summary>
        protected void InterpolateRemote() {
            // Pulling a magazine out of a gun someone else holds: the gun was steered toward its owner's
            // pose harder than a hand can pull, so the mag only shook in the socket and was never freed,
            // and nothing happened on the holder's side. For as long as a hand here has the mag, the gun
            // is held where it is (gravity cancelled, motion damped) and the grip wins.
            if (Has(_CachedGun) && GripBeatsGun()) {
                Vector3 hold = FastMath.Sub(default(Vector3), Physics.gravity);
                rb.AddForce(hold, ForceMode.Acceleration);
                rb.velocity = FastMath.Mul(rb.velocity, 0.5f);
                rb.angularVelocity = FastMath.Mul(rb.angularVelocity, 0.5f);
                return;
            }

            if (heldAtRest) {
                if (netReceiveTime <= restAt && rb.IsSleeping())
                    return;
                heldAtRest = false;
            }

            float since = Time.time - netReceiveTime;
            bool silent = since > SilenceSeconds;

            Vector3 targetVel = netVelocity;
            Vector3 targetAngVel = netAngularVelocity;
            Vector3 predictedPos = netPosition;
            Quaternion predictedRot = netRotation;

            if (silent) {
                // The owner stopped sending: it sits at the last pose, it is not still moving.
                targetVel = default(Vector3);
                targetAngVel = default(Vector3);
            }
            else {
                // Dead reckon from the last packet so the object keeps moving between updates
                float age = Math.Min(since, extrapolationLimit);
                predictedPos = FastMath.AddScaled(netPosition, netVelocity, age);
                float angSpeed = FastMath.Mag(netAngularVelocity);
                if (angSpeed > 0.001f)
                    predictedRot = FastMath.Mul(FastMath.AngleAxis(angSpeed * age * 57.29578f, netAngularVelocity), netRotation);
            }

            if (!FollowDrive.Sane(predictedPos))
                return;

            Vector3 position = rb.position;
            Quaternion rotation = rb.rotation;
            Vector3 velocity = rb.velocity;
            Vector3 angularVelocity = rb.angularVelocity;
            float errorSqr = FastMath.SqrDist(predictedPos, position);

            bool unitSnap = isPuppetBone && npcRoot && npcRoot.SnapPending;
            if (unitSnap || errorSqr > snapDistance * snapDistance) {
                if (!unitSnap && isPuppetBone && npcRoot)
                    npcRoot.RequestSnap();
                rb.position = predictedPos;
                rb.rotation = predictedRot;
                rb.velocity = targetVel;
                rb.angularVelocity = targetAngVel;
                return;
            }

            bool targetStill = FastMath.SqrMag(targetVel) < StillTargetSqr && FastMath.SqrMag(targetAngVel) < StillTargetSqr;
            if (targetStill && errorSqr < RestErrorSqr && FastMath.Angle(rotation, predictedRot) < RestAngle
                && FastMath.SqrMag(velocity) < RestSpeedSqr && FastMath.SqrMag(angularVelocity) < RestSpinSqr) {
                rb.velocity = default(Vector3);
                rb.angularVelocity = default(Vector3);
                rb.Sleep();
                heldAtRest = true;
                restAt = Time.time;
                return;
            }

            FollowDrive.Steer(rb, position, rotation, velocity, angularVelocity, predictedPos, predictedRot, targetVel, targetAngVel, Time.fixedDeltaTime);
            CountDriven();
        }

        bool GripBeatsGun() {
            try {
                MagazineSocket socket = _CachedGun.magazineSocket;
                MagazinePlug plug = socket ? socket._magazinePlug : null;
                if (!plug || !plug.magazine)
                    return false;
                return Magazine_Settings.HeldHere(plug.magazine.transform);
            }
            catch { return false; }
        }

        void FollowKinematic() {
            float age = Math.Min(Time.time - netReceiveTime, extrapolationLimit);
            Vector3 predictedPos = FastMath.AddScaled(netPosition, netVelocity, age);
            Vector3 current = rb.position;

            if (FastMath.SqrDist(current, predictedPos) > snapDistance * snapDistance) {
                rb.position = predictedPos;
                rb.rotation = netRotation;
                return;
            }

            float t = 1f - FastMath.Exp(-followSharpness * Time.fixedDeltaTime);
            rb.MovePosition(FastMath.Lerp(current, predictedPos, t));
            rb.MoveRotation(FastMath.Slerp(rb.rotation, netRotation, t));
        }

        ConfigurableJoint[] relaxedJoints;
        JointDrive[] savedDrives;
        HingeJoint[] relaxedHinges;
        bool[] savedHingeFlags;
        bool jointsRelaxed;

        void RelaxOwnJoints(bool relax) {
            if (relax == jointsRelaxed)
                return;
            jointsRelaxed = relax;

            try {
                if (relax) {
                    relaxedJoints = GetComponents<ConfigurableJoint>();
                    savedDrives = new JointDrive[relaxedJoints.Length * 6];
                    JointDrive off = new JointDrive();
                    off.positionSpring = 0f;
                    off.positionDamper = 0f;
                    off.maximumForce = 0f;

                    for (int i = 0; i < relaxedJoints.Length; i++) {
                        ConfigurableJoint joint = relaxedJoints[i];
                        if (!joint) {
                            relaxedJoints[i] = null;
                            continue;
                        }
                        int b = i * 6;
                        savedDrives[b] = joint.slerpDrive;
                        savedDrives[b + 1] = joint.angularXDrive;
                        savedDrives[b + 2] = joint.angularYZDrive;
                        savedDrives[b + 3] = joint.xDrive;
                        savedDrives[b + 4] = joint.yDrive;
                        savedDrives[b + 5] = joint.zDrive;
                        joint.slerpDrive = off;
                        joint.angularXDrive = off;
                        joint.angularYZDrive = off;
                        joint.xDrive = off;
                        joint.yDrive = off;
                        joint.zDrive = off;
                    }

                    relaxedHinges = GetComponents<HingeJoint>();
                    savedHingeFlags = new bool[relaxedHinges.Length * 2];
                    for (int i = 0; i < relaxedHinges.Length; i++) {
                        HingeJoint hinge = relaxedHinges[i];
                        if (!hinge)
                            continue;
                        savedHingeFlags[i * 2] = hinge.useSpring;
                        savedHingeFlags[i * 2 + 1] = hinge.useMotor;
                        hinge.useSpring = false;
                        hinge.useMotor = false;
                    }
                }
                else {
                    if (relaxedJoints != null) {
                        for (int i = 0; i < relaxedJoints.Length; i++) {
                            ConfigurableJoint joint = relaxedJoints[i];
                            if (!joint)
                                continue;
                            int b = i * 6;
                            joint.slerpDrive = savedDrives[b];
                            joint.angularXDrive = savedDrives[b + 1];
                            joint.angularYZDrive = savedDrives[b + 2];
                            joint.xDrive = savedDrives[b + 3];
                            joint.yDrive = savedDrives[b + 4];
                            joint.zDrive = savedDrives[b + 5];
                        }
                    }
                    if (relaxedHinges != null) {
                        for (int i = 0; i < relaxedHinges.Length; i++) {
                            HingeJoint hinge = relaxedHinges[i];
                            if (!hinge)
                                continue;
                            hinge.useSpring = savedHingeFlags[i * 2];
                            hinge.useMotor = savedHingeFlags[i * 2 + 1];
                        }
                    }
                    relaxedJoints = null;
                    relaxedHinges = null;
                }
            }
            catch { }
        }

        public bool IsPluggedIntoGun() => (Has(_CachedPlug) && _CachedPlug.EnteringOrInside())
            || (Has(_CachedSocketPlug) && _CachedSocketPlug.InSocket() && !SocketCopyStranded());

        // The owner took it out of the socket and carried it off, but this copy is still seated in its
        // own socket: let the follower pull it free again.
        const float StrandedSqr = 0.6f * 0.6f;

        bool SocketCopyStranded() {
            return !IsOwner() && hasNetTarget && Has(rb) && FastMath.SqrDist(rb.position, netPosition) > StrandedSqr;
        }

        protected void JointCheck() {
            if (!IsOwner()) {
                // While a magazine sits inside a gun the gun's own sync moves it
                if (IsPluggedIntoGun()) {
                    SetRemoteInterpolation(true);
                    return;
                }

                bool hasRb = Has(rb);
                float now = Time.time;

                if (hasRb && now >= nextConstraintCheck) {
                    isWorldConstrained = ComputeWorldConstrained();
                    // Spread out: bodies set up in the same frame would otherwise all check in the
                    // same frame forever, a spike every few seconds with hundreds of them.
                    nextConstraintCheck = now + ConstraintRecheck + (objectId & 63) * (ConstraintRecheck / 64f);
                }

                // NPC muscles are handled by PooleeSyncable. The claw's own joints would pull it
                // back to its rest pose on a machine that is not running its AI, so they stay
                // relaxed while we hold the owner's pose with forces.
                RelaxOwnJoints(hasRb && hasNetTarget && (holdPose || (isWorldConstrained && !isPuppetBone
                    && (now - netReceiveTime < 0.5f))));

                SetRemoteInterpolation(true);
            }
            else {
                RelaxOwnJoints(false);
                SetRemoteInterpolation(false);
                nextConstraintCheck = 0f;
            }
        }

        // True when a joint constrains this body (doors, NPC muscles, pull handles)
        protected bool ComputeWorldConstrained() {
            Joint[] joints = GetComponents<Joint>();
            return joints != null && joints.Length > 0;
        }

        protected void SetRemoteInterpolation(bool remote) {
            if (!rb) return;

            if (remote && !interpolationOverridden) {
                startInterpolation = rb.interpolation;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                interpolationOverridden = true;
            }
            else if (!remote && interpolationOverridden) {
                rb.interpolation = startInterpolation;
                interpolationOverridden = false;
            }
        }
    }
}
