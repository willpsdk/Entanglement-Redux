using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Network;
using Entanglement.Patching;
using Entanglement.Representation;
using MelonLoader;
using StressLevelZero.Interaction;
using StressLevelZero.Pool;
using StressLevelZero.Props;
using StressLevelZero.Props.Weapons;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Utilties;
using static Entanglement.Network.TransformCreateMessageHandler;

namespace Entanglement.Objects
{
    [RegisterTypeInIl2Cpp]
    public partial class TransformSyncable : Syncable
    {
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

        public const float positionSpring = 50000f;
        public const float positionDamper = 5000f;
        public const float maximumForce = 80000f;
        public const float linearLimit = 0.02f;

        // FIX: Increase to 60Hz for smooth object sync matching player sync (1/60 = 0.0167 seconds between syncs)
        private float lastSyncTime = 0f;
        private const float OBJECT_SYNC_INTERVAL = 1f / 60f;

        protected float timeOfDisable = 0f;

        private TransformSyncMessageData _cachedSyncData;

        public override void Cleanup()
        {
            DestroyJoint();
            if (targetGo) Destroy(targetGo);

            base.Cleanup();
        }

        public override void SyncUpdate()
        {
            // Safety check - ensure we have an active node
            if (Node.activeNode == null)
                return;

            // Rate limit object syncs to configured interval
            lastSyncTime += Time.fixedDeltaTime;
            if (lastSyncTime < OBJECT_SYNC_INTERVAL)
                return;

            lastSyncTime -= OBJECT_SYNC_INTERVAL;
            if (lastSyncTime > OBJECT_SYNC_INTERVAL)
                lastSyncTime = 0f;

            if (_cachedSyncData == null) _cachedSyncData = new TransformSyncMessageData();

            _cachedSyncData.objectId = objectId;
            _cachedSyncData.simplifiedTransform = new SimplifiedTransform(transform, rb);

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSync, _cachedSyncData);
            if (message != null)
            {
                Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
            }

            if (targetGo)
            {
                targetGo.transform.position = transform.position;
                targetGo.transform.rotation = transform.rotation;
            }

            UpdateStoredPositions();
        }

        public override bool ShouldSync()
        {
            if (rb && rb.IsSleeping() && !HasChangedPositions()) return false;
            return HasChangedPositions();
        }

        public bool HasChangedPositions() => (transform.position - lastPosition).sqrMagnitude > 0.001f || Quaternion.Angle(transform.rotation, lastRotation) > 0.05f;

        protected override void UpdateOwner(bool checkForMag = true)
        {
            if (lastOwner == SteamIntegration.currentUser.m_SteamID) objectHealth = GetHealth();

            if (!IsOwner()) SetHealth(float.PositiveInfinity);
            else SetHealth(objectHealth);

            if (TryGetRigidbody(out Rigidbody rb))
            {
                if (PlayerRepresentation.representations.ContainsKey(lastOwner))
                    PlayerRepresentation.representations[lastOwner].IgnoreCollision(rb, false);

                if (PlayerRepresentation.representations.ContainsKey(staleOwner))
                    PlayerRepresentation.representations[staleOwner].IgnoreCollision(rb, true);

                try
                {
                    if (checkForMag && _CachedGun)
                    {
                        MagazineSocket magSocket = _CachedGun.magazineSocket;
                        if (magSocket && magSocket._magazinePlug)
                        {
                            TransformSyncable _CachedTransform = cache.GetOrAdd(magSocket._magazinePlug.magazine.gameObject);
                            if (_CachedTransform) _CachedTransform.ForceOwner(staleOwner, false);
                        }
                    }
                }
                catch { }
            }

            if (_CachedPlug && lastOwner != staleOwner && staleOwner == SteamIntegration.currentUser.m_SteamID)
            {
                Socket plugSocket = _CachedPlug._lastSocket;
                if (_CachedPlug.InGun())
                {
                    _CachedPlug.ForceEject();
                    _CachedPlug.InsertPlug(plugSocket);
                }
            }
        }

        protected void OnEnable()
        {
            DestroyJoint();

            // When an object comes back from a holster/slot, immediately reclaim
            // ownership if we were the last owner so the remote side doesn't fight.
            if (isValid && staleOwner == SteamIntegration.currentUser.m_SteamID)
            {
                SendEnqueue();
            }
        }

