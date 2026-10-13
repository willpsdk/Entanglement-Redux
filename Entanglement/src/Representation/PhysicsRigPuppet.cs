using System;

using UnityEngine;

using StressLevelZero.Combat;

using Entanglement.Data;

namespace Entanglement.Representation
{
    /// <summary>
    /// Boneworks physics body for a remote player — Fusion RigManager/physicsRig equivalent.
    /// Dynamic PD segments in world space; visual Ford stays kinematic IK and soft-follows pelvis.
    /// </summary>
    public sealed class PhysicsRigPuppet
    {
        public Rigidbody Pelvis { get; private set; }
        public Rigidbody Chest { get; private set; }
        public Rigidbody LeftHand { get; private set; }
        public Rigidbody RightHand { get; private set; }
        public Rigidbody LeftFoot { get; private set; }
        public Rigidbody RightFoot { get; private set; }
        public Collider HeadCollider { get; private set; }
        public Collider TorsoCollider { get; private set; }
        public GameObject Root { get; private set; }

        readonly PDController pelvisPD = new PDController();
        readonly PDController chestPD = new PDController();
        readonly PDController leftHandPD = new PDController();
        readonly PDController rightHandPD = new PDController();
        readonly PDController leftFootPD = new PDController();
        readonly PDController rightFootPD = new PDController();

        const float FreeAccelCap = 120f;
        const float ContactAccelCap = 36f;
        const float LimbAccelCap = 180f;
        const float ChestAccelCap = 100f;
        const float SpeedLimit = 12f;
        const float LimbSpeedLimit = 18f;
        const float MaxDownAccel = 19.6f;
        const float SnapDistanceSqr = 4f;

        public bool IsValid => Root && Pelvis && Chest && LeftHand && RightHand && LeftFoot && RightFoot;

        public struct PoseTargets
        {
            public Vector3 pelvis;
            public Quaternion pelvisRot;
            public Vector3 pelvisVel;
            public Vector3 chest;
            public Vector3 leftHand;
            public Vector3 rightHand;
            public Vector3 leftFoot;
            public Vector3 rightFoot;
        }

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
            torso.height = 1.05f;
            torso.center = new Vector3(0f, -0.55f, skullOffset.z);
            TorsoCollider = torso;

            Chest = SpawnLimb("PhysChest", host.transform.position, mass * 0.35f, 0.16f, layer, capsule: true);
            LeftHand = SpawnLimb("PhysHand.L", host.transform.position, mass * 0.08f, 0.07f, layer);
            RightHand = SpawnLimb("PhysHand.R", host.transform.position, mass * 0.08f, 0.07f, layer);
            LeftFoot = SpawnLimb("PhysFoot.L", host.transform.position, mass * 0.1f, 0.08f, layer);
            RightFoot = SpawnLimb("PhysFoot.R", host.transform.position, mass * 0.1f, 0.08f, layer);

            MarkHittable(host);
            MarkHittable(Chest.gameObject);
            MarkHittable(LeftHand.gameObject);
            MarkHittable(RightHand.gameObject);
            MarkHittable(LeftFoot.gameObject);
            MarkHittable(RightFoot.gameObject);

            IgnoreSelfCollisions();
        }

        static void MarkHittable(GameObject go) {
            if (!go)
                return;
            ImpactProperties impact = go.GetComponent<ImpactProperties>();
            if (!impact)
                impact = go.AddComponent<ImpactProperties>();
            impact.FireResistance = 1f;
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

        static Rigidbody SpawnLimb(string name, Vector3 at, float mass, float radius, int layer, bool capsule = false) {
            GameObject go = new GameObject(name);
            go.layer = layer;
            go.transform.position = at;
            Rigidbody rb = ConfigureBody(go, Mathf.Clamp(mass, 1.2f, 30f), freezeRotation: true);
            rb.drag = 1f;
            rb.angularDrag = 1f;
            if (capsule) {
                CapsuleCollider col = go.AddComponent<CapsuleCollider>();
                col.isTrigger = false;
                col.radius = radius;
                col.height = radius * 3.2f;
                col.direction = 1;
            }
            else {
                SphereCollider col = go.AddComponent<SphereCollider>();
                col.isTrigger = false;
                col.radius = radius;
            }
            return rb;
        }

        void IgnoreSelfCollisions() {
            Collider[] parts = {
                HeadCollider, TorsoCollider,
                Chest ? Chest.GetComponent<Collider>() : null,
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

        public void Drive(PoseTargets pose, bool softContact, bool parked) {
            if (!IsValid)
                return;

            if (parked) {
                ParkAll();
                return;
            }

            float pelvisCap = softContact ? ContactAccelCap : FreeAccelCap;
            DriveBody(Pelvis, pelvisPD, pose.pelvis, pose.pelvisRot, pose.pelvisVel, pelvisCap, SpeedLimit, true);
            DriveBody(Chest, chestPD, pose.chest, pose.pelvisRot, Vector3.zero, ChestAccelCap, SpeedLimit, true);
            DriveBody(LeftHand, leftHandPD, pose.leftHand, Quaternion.identity, Vector3.zero, LimbAccelCap, LimbSpeedLimit, false);
            DriveBody(RightHand, rightHandPD, pose.rightHand, Quaternion.identity, Vector3.zero, LimbAccelCap, LimbSpeedLimit, false);
            DriveBody(LeftFoot, leftFootPD, pose.leftFoot, Quaternion.identity, Vector3.zero, LimbAccelCap, LimbSpeedLimit, false);
            DriveBody(RightFoot, rightFootPD, pose.rightFoot, Quaternion.identity, Vector3.zero, LimbAccelCap, LimbSpeedLimit, false);
        }

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

            if ((target - body.position).sqrMagnitude > SnapDistanceSqr) {
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
            Park(Chest);
            Park(LeftHand);
            Park(RightHand);
            Park(LeftFoot);
            Park(RightFoot);
            pelvisPD.Reset();
            chestPD.Reset();
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
            DestroyLimb(Chest);
            DestroyLimb(LeftHand);
            DestroyLimb(RightHand);
            DestroyLimb(LeftFoot);
            DestroyLimb(RightFoot);
            Pelvis = null;
            Chest = null;
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
