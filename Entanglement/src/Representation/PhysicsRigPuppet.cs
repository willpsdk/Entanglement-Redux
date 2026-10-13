using System;

using UnityEngine;

using Entanglement.Data;

namespace Entanglement.Representation
{
    /// <summary>
    /// Full Boneworks physics puppet (Fusion RigManager ideology): dynamic PD pelvis, hands, and
    /// feet in world space. Visual Ford is kinematic IK and soft-follows the pelvis.
    /// </summary>
    public sealed class PhysicsRigPuppet
    {
        public Rigidbody Pelvis { get; private set; }
        public Rigidbody LeftHand { get; private set; }
        public Rigidbody RightHand { get; private set; }
        public Rigidbody LeftFoot { get; private set; }
        public Rigidbody RightFoot { get; private set; }
        public Collider HeadCollider { get; private set; }
        public Collider TorsoCollider { get; private set; }
        public GameObject Root { get; private set; }

        readonly PDController pelvisPD = new PDController();
        readonly PDController leftHandPD = new PDController();
        readonly PDController rightHandPD = new PDController();
        readonly PDController leftFootPD = new PDController();
        readonly PDController rightFootPD = new PDController();

        const float FreeAccelCap = 120f;
        const float ContactAccelCap = 36f;
        const float LimbAccelCap = 180f;
        const float SpeedLimit = 12f;
        const float LimbSpeedLimit = 18f;
        const float MaxDownAccel = 19.6f;
        const float SnapDistanceSqr = 4f;

        public bool IsValid => Root && Pelvis && LeftHand && RightHand;

        public void Build(GameObject host, float mass, Vector3 skullOffset, float headRadius, int layer) {
            Root = host;
            host.layer = layer;

            Pelvis = ConfigureBody(host, mass, freezeRotation: true);

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

            // World-space limbs (not parented) so PD targets are absolute like Fusion's physics rig.
            LeftHand = SpawnLimb("PhysHand.L", host.transform.position, mass * 0.08f, 0.07f, layer);
            RightHand = SpawnLimb("PhysHand.R", host.transform.position, mass * 0.08f, 0.07f, layer);
            LeftFoot = SpawnLimb("PhysFoot.L", host.transform.position, mass * 0.1f, 0.08f, layer);
            RightFoot = SpawnLimb("PhysFoot.R", host.transform.position, mass * 0.1f, 0.08f, layer);

            IgnoreSelfCollisions();
        }

        static Rigidbody ConfigureBody(GameObject go, float mass, bool freezeRotation) {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (!rb)
                rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.mass = mass;
            rb.drag = 0.5f;
            rb.angularDrag = 0.5f;
            rb.constraints = freezeRotation ? RigidbodyConstraints.FreezeRotation : RigidbodyConstraints.None;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return rb;
        }

        static Rigidbody SpawnLimb(string name, Vector3 at, float mass, float radius, int layer) {
            GameObject go = new GameObject(name);
            go.layer = layer;
            go.transform.position = at;
            Rigidbody rb = ConfigureBody(go, Mathf.Clamp(mass, 1.2f, 8f), freezeRotation: true);
            rb.drag = 1f;
            rb.angularDrag = 1f;
            SphereCollider col = go.AddComponent<SphereCollider>();
            col.isTrigger = false;
            col.radius = radius;
            return rb;
        }

        void IgnoreSelfCollisions() {
            Collider[] parts = {
                HeadCollider, TorsoCollider,
                LeftHand ? LeftHand.GetComponent<Collider>() : null,
                RightHand ? RightHand.GetComponent<Collider>() : null,
                LeftFoot ? LeftFoot.GetComponent<Collider>() : null,
                RightFoot ? RightFoot.GetComponent<Collider>() : null,
            };
            for (int i = 0; i < parts.Length; i++) {
                if (!parts[i])
                    continue;
                for (int j = i + 1; j < parts.Length; j++) {
                    if (!parts[j])
                        continue;
                    try { Physics.IgnoreCollision(parts[i], parts[j], true); }
                    catch { }
                }
            }
        }

        public void Drive(
            Vector3 pelvisTarget, Quaternion pelvisRot, Vector3 pelvisVel,
            Vector3 leftHandTarget, Vector3 rightHandTarget,
            Vector3 leftFootTarget, Vector3 rightFootTarget,
            bool softContact, bool parked)
        {
            if (!IsValid)
                return;

            if (parked) {
                ParkAll();
                return;
            }

            float pelvisCap = softContact ? ContactAccelCap : FreeAccelCap;
            DriveBody(Pelvis, pelvisPD, pelvisTarget, pelvisRot, pelvisVel, pelvisCap, SpeedLimit, true);
            DriveBody(LeftHand, leftHandPD, leftHandTarget, Quaternion.identity, Vector3.zero, LimbAccelCap, LimbSpeedLimit, false);
            DriveBody(RightHand, rightHandPD, rightHandTarget, Quaternion.identity, Vector3.zero, LimbAccelCap, LimbSpeedLimit, false);
            DriveBody(LeftFoot, leftFootPD, leftFootTarget, Quaternion.identity, Vector3.zero, LimbAccelCap, LimbSpeedLimit, false);
            DriveBody(RightFoot, rightFootPD, rightFootTarget, Quaternion.identity, Vector3.zero, LimbAccelCap, LimbSpeedLimit, false);
        }

        /// <summary>Soft-follow visual Ford root to the physical pelvis (IK bones stay relative).</summary>
        public void FollowVisual(Transform visualRoot, float blend = 0.35f) {
            if (!Pelvis || !visualRoot)
                return;
            Vector3 at = Pelvis.position;
            Quaternion yaw = Quaternion.Euler(0f, Pelvis.rotation.eulerAngles.y, 0f);
            visualRoot.position = Vector3.Lerp(visualRoot.position, at, blend);
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, yaw, blend);
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

        void ParkAll() {
            Park(Pelvis);
            Park(LeftHand);
            Park(RightHand);
            Park(LeftFoot);
            Park(RightFoot);
            pelvisPD.Reset();
            leftHandPD.Reset();
            rightHandPD.Reset();
            leftFootPD.Reset();
            rightFootPD.Reset();
        }

        static void Park(Rigidbody body) {
            if (!body)
                return;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        public void Destroy() {
            ParkAll();
            DestroyLimb(LeftHand);
            DestroyLimb(RightHand);
            DestroyLimb(LeftFoot);
            DestroyLimb(RightFoot);
            Pelvis = null;
            LeftHand = null;
            RightHand = null;
            LeftFoot = null;
            RightFoot = null;
            HeadCollider = null;
            TorsoCollider = null;
            Root = null;
        }

        static void DestroyLimb(Rigidbody rb) {
            if (!rb)
                return;
            try { UnityEngine.Object.Destroy(rb.gameObject); }
            catch { }
        }
    }
}