        protected void OnDisable()
        {
            timeOfDisable = Time.realtimeSinceStartup;
            DestroyJoint();

            // Do NOT dequeue here. Holstering disables the object but the player still
            // owns it. Dequeue only happens on explicit hand detach (GripDetachPatch)
            // when the other hand isn't also holding the chain.
        }

        protected void UpdateStoredPositions()
        {
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (!SteamIntegration.hasLobby) return;
            if (!collision.rigidbody || !IsOwner()) return;

            // Only sync if THIS object is currently held by a player hand.
            // This prevents cascade: a held gun bumps a box (sync), but that box
            // bumping a second box does NOT trigger sync because the box isn't held.
            if (rb == null || _CachedBodies == null || !_CachedBodies.IsHolding()) return;

            if (collision.collider.gameObject.IsBlacklisted()) return;

            TransformSyncable existingSync = cache.Get(collision.rigidbody.gameObject);
            if (existingSync) return;

            // Ignore NPC/AI rigs and non-interactable animated objects
            if (!ObjectSync.IsScenePhysicsSyncCandidate(collision.rigidbody)) return;

            Transform targetObj = collision.rigidbody.transform;
            ulong ownerId = SteamIntegration.currentUser.m_SteamID;

            ObjectSync.GetPooleeData(targetObj, out Rigidbody[] rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime);

            if (rigidbodies == null || rigidbodies.Length == 0)
                rigidbodies = targetObj.GetJointedBodies();

            if (rigidbodies == null) return;

            for (int i = 0; i < rigidbodies.Length; i++)
            {
                Rigidbody rBody = rigidbodies[i];
                if (rBody == null || rBody.isKinematic) continue;

                TransformSyncable syncObj = cache.Get(rBody.gameObject);
                if (syncObj) continue;

                ushort? objectId = null;
                ushort callbackIndex = 0;

                if (Server.instance != null)
                    objectId = ObjectSync.GetNextObjectId();

                Syncable syncable = CreateSync(ownerId, rBody, objectId);
                if (syncable == null) continue;

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

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            if (isValid)
            {
                JointCheck();
                if (!IsOwner()) SetHealth(float.PositiveInfinity);
            }
        }

        protected bool TryGetRigidbody(out Rigidbody rigidbody)
        {
            if (rb)
            {
                rigidbody = rb;
                return true;
            }
            else
            {
                rigidbody = gameObject.GetComponent<Rigidbody>();
                rb = rigidbody;
                return rigidbody != null;
            }
        }

        public static Syncable CreateSync(ulong owner, Rigidbody rigidbody = null, ushort? objectId = null)
        {
            if (!rigidbody) return null;

            GameObject go = rigidbody.gameObject;
            TransformSyncable existingSync = cache.GetOrAdd(go);
            if (existingSync)
            {
                existingSync.ForceOwner(owner);
                if (objectId != null) ObjectSync.MoveSyncable(existingSync, objectId.Value);
                return existingSync;
            }

            TransformSyncable syncObj = go.AddComponent<TransformSyncable>();
            cache.Add(go, syncObj);

            syncObj._CachedPlug = go.GetComponentInChildren<MagazinePlug>(true);
            syncObj._CachedGun = go.GetComponentInChildren<Gun>(true);
            syncObj._CachedDestructable = go.GetComponentInChildren<ObjectDestructable>(true);
            syncObj._CachedHealth = go.GetComponentInChildren<Prop_Health>(true);

            if (syncObj._CachedDestructable) DestructCache.Add(syncObj._CachedDestructable.gameObject, syncObj);
            if (syncObj._CachedHealth) DestructCache.Add(syncObj._CachedHealth.gameObject, syncObj);

            syncObj.events = syncObj.GetComponentsInChildren<GripEvents>(true);
            syncObj.SetupEvents();

            syncObj.rb = rigidbody;
            syncObj.startDrag = rigidbody.drag;
            syncObj.startAngularDrag = rigidbody.angularDrag;
            syncObj._CachedBodies = syncObj.transform.GetJointedBodies();

            syncObj.ForceOwner(owner);

            if (objectId != null)
            {
                ushort id = objectId.Value;
                syncObj.objectId = id;
                syncObj.isValid = true;
                ObjectSync.RegisterSyncable(syncObj, id);
            }
            return syncObj;
        }

        public override void SendEnqueue()
        {
            if (isValid) OnValidEnqueue();
            else MelonCoroutines.Start(WaitUntilValid(OnValidEnqueue));
        }

        public override void SendDequeue()
        {
            if (isValid) OnValidDequeue();
            else MelonCoroutines.Start(WaitUntilValid(OnValidDequeue));
        }

        public void OnValidEnqueue()
        {
            ulong userId = SteamIntegration.currentUser.m_SteamID;
            if (ownerQueue.Contains(userId)) return;

            // Optimistic local ownership handoff to avoid one-roundtrip freeze when grabbing objects as client.
            EnqueueOwner(userId);

            TransformQueueMessageData queueData = new TransformQueueMessageData() { userId = userId, objectId = objectId, isAdd = true };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformQueue, queueData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            MelonCoroutines.Start(CoOwnershipResyncPulse());
        }

        public void OnValidDequeue()
        {
            ulong userId = SteamIntegration.currentUser.m_SteamID;
            if (!ownerQueue.Contains(userId)) return;

            // Send a reliable final state before relinquishing ownership so throw velocity is preserved.
            SendOwnershipHandoffSnapshot();

            DequeueOwner(userId);

            TransformQueueMessageData queueData = new TransformQueueMessageData() { userId = userId, objectId = objectId, isAdd = false };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformQueue, queueData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            MelonCoroutines.Start(CoOwnershipResyncPulse());
        }

        private void SendOwnershipHandoffSnapshot()
        {
            if (!isValid || Node.activeNode == null || !IsOwner())
                return;

            TransformSyncMessageData handoffData = new TransformSyncMessageData()
            {
                objectId = objectId,
                simplifiedTransform = new SimplifiedTransform(transform, rb)
            };

            NetworkMessage handoffMessage = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSync, handoffData);
            if (handoffMessage != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, handoffMessage.GetBytes());
        }

