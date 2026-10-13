using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Extensions;

namespace Entanglement.Data {
    public struct SimplifiedQuaternion {
        public short c1, c2, c3;
        public byte loss; // Lost component in compression

        public const ushort size = sizeof(short) * 3 + sizeof(byte);

        // The amount we multiply / divide by to preserve precision when using shorts
        public const float PRECISION_OFFSET = 10000.0f;

        // Drops the largest component, which the other three rebuild. Runs for every body sent, so no
        // arrays and no Unity calls.
        public static SimplifiedQuaternion SimplifyQuat(Quaternion quat) {
            float x = quat.x, y = quat.y, z = quat.z, w = quat.w;

            byte dropped = 0;
            float biggest = Math.Abs(x);
            if (Math.Abs(y) > biggest) { dropped = 1; biggest = Math.Abs(y); }
            if (Math.Abs(z) > biggest) { dropped = 2; biggest = Math.Abs(z); }
            if (Math.Abs(w) > biggest) { dropped = 3; }

            // The dropped one comes back positive, so the rest are flipped to match its sign.
            float sign;
            switch (dropped) {
                case 0: sign = x < 0f ? -1f : 1f; break;
                case 1: sign = y < 0f ? -1f : 1f; break;
                case 2: sign = z < 0f ? -1f : 1f; break;
                default: sign = w < 0f ? -1f : 1f; break;
            }
            float scale = sign * PRECISION_OFFSET;

            SimplifiedQuaternion simplified;
            simplified.loss = dropped;
            switch (dropped) {
                case 0:
                    simplified.c1 = (short)(y * scale);
                    simplified.c2 = (short)(z * scale);
                    simplified.c3 = (short)(w * scale);
                    break;
                case 1:
                    simplified.c1 = (short)(x * scale);
                    simplified.c2 = (short)(z * scale);
                    simplified.c3 = (short)(w * scale);
                    break;
                case 2:
                    simplified.c1 = (short)(x * scale);
                    simplified.c2 = (short)(y * scale);
                    simplified.c3 = (short)(w * scale);
                    break;
                default:
                    simplified.c1 = (short)(x * scale);
                    simplified.c2 = (short)(y * scale);
                    simplified.c3 = (short)(z * scale);
                    break;
            }
            return simplified;
        }

        public Quaternion ExpandQuat() {
            if (loss >= 4)
                throw new DataCorruptionException($"Expanding a quaternion led to a lost component of {loss}!");

            float f1 = c1 / PRECISION_OFFSET;
            float f2 = c2 / PRECISION_OFFSET;
            float f3 = c3 / PRECISION_OFFSET;

            // Rounding can take the rest a hair past 1; never a NaN rotation.
            float rest = 1f - f1 * f1 - f2 * f2 - f3 * f3;
            float f4 = rest > 0f ? (float)Math.Sqrt(rest) : 0f;

            switch (loss) {
                case 0:
                    return FastMath.Q(f4, f1, f2, f3);

                case 1:
                    return FastMath.Q(f1, f4, f2, f3);

                case 2:
                    return FastMath.Q(f1, f2, f4, f3);

                case 3:
                    return FastMath.Q(f1, f2, f3, f4);
            }

            return FastMath.Identity;
        }
    }
}
