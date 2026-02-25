using Entanglement.Network;
using MelonLoader;
using PuppetMasta;
using RootMotion.Dynamics;
using StressLevelZero.AI;
using System;
using UnityEngine;

namespace Entanglement.Objects
{
    [RegisterTypeInIl2Cpp]
    public class PuppetSyncable : Syncable
    {
        // Boneworks usually uses AIBrain for the core enemy logic
        public AIBrain aiBrain;
        public PuppetMaster puppetMaster;
        public Transform rootTransform;

        // Network send throttling and thresholds
        private const float SendInterval = 1f / 20f; // 20 Hz
        private const float PositionSendThreshold = 0.01f; // meters
        private const float RotationSendThresholdDeg = 0.5f; // degrees

        // Client-side smoothing and validation
        [SerializeField] private float smoothSpeed = 10f;
        [SerializeField] private float snapDistance = 3f; // meters
        [SerializeField] private float maxAllowedMagnitude = 10000f; // sanity clamp

        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private int targetAIState;

        private Vector3 lastSentPosition;
        private Quaternion lastSentRotation = Quaternion.identity;
        private int lastSentAIState;
        private float lastSendTime;

        private Rigidbody rootRigidbody;

        public PuppetSyncable(IntPtr intPtr) : base(intPtr) { }

        public void Setup(AIBrain brain, ushort objectId)
        {
            this.aiBrain = brain;
            this.puppetMaster = brain != null ? brain.GetComponentInChildren<PuppetMaster>() : null;
            this.rootTransform = brain != null ? brain.transform : null;
            this.rootRigidbody = this.rootTransform != null ? this.rootTransform.GetComponent<Rigidbody>() : null;
            this.objectId = objectId;
            this.isValid = (this.aiBrain != null) && (this.puppetMaster != null) && (this.rootTransform != null);

            if (!this.isValid)
            {
                MelonLogger.Warning($"PuppetSyncable invalid setup for objectId={objectId}: aiBrain={aiBrain != null}, puppetMaster={puppetMaster != null}, rootTransform={rootTransform != null}");
                return;
            }

            // Initialize state
            targetPosition = rootTransform.position;
            targetRotation = rootTransform.rotation;
            lastSentPosition = targetPosition;
            lastSentRotation = targetRotation;
            lastSentAIState = GetAIStateSafe();

            ObjectSync.RegisterSyncable(this, objectId);
        }

        // NPC ownership is typically server/host authoritative. Intentionally no-op.
        protected override void UpdateOwner(bool checkForMag = true) { }

        public override void SyncUpdate()
        {
            if (!IsOwner() || !ShouldSync())
                return;

            // Throttle send rate
            float now = Time.unscaledTime;
            if (now - lastSendTime < SendInterval)
                return;

            Vector3 pos = rootTransform.position;
            Quaternion rot = rootTransform.rotation;
            int state = GetAIStateSafe();

            // Validate
            if (!IsValidPose(pos, rot))
                return;

            bool posChanged = (Vector3.SqrMagnitude(pos - lastSentPosition) >= PositionSendThreshold * PositionSendThreshold);
            bool rotChanged = (Quaternion.Angle(rot, lastSentRotation) >= RotationSendThresholdDeg);
            bool stateChanged = (state != lastSentAIState);

            if (!(posChanged || rotChanged || stateChanged))
                return;

            PuppetSyncMessageData data = new PuppetSyncMessageData()
            {
                objectId = this.objectId,
                position = pos,
                rotation = Entanglement.Data.SimplifiedQuaternion.SimplifyQuat(rot),
                aiState = (byte)Mathf.Clamp(state, 0, 255)
            };

            NetworkMessage msg = NetworkMessage.CreateMessage(BuiltInMessageType.PuppetSync, data);
            if (msg != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, msg.GetBytes());

            lastSentPosition = pos;
            lastSentRotation = rot;
            lastSentAIState = state;
            lastSendTime = now;
        }

        public override bool ShouldSync()
        {
            if (aiBrain == null || puppetMaster == null || rootTransform == null) return false;
            var health = aiBrain.health;
            if (health == null) return true; // best effort if health isn't available
            return health.currentHealth > 0f;
        }

        public void ApplyState(Vector3 pos, Quaternion rot, byte aiState)
        {
            if (IsOwner()) return; // Don't let clients override the host's AI

            // Validate incoming data
            if (!IsValidPose(pos, rot))
                return;

            // Clamp extremes to avoid physics/transform explosions
            if (pos.sqrMagnitude > maxAllowedMagnitude * maxAllowedMagnitude)
            {
                MelonLogger.Warning($"PuppetSyncable[{objectId}] rejecting extreme position magnitude: {pos}");
                return;
            }

            targetPosition = pos;
            targetRotation = rot;
            targetAIState = aiState;

            // Optional: sync AI state to match host if safe to do so.
            // Intentionally conservative here to avoid fighting local behaviour controllers.
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            if (IsOwner() || aiBrain == null || rootTransform == null)
                return;

            // Determine smoothing factor (frame-rate independent)
            float t = 1f - Mathf.Exp(-smoothSpeed * Time.fixedDeltaTime);

            // Snap if too far
            float dist = Vector3.Distance(rootTransform.position, targetPosition);
            if (dist > snapDistance)
            {
                if (rootRigidbody != null)
                {
                    rootRigidbody.position = targetPosition;
                    rootRigidbody.rotation = targetRotation;
                    rootRigidbody.velocity = Vector3.zero;
                    rootRigidbody.angularVelocity = Vector3.zero;
                }
                else
                {
                    rootTransform.SetPositionAndRotation(targetPosition, targetRotation);
                }
                return;
            }

            // Smoothly move towards target
            Vector3 newPos = Vector3.Lerp(rootTransform.position, targetPosition, t);
            Quaternion newRot = Quaternion.Slerp(rootTransform.rotation, targetRotation, t);

            if (rootRigidbody != null)
            {
                rootRigidbody.MovePosition(newPos);
                rootRigidbody.MoveRotation(newRot);
            }
            else
            {
                rootTransform.SetPositionAndRotation(newPos, newRot);
            }
        }

        private int GetAIStateSafe()
        {
            try
            {
                return aiBrain != null && aiBrain.behaviour != null ? aiBrain.behaviour.state : 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"PuppetSyncable[{objectId}] failed to read AI state: {ex.Message}");
                return 0;
            }
        }

        private static bool IsValidPose(in Vector3 pos, in Quaternion rot)
        {
            return IsFinite(pos.x) && IsFinite(pos.y) && IsFinite(pos.z)
                && IsFinite(rot.x) && IsFinite(rot.y) && IsFinite(rot.z) && IsFinite(rot.w);
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}