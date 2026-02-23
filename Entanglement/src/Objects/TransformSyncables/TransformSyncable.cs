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

        public Rigidbody rb;
        public Rigidbody[] _CachedBodies;

        public Vector3 lastPosition;
        public Quaternion lastRotation;

        public Rigidbody targetBody;
        public GameObject targetGo;
        public ConfigurableJoint syncJoint;

        public Gun _CachedGun;
        public MagazinePlug _CachedPlug;

        public float startDrag = -1f;
        public float startAngularDrag = -1f;

        public const float positionSpring = 250000f;
        public const float positionDamper = 15000f;
        public const float maximumForce = 150000f;
        public const float linearLimit = 0.005f;

        protected float timeOfDisable = 0f;

        // FUSION UPGRADE: Cached network message data to prevent 10,000+ GC allocations per second
        private TransformSyncMessageData _cachedSyncData;

        public override void Cleanup() {
            DestroyJoint();
            if (targetGo) Destroy(targetGo);

            base.Cleanup();
        }

        public override void SyncUpdate() {
            if (_cachedSyncData == null) _cachedSyncData = new TransformSyncMessageData();

            _cachedSyncData.objectId = objectId;
            _cachedSyncData.simplifiedTransform = new SimplifiedTransform(transform, rb);

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSync, _cachedSyncData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());

            if (targetGo) {
                targetGo.transform.position = transform.position;
                targetGo.transform.rotation = transform.rotation;
            }

            UpdateStoredPositions();
        }

        public override bool ShouldSync() {
            // Cull sleeping rigidbodies entirely if they exist
            if (rb && rb.IsSleeping()) return false;
            // Otherwise, only sync if it physically moved
            return HasChangedPositions();
        }

        public bool HasChangedPositions() => (transform.position - lastPosition).sqrMagnitude > 0.001f || Quaternion.Angle(transform.rotation, lastRotation) > 0.05f;

        protected override void UpdateOwner(bool checkForMag = true) {
            if (lastOwner == SteamIntegration.currentUser.m_SteamID) objectHealth = GetHealth();

            if (!IsOwner()) SetHealth(float.PositiveInfinity);
            else SetHealth(objectHealth);

            if (TryGetRigidbody(out Rigidbody rb)) {
                if (PlayerRepresentation.representations.ContainsKey(lastOwner))
                    PlayerRepresentation.representations[lastOwner].IgnoreCollision(rb, false);

                if (PlayerRepresentation.representations.ContainsKey(staleOwner))
                    PlayerRepresentation.representations[staleOwner].IgnoreCollision(rb, true);

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

            if (_CachedPlug && lastOwner != staleOwner && staleOwner == SteamIntegration.currentUser.m_SteamID) {
                Socket plugSocket = _CachedPlug._lastSocket;
                if (_CachedPlug.InGun()) {
                    _CachedPlug.ForceEject();
                    _CachedPlug.InsertPlug(plugSocket);
                }
            }
        }

        protected void OnEnable() {
            DestroyJoint();
        }

        protected void OnDisable() {
            timeOfDisable = Time.realtimeSinceStartup;

            DestroyJoint();
            SendDequeue();
        }

        protected void UpdateStoredPositions() {
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }

        protected virtual void OnCollisionEnter(Collision collision) {
            if (!SteamIntegration.hasLobby) return;
            if (collision.collider.gameObject.IsBlacklisted()) return;
            if (!collision.rigidbody || !IsOwner()) return;

            if (rb && !_CachedBodies.IsHolding()) return;

            // QUICK CACHE LOOKUP: Prevents expensive hierarchy searches if object is already synced
            TransformSyncable existingSync = cache.Get(collision.rigidbody.gameObject);
            if (existingSync) return;

            Transform targetObj = collision.rigidbody.transform;
            Rigidbody[] rigidbodies = null;

            ulong ownerId = SteamIntegration.currentUser.m_SteamID;

            ObjectSync.GetPooleeData(targetObj, out rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime);

            for (int i = 0; i < rigidbodies.Length; i++) {
                Rigidbody rBody = rigidbodies[i];
                TransformSyncable syncObj = cache.Get(rBody.gameObject);
                if (!syncObj && !rBody.isKinematic) {
                    ushort? objectId = null;
                    ushort callbackIndex = 0;

                    if (Server.instance != null) {
                        objectId = ObjectSync.lastId;
                        objectId++;
                    }

                    Syncable syncable = CreateSync(ownerId, rBody, objectId);

                    if (Server.instance == null)
                        callbackIndex = ObjectSync.QueueSyncable(syncable);

                    TransformCreateMessageData createSync = new TransformCreateMessageData()
                    {
                        ownerId = ownerId,
                        objectId = objectId != null ? objectId.Value : (ushort)0,
                        callbackIndex = callbackIndex,
                        objectPath = rBody.transform.GetFullPath(overrideRootName),
                        spawnIndex = spawnIndex,
                        spawnTime = spawnTime,
                        enqueueOwner = false
                    };

                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCreate, createSync);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Object, message.GetBytes());
                }
            }
        }

        protected override void FixedUpdate() {
            base.FixedUpdate();

            if (isValid) {
                JointCheck(); 
                if (!IsOwner()) SetHealth(float.PositiveInfinity);
            }
        }

        protected bool TryGetRigidbody(out Rigidbody rigidbody) {
            if (rb) {
                rigidbody = rb;
                return true;
            }
            else {
                rigidbody = gameObject.GetComponent<Rigidbody>();
                rb = rigidbody;
                return rigidbody != null;
            }
        }

        public static Syncable CreateSync(ulong owner, Rigidbody rigidbody = null, ushort? objectId = null) {
            if (!rigidbody) return null;

            GameObject go = rigidbody.gameObject;
            TransformSyncable existingSync = cache.GetOrAdd(go);
            if (existingSync) {
                existingSync.ForceOwner(owner);
                if (objectId != null) ObjectSync.MoveSyncable(existingSync, objectId.Value);
                return existingSync;
            }

            rigidbody.velocity = Vector3.zero;
            TransformSyncable syncObj = go.AddComponent<TransformSyncable>();
            cache.Add(go, syncObj);
            
            syncObj._CachedPlug = go.GetComponentInChildren<MagazinePlug>(true);
            syncObj._CachedGun = go.GetComponentInChildren<Gun>(true);
            syncObj._CachedDestructable = go.GetComponentInChildren<ObjectDestructable>(true);
            syncObj._CachedHealth = go.GetComponentInChildren<Prop_Health>(true);

            // FIX: Use indexer instead of Add() to prevent Dictionary duplication crashes on pooled objects
            if (syncObj._CachedDestructable) DestructCache[syncObj._CachedDestructable.gameObject] = syncObj;
            if (syncObj._CachedHealth) DestructCache[syncObj._CachedHealth.gameObject] = syncObj;

            syncObj.events = syncObj.GetComponentsInChildren<GripEvents>(true);
            syncObj.SetupEvents();

            syncObj.rb = rigidbody;
            syncObj.startDrag = rigidbody.drag;
            syncObj.startAngularDrag = rigidbody.angularDrag;
            syncObj._CachedBodies = syncObj.transform.GetJointedBodies();
            
            syncObj.ForceOwner(owner);
            
            if (objectId != null) {
                ushort id = objectId.Value;
                syncObj.objectId = id;
                syncObj.isValid = true;
                ObjectSync.RegisterSyncable(syncObj, id);
            }
            return syncObj;
        }

        // FIX: Pre-check isValid to prevent MelonCoroutines from generating garbage on rapid enqueue requests
        public override void SendEnqueue() {
            if (isValid) OnValidEnqueue();
            else MelonCoroutines.Start(WaitUntilValid(OnValidEnqueue));
        }

        public override void SendDequeue() {
            if (isValid) OnValidDequeue();
            else MelonCoroutines.Start(WaitUntilValid(OnValidDequeue));
        }

        public void OnValidEnqueue() {
            ulong userId = SteamIntegration.currentUser.m_SteamID;
            if (ownerQueue.Contains(userId)) return;

            if (Server.instance != null) EnqueueOwner(userId);

            TransformQueueMessageData queueData = new TransformQueueMessageData() { userId = userId, objectId = objectId, isAdd = true };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformQueue, queueData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Object, message.GetBytes());
        }

        public void OnValidDequeue() {
            ulong userId = SteamIntegration.currentUser.m_SteamID;
            if (!ownerQueue.Contains(userId)) return;

            if (Server.instance != null) DequeueOwner(userId);

            TransformQueueMessageData queueData = new TransformQueueMessageData() { userId = userId, objectId = objectId, isAdd = false };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformQueue, queueData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Object, message.GetBytes());
        }

        public IEnumerator WaitUntilValid(Action onFinish) {
            while (!isValid) yield return null;
            onFinish?.Invoke();
        }

        public void ApplyTransform(SimplifiedTransform simplifiedTransform) {
            if (SteamIntegration.currentUser.m_SteamID == staleOwner || (_CachedPlug && _CachedPlug.EnteringOrInside())) return;

            if (rb && simplifiedTransform.isSleeping) {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.Sleep();
            }

            if (targetBody) {
                targetBody.transform.position = simplifiedTransform.position;
                targetBody.transform.rotation = simplifiedTransform.rotation.ExpandQuat();
                
                if (!simplifiedTransform.isSleeping) {
                    targetBody.velocity = simplifiedTransform.velocity;
                    targetBody.angularVelocity = simplifiedTransform.angularVelocity;
                }
            }
            
            if (!rb) {
                simplifiedTransform.Apply(transform);
            } else if (!syncJoint && !simplifiedTransform.isSleeping) {
                rb.velocity = simplifiedTransform.velocity;
                rb.angularVelocity = simplifiedTransform.angularVelocity;
            }

            if (!transform.gameObject.activeInHierarchy && Mathf.Abs(Time.realtimeSinceStartup - timeOfDisable) >= 2f)
                transform.ForceActivate();
        }

        protected void ReCreateJoint() {
            if (targetGo) Destroy(targetGo);

            targetGo = new GameObject($"TransformSyncFollow {transform.name}, {transform.GetInstanceID()}");

            bool hasBody = TryGetRigidbody(out Rigidbody rb);

            targetBody = targetGo.AddComponent<Rigidbody>();
            targetBody.isKinematic = true;
            targetBody.transform.position = transform.position;
            targetBody.transform.rotation = transform.rotation;

            DestroyJoint();

            if (hasBody) {
                targetBody.mass = rb.mass;
                targetBody.centerOfMass = rb.centerOfMass;

                targetBody.transform.position = rb.transform.position;
                targetBody.transform.rotation = rb.transform.rotation;

                rb.isKinematic = false;
                rb.drag = 0f;
                rb.angularDrag = 0f;

                syncJoint = rb.gameObject.AddComponent<ConfigurableJoint>();
                syncJoint.axis = Vector3.zero;
                syncJoint.secondaryAxis = Vector3.zero;
                syncJoint.connectedBody = targetBody;
                syncJoint.autoConfigureConnectedAnchor = false;
                syncJoint.anchor = Vector3.zero;
                syncJoint.connectedAnchor = Vector3.zero;
                syncJoint.SetMotion(ConfigurableJointMotion.Limited);
                syncJoint.SetDrive(positionSpring * rb.mass, positionDamper * rb.mass, maximumForce * rb.mass);
                syncJoint.projectionMode = JointProjectionMode.PositionAndRotation;
                syncJoint.linearLimit = new SoftJointLimit() { limit = linearLimit };
                syncJoint.lowAngularXLimit = new SoftJointLimit() { limit = -5f };
                syncJoint.highAngularXLimit = syncJoint.angularZLimit = syncJoint.angularYLimit = new SoftJointLimit() { limit = 5f };
                syncJoint.projectionDistance = 0.005f;
                syncJoint.projectionAngle = 5f;
                syncJoint.enablePreprocessing = false;
            }
        }

        protected void JointCheck() {
            if (!IsOwner()) {
                if (rb && !syncJoint) ReCreateJoint();
            }
            else DestroyJoint();
        }

        protected void DestroyJoint() {
            if (syncJoint) {
                Destroy(syncJoint);
                syncJoint = null;
                if (TryGetRigidbody(out Rigidbody rb)) {
                    rb.drag = startDrag;
                    rb.angularDrag = startAngularDrag;
                }
            }
        }
    }
}