        private IEnumerator CoOwnershipResyncPulse()
        {
            // Small burst of reliable transform syncs to settle contested grabs/holster movement.
            for (int i = 0; i < 8; i++)
            {
                if (!isValid || Node.activeNode == null)
                    yield break;

                if (IsOwner())
                {
                    TransformSyncMessageData data = new TransformSyncMessageData
                    {
                        objectId = objectId,
                        simplifiedTransform = new SimplifiedTransform(transform, rb)
                    };

                    NetworkMessage msg = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSync, data);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msg.GetBytes());
                }

                yield return null;
            }
        }

        public IEnumerator WaitUntilValid(Action onFinish)
        {
            while (!isValid) yield return null;
            onFinish?.Invoke();
        }

        public void ApplyTransform(SimplifiedTransform simplifiedTransform)
        {
            if (SteamIntegration.currentUser.m_SteamID == staleOwner || (_CachedPlug && _CachedPlug.EnteringOrInside())) return;

            if (rb && simplifiedTransform.isSleeping)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.Sleep();
            }

            if (targetBody)
            {
                targetBody.transform.position = simplifiedTransform.position;
                targetBody.transform.rotation = simplifiedTransform.rotation.ExpandQuat();

                if (!simplifiedTransform.isSleeping)
                {
                    targetBody.velocity = simplifiedTransform.velocity;
                    targetBody.angularVelocity = simplifiedTransform.angularVelocity;
                }
            }

            if (!rb)
            {
                simplifiedTransform.Apply(transform);
            }
            else if (!syncJoint && !simplifiedTransform.isSleeping)
            {
                rb.velocity = simplifiedTransform.velocity;
                rb.angularVelocity = simplifiedTransform.angularVelocity;
            }

            if (!transform.gameObject.activeInHierarchy && Mathf.Abs(Time.realtimeSinceStartup - timeOfDisable) >= 2f)
                transform.ForceActivate();
        }

        protected void ReCreateJoint()
        {
            if (targetGo) Destroy(targetGo);

            targetGo = new GameObject($"TransformSyncFollow {transform.name}, {transform.GetInstanceID()}");

            bool hasBody = TryGetRigidbody(out Rigidbody rb);

            targetBody = targetGo.AddComponent<Rigidbody>();
            targetBody.isKinematic = true;
            targetBody.transform.position = transform.position;
            targetBody.transform.rotation = transform.rotation;

            DestroyJoint();

            if (hasBody)
            {
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

        protected void JointCheck()
        {
            if (!IsOwner())
            {
                if (rb && !syncJoint) ReCreateJoint();
            }
            else DestroyJoint();
        }

        protected void DestroyJoint()
        {
            if (syncJoint)
            {
                Destroy(syncJoint);
                syncJoint = null;
                if (TryGetRigidbody(out Rigidbody rb))
                {
                    rb.drag = startDrag;
                    rb.angularDrag = startAngularDrag;
                }
            }
        }
    }
}