using System;

using UnityEngine;

using Entanglement.Data;

namespace Entanglement.Representation
{
    /// <summary>
    /// Boneworks stand-in for Fusion's RigManager physics puppet: multiple dynamic PD-driven
    /// segments (pelvis/torso, hands) instead of a single Ford + capsule. Visual Ford stays
    /// kinematic for IK; physical presence is these bodies.
    /// </summary>
    public sealed class PhysicsRigPuppet
    {
        public Rigidbody Pelvis { get; private set; }
        public Rigidbody LeftHand { get; private set; }
        public Rigidbody RightHand { get; private set; }
        public Collider HeadCollider { get; private set; }
        public Collider TorsoCollider { get; private set; }
        public GameObject Root { get; private set; }

        readonly PDController pelvisPD = new PDController();
        readonly PDController leftHandPD = new PDController();
        readonly PDController rightHandPD = new PDController();

        const float FreeAccelCap = 120f;
        const float ContactAccelCap = 36f;
        const float HandAccelCap = 180f;
        const float SpeedLimit = 12f;
        const float HandSpeedLimit = 18f;
        const float MaxDownAccel = 19.6f;
        const float SnapDistanceSqr = 4f;

        public bool IsValid => Root && Pelvis;

        public void Build(GameObject host, float mass, Vector3 skullOffset, float headRadius, int layer) {
            Root = host;
            host.layer = layer;

            Pelvis = host.GetComponent<Rigidbody>();
            if (!Pelvis)
                Pelvis = host.AddComponent<Rigidbody>();

            Pelvis.isKinematic = false;
            Pelvis.useGravity = false;
            Pelvis.mass = mass;
            Pelvis.drag = 0.5f;
            Pelvis.angularDrag = 0.5f;
            Pelvis.constraints = RigidbodyConstraints.FreezeRotation;
            Pelvis.detectCollisions = true;
            Pelvis.interpolation = RigidbodyInterpolation.Interpolate;
            Pelvis.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            SphereCollider head = host.GetComponent<SphereCollider>();
            if (!head)
                head = host.AddComponent<SphereCollider>();
            head.isTrigger = false;
            head.radius = headRadius;
            head.center = skullOffset;
            HeadCollider = head;

            CapsuleCollider torso = host.GetComponent<CapsuleCollider>();
            if (!torso)
                torso = host.AddComponent<CapsuleCollider>();
            torso.isTrigger = false;
            torso.radius = 0.18f;
            torso.height = 1.3f;
            torso.center = new Vector3(0f, -0.85f, skullOffset.z);
            TorsoCollider = torso;

            LeftHand = EnsureHandBody(host.transform, "PhysHand.L", mass * 0.08f);
            RightHand = EnsureHandBody(host.transform, "PhysHand.R", mass * 0.08f);
        }

        static Rigidbody EnsureHandBody(Transform parent, string name, float mass) {
            Transform existing = parent.Find(name);
            GameObject go = existing ? existing.gameObject : new GameObject(name);
            if (!existing)
                go.transform.SetParent(parent, false);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (!rb)
                rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.mass = Mathf.Clamp(mass, 1.5f, 6f);
            rb.drag = 1f;
            rb.angularDrag = 1f;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            SphereCollider col = go.GetComponent<SphereCollider>();
            if (!col)
                col = go.AddComponent<SphereCollider>();
            col.isTrigger = false;
            col.radius = 0.07f;
            return rb;
        }

        public void Drive(
            Vector3 pelvisTarget, Quaternion pelvisRot, Vector3 pelvisVel,
            Vector3 leftHandTarget, Vector3 rightHandTarget,
            bool softContact, bool parked)
        {
            if (!IsValid)
                return;

            if (parked) {
                Park(Pelvis);
                Park(LeftHand);
                Park(RightHand);
                pelvisPD.Reset();
                leftHandPD.Reset();
                rightHandPD.Reset();
                return;
            }

            DriveBody(Pelvis, pelvisPD, pelvisTarget, pelvisRot, pelvisVel,
                softContact ? ContactAccelCap : FreeAccelCap, SpeedLimit, true);

            if (LeftHand)
                DriveBody(LeftHand, leftHandPD, leftHandTarget, Quaternion.identity, Vector3.zero,
                    HandAccelCap, HandSpeedLimit, false);
            if (RightHand)
                DriveBody(RightHand, rightHandPD, rightHandTarget, Quaternion.identity, Vector3.zero,
                    HandAccelCap, HandSpeedLimit, false);
        }

        void DriveBody(Rigidbody body, PDController pd, Vector3 target, Quaternion rot,
            Vector3 targetVel, float accelCap, float speedLimit, bool yawOnly)
        {
            if (!body)
                return;
            if (body.isKinematic)
                body.isKinematic = false;

            Vector3 error = target - body.position;
            if (error.sqrMagnitude > SnapDistanceSqr) {
                body.position = target;
                if (yawOnly)
                    body.rotation = rot;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                pd.Reset();
                return;
            }

            Vector3 force = pd.GetForce(body.position, body.velocity, target, targetVel);
            force = Vector3.ClampMagnitude(force, accelCap);
            if (force.y < -MaxDownAccel)
                force.y = -MaxDownAccel;
            body.AddForce(force, ForceMode.Acceleration);

            if (yawOnly)
                body.MoveRotation(rot);

            if (body.velocity.sqrMagnitude > speedLimit * speedLimit)
                body.velocity = body.velocity.normalized * speedLimit;
        }

        static void Park(Rigidbody body) {
            if (!body)
                return;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        public void Destroy() {
            pelvisPD.Reset();
            leftHandPD.Reset();
            rightHandPD.Reset();
            Pelvis = null;
            LeftHand = null;
            RightHand = null;
            HeadCollider = null;
            TorsoCollider = null;
            Root = null;
        }
    }
}
