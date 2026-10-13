using UnityEngine;

using Entanglement.Extensions;

namespace Entanglement.Data
{
    /// <summary>
    /// Fusion-style proportional-derivative controller. Drives dynamic rigidbodies with
    /// <see cref="ForceMode.Acceleration"/> so mass does not explode forces. Clamps, world bounds,
    /// and derivative resets prevent the lag/crash failure mode of naive force sync.
    /// </summary>
    public class PDController
    {
        const float MaxForce = 5000f;
        const float MaxTorque = 1000f;

        const float PositionFrequency = 10f;
        const float PositionDamping = 3f;
        const float RotationFrequency = 100f;
        const float RotationDamping = 10f;

        const float WorldLimit = 50000f;
        const float SpeedLimit = 1000f;

        static float positionKp;
        static float positionKd;
        static float rotationKp;
        static float rotationKd;

        static float positionKsg;
        static float positionKdg;
        static float rotationKsg;
        static float rotationKdg;

        static float lastFixedDelta = 1f;
        static bool gainsReady;

        bool validPosition;
        bool validRotation;
        Vector3 lastVelocity;
        Vector3 lastAngularVelocity;
        Vector3 lastPosition;
        Quaternion lastRotation = Quaternion.identity;

        public Vector3 SavedForce { get; private set; }
        public Vector3 SavedTorque { get; private set; }

        public static void Initialize() {
            positionKp = CalculateKP(PositionFrequency);
            positionKd = CalculateKD(PositionFrequency, PositionDamping);
            rotationKp = CalculateKP(RotationFrequency);
            rotationKd = CalculateKD(RotationFrequency, RotationDamping);
            gainsReady = true;
            OnFixedUpdate();
        }

        public static void OnFixedUpdate() {
            if (!gainsReady)
                Initialize();

            float dt = Time.fixedDeltaTime;
            if (dt <= 1e-6f)
                return;
            if (Mathf.Abs(dt - lastFixedDelta) < 1e-8f && positionKsg != 0f)
                return;

            lastFixedDelta = dt;

            float pG = 1f / (1f + positionKd * dt + positionKp * dt * dt);
            positionKsg = positionKp * pG;
            positionKdg = (positionKd + positionKp * dt) * pG;

            float rG = 1f / (1f + rotationKd * dt + rotationKp * dt * dt);
            rotationKsg = rotationKp * rG;
            rotationKdg = (rotationKd + rotationKp * dt) * rG;
        }

        static float CalculateKP(float frequency) {
            return (6f * frequency) * (6f * frequency) * 0.25f;
        }

        static float CalculateKD(float frequency, float damping) {
            return 4.5f * frequency * damping;
        }

        public void ResetPosition() {
            validPosition = false;
            SavedForce = Vector3.zero;
        }

        public void ResetRotation() {
            validRotation = false;
            SavedTorque = Vector3.zero;
        }

        public void Reset() {
            ResetPosition();
            ResetRotation();
        }

        public static bool IsInBounds(Vector3 p) {
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
                return false;
            float sqr = p.x * p.x + p.y * p.y + p.z * p.z;
            return !float.IsInfinity(sqr) && sqr < WorldLimit * WorldLimit;
        }

        public static Vector3 LimitVelocity(Vector3 v) {
            float sqr = v.sqrMagnitude;
            if (float.IsNaN(sqr) || float.IsInfinity(sqr))
                return Vector3.zero;
            if (sqr <= SpeedLimit * SpeedLimit)
                return v;
            return v * (SpeedLimit / Mathf.Sqrt(sqr));
        }

        public Vector3 GetForce(Vector3 position, Vector3 velocity, Vector3 targetPos, Vector3 targetVel) {
            if (!IsInBounds(targetPos))
                return Vector3.zero;

            Vector3 limitedTargetVel = LimitVelocity(targetVel);

            if (!validPosition) {
                lastPosition = targetPos;
                lastVelocity = limitedTargetVel;
                validPosition = true;
            }

            Vector3 force = (lastPosition - position) * positionKsg + (lastVelocity - velocity) * positionKdg;
            force += (limitedTargetVel - lastVelocity) / lastFixedDelta;
            lastVelocity = limitedTargetVel;
            lastPosition = targetPos;

            SavedForce = Vector3.ClampMagnitude(force, MaxForce);
            return SavedForce;
        }

        public Vector3 GetTorque(Quaternion rotation, Vector3 angularVelocity, Quaternion targetRot, Vector3 targetAngVel) {
            if (!validRotation) {
                lastRotation = targetRot;
                lastAngularVelocity = targetAngVel;
                validRotation = true;
            }

            Quaternion q = lastRotation * Quaternion.Inverse(rotation);
            if (q.w < 0f) {
                q.x = -q.x;
                q.y = -q.y;
                q.z = -q.z;
                q.w = -q.w;
            }

            q.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f)
                axis = Vector3.up;
            else
                axis.Normalize();

            float angleRad = angleDeg * ((float)System.Math.PI / 180f);
            Vector3 torque = rotationKsg * axis * angleRad + rotationKdg * (lastAngularVelocity - angularVelocity);
            torque += (targetAngVel - lastAngularVelocity) / lastFixedDelta;
            lastAngularVelocity = targetAngVel;
            lastRotation = targetRot;

            SavedTorque = Vector3.ClampMagnitude(torque, MaxTorque);
            return SavedTorque;
        }

        /// <summary>Apply PD force/torque to a dynamic body. Skips kinematic bodies (Fusion rule).</summary>
        public void Drive(Rigidbody rb, Vector3 targetPos, Quaternion targetRot, Vector3 targetVel, Vector3 targetAngVel, bool applyTorque = true) {
            if (!rb || rb.isKinematic) {
                Reset();
                return;
            }

            Vector3 force = GetForce(rb.position, rb.velocity, targetPos, targetVel);
            rb.AddForce(force, ForceMode.Acceleration);

            if (applyTorque && (rb.constraints & RigidbodyConstraints.FreezeRotation) != RigidbodyConstraints.FreezeRotation) {
                Vector3 torque = GetTorque(rb.rotation, rb.angularVelocity, targetRot, targetAngVel);
                rb.AddTorque(torque, ForceMode.Acceleration);
            }
            else {
                ResetRotation();
            }
        }
    }
}
