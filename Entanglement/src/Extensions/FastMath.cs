using System;

using UnityEngine;

namespace Entanglement.Extensions
{
    // Vector and rotation maths done in C#. In this build every UnityEngine.Vector3, Quaternion and
    // Mathf method (operators, constructors, Vector3.zero, Quaternion.identity) is a call into the
    // game that also allocates its result on the game's heap. Code that runs per body per physics
    // step uses these instead, which only read and write the struct fields. The formulas match
    // Unity's own.
    public static class FastMath
    {
        public static Vector3 V(float x, float y, float z) => new Vector3 { x = x, y = y, z = z };

        public static Quaternion Q(float x, float y, float z, float w) => new Quaternion { x = x, y = y, z = z, w = w };

        public static readonly Quaternion Identity = new Quaternion { w = 1f };

        public static Vector3 Add(Vector3 a, Vector3 b) => V(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 Sub(Vector3 a, Vector3 b) => V(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 Mul(Vector3 a, float s) => V(a.x * s, a.y * s, a.z * s);

        /// <summary>a + b * s</summary>
        public static Vector3 AddScaled(Vector3 a, Vector3 b, float s) => V(a.x + b.x * s, a.y + b.y * s, a.z + b.z * s);

        public static float SqrMag(Vector3 a) => a.x * a.x + a.y * a.y + a.z * a.z;

        public static float Mag(Vector3 a) => (float)Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z);

        public static float SqrDist(Vector3 a, Vector3 b) {
            float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
            return x * x + y * y + z * z;
        }

        // Like Vector3.Lerp, t is clamped.
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) {
            t = Clamp01(t);
            return V(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        public static float Exp(float x) => (float)Math.Exp(x);

        // lhs * rhs, as Quaternion.operator*.
        public static Quaternion Mul(Quaternion a, Quaternion b) => Q(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        // Rotations are unit quaternions, whose inverse is the conjugate.
        public static Quaternion Inverse(Quaternion q) => Q(-q.x, -q.y, -q.z, q.w);

        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        // As Quaternion.Angle, in degrees.
        public static float Angle(Quaternion a, Quaternion b) {
            float dot = Math.Abs(Dot(a, b));
            if (dot > 1f)
                dot = 1f;
            return dot > 1f - 0.000001f ? 0f : (float)(Math.Acos(dot) * 2.0 * (180.0 / Math.PI));
        }

        // As Quaternion.AngleAxis: degrees about the axis, which need not be normalised.
        public static Quaternion AngleAxis(float degrees, Vector3 axis) {
            float length = Mag(axis);
            if (length < 0.000001f)
                return Identity;
            double half = degrees * 0.5 * (Math.PI / 180.0);
            float s = (float)Math.Sin(half) / length;
            return Q(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(half));
        }

        public static Quaternion Normalize(Quaternion q) {
            float length = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (length < 0.000001f)
                return Identity;
            float inv = 1f / length;
            return Q(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        // As Quaternion.Slerp: t clamped, the short way round.
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) {
            t = Clamp01(t);
            float dot = Dot(a, b);
            if (dot < 0f) {
                b = Q(-b.x, -b.y, -b.z, -b.w);
                dot = -dot;
            }
            float wa, wb;
            if (dot > 0.9995f) {
                wa = 1f - t;
                wb = t;
            }
            else {
                double theta = Math.Acos(dot);
                double sin = Math.Sin(theta);
                wa = (float)(Math.Sin((1.0 - t) * theta) / sin);
                wb = (float)(Math.Sin(t * theta) / sin);
            }
            return Normalize(Q(a.x * wa + b.x * wb, a.y * wa + b.y * wb, a.z * wa + b.z * wb, a.w * wa + b.w * wb));
        }
    }
}
