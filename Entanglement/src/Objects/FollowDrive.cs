using System;

using UnityEngine;

using Entanglement.Extensions;

namespace Entanglement.Objects
{
    /// <summary>
    /// Steers a body that another machine owns toward that machine's pose with forces, so here it
    /// stays a normal physics body: walls stop it, it rests on things, and it shoves (and is shoved
    /// by) whatever it touches. Every physics step it is asked to close a share of the gap to the
    /// target and to move at the target's speed. That is handed to the body as an acceleration,
    /// so a heavy body and a light one follow the same way.
    /// </summary>
    public static class FollowDrive
    {
        // Share of the remaining gap closed per physics step. A quarter settles in about four steps
        // (under 50 ms at 90 Hz) and never overshoots.
        const float PositionShare = 0.25f;
        const float RotationShare = 0.3f;

        // Caps, so a wild target can't fling the body. Past TransformSyncable.snapDistance it is
        // moved there instead of steered.
        const float MaxAcceleration = 1200f;          // m/s²
        const float MaxAngularAcceleration = 1500f;   // rad/s²

        // Past this the target is garbage (NaN, or thrown out of the world) and is ignored.
        const float WorldLimit = 50000f;

        /// <summary>
        /// One physics step of steering a body toward a pose. The body's current state is passed in,
        /// already read, so it isn't fetched twice. Gravity is cancelled for a body that uses it: the
        /// pose it was given already has gravity in it, so it would sag below where its owner has it.
        /// </summary>
        public static void Steer(Rigidbody rb, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity,
            Vector3 target, Quaternion targetRotation, Vector3 targetVelocity, Vector3 targetAngularVelocity, float dt) {
            Vector3 accel = Acceleration(position, velocity, target, targetVelocity, dt);
            if (rb.useGravity)
                accel = FastMath.Sub(accel, Physics.gravity);
            rb.AddForce(accel, ForceMode.Acceleration);

            if ((rb.constraints & RigidbodyConstraints.FreezeRotation) != RigidbodyConstraints.FreezeRotation)
                rb.AddTorque(AngularAcceleration(rotation, angularVelocity, targetRotation, targetAngularVelocity, dt), ForceMode.Acceleration);
        }

        /// <summary>A position we can steer toward.</summary>
        public static bool Sane(Vector3 p) {
            float sqr = p.x * p.x + p.y * p.y + p.z * p.z;
            return !float.IsNaN(sqr) && !float.IsInfinity(sqr) && sqr < WorldLimit * WorldLimit;
        }

        /// <summary>The acceleration that moves a body at <paramref name="position"/> toward the target this step.
        /// Gravity is not included; the caller cancels it for bodies that use it.</summary>
        public static Vector3 Acceleration(Vector3 position, Vector3 velocity, Vector3 target, Vector3 targetVelocity, float dt) {
            if (dt <= 0f)
                return default(Vector3);
            float gain = PositionShare / dt;
            float inv = 1f / dt;
            Vector3 a = FastMath.V(
                (targetVelocity.x + (target.x - position.x) * gain - velocity.x) * inv,
                (targetVelocity.y + (target.y - position.y) * gain - velocity.y) * inv,
                (targetVelocity.z + (target.z - position.z) * gain - velocity.z) * inv);
            return Cap(a, MaxAcceleration);
        }

        /// <summary>The angular acceleration that turns a body toward the target rotation this step.</summary>
        public static Vector3 AngularAcceleration(Quaternion rotation, Vector3 angularVelocity, Quaternion target, Vector3 targetAngularVelocity, float dt) {
            if (dt <= 0f)
                return default(Vector3);

            // The turn still to make, as an axis scaled by its angle in radians, the short way round.
            Quaternion turn = FastMath.Mul(target, FastMath.Inverse(rotation));
            if (turn.w < 0f) {
                turn.x = -turn.x;
                turn.y = -turn.y;
                turn.z = -turn.z;
                turn.w = -turn.w;
            }
            float w = FastMath.Clamp(turn.w, -1f, 1f);
            float sinHalf = (float)Math.Sqrt(1f - w * w);
            float scale = sinHalf < 0.0001f ? 2f : 2f * (float)Math.Acos(w) / sinHalf;
            Vector3 still = FastMath.V(turn.x * scale, turn.y * scale, turn.z * scale);
            if (float.IsNaN(still.x) || float.IsNaN(still.y) || float.IsNaN(still.z))
                still = default(Vector3);

            float gain = RotationShare / dt;
            float inv = 1f / dt;
            Vector3 a = FastMath.V(
                (targetAngularVelocity.x + still.x * gain - angularVelocity.x) * inv,
                (targetAngularVelocity.y + still.y * gain - angularVelocity.y) * inv,
                (targetAngularVelocity.z + still.z * gain - angularVelocity.z) * inv);
            return Cap(a, MaxAngularAcceleration);
        }

        static Vector3 Cap(Vector3 a, float max) {
            float sqr = FastMath.SqrMag(a);
            if (float.IsNaN(sqr))
                return default(Vector3);
            if (sqr <= max * max)
                return a;
            return FastMath.Mul(a, max / (float)Math.Sqrt(sqr));
        }
    }
}
