using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Entanglement.Extensions;

namespace Entanglement.Data
{
    public struct SimplifiedTransform
    {
        // Size: Position (12) + Rotation (7) + Velocity (6) + AngVelocity (6) + IsSleeping (1) = 32 bytes total!
        public const ushort size = (sizeof(float) * 3 + SimplifiedQuaternion.size + sizeof(short) * 6 + 1);

        // Velocity precision: stores up to +/- 327.67 m/s with 2 decimal places of accuracy (plenty for Boneworks)
        public const float VELOCITY_PRECISION = 100.0f;

        public Vector3 position;
        public SimplifiedQuaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public bool isSleeping;

        public SimplifiedTransform(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = SimplifiedQuaternion.SimplifyQuat(rotation);
            this.velocity = Vector3.zero;
            this.angularVelocity = Vector3.zero;
            this.isSleeping = false;
        }

        public SimplifiedTransform(Transform transform, Rigidbody rb = null)
        {
            this.position = transform.position;
            this.rotation = SimplifiedQuaternion.SimplifyQuat(transform.rotation);

            if (rb)
            {
                this.velocity = rb.velocity;
                this.angularVelocity = rb.angularVelocity;
                this.isSleeping = rb.IsSleeping();
            }
            else
            {
                this.velocity = Vector3.zero;
                this.angularVelocity = Vector3.zero;
                this.isSleeping = true;
            }
        }

        public byte[] GetBytes()
        {
            List<byte> bytes = new List<byte>(size);

            // 1. Position (12 bytes)
            bytes.AddRange(BitConverter.GetBytes(position.x));
            bytes.AddRange(BitConverter.GetBytes(position.y));
            bytes.AddRange(BitConverter.GetBytes(position.z));

            // 2. Rotation (7 bytes)
            bytes.AddRange(BitConverter.GetBytes(rotation.c1));
            bytes.AddRange(BitConverter.GetBytes(rotation.c2));
            bytes.AddRange(BitConverter.GetBytes(rotation.c3));
            bytes.Add(rotation.loss);

            // 3. Velocity COMPRESSED (6 bytes)
            bytes.AddRange(BitConverter.GetBytes((short)(velocity.x * VELOCITY_PRECISION)));
            bytes.AddRange(BitConverter.GetBytes((short)(velocity.y * VELOCITY_PRECISION)));
            bytes.AddRange(BitConverter.GetBytes((short)(velocity.z * VELOCITY_PRECISION)));

            // 4. Angular Velocity COMPRESSED (6 bytes)
            bytes.AddRange(BitConverter.GetBytes((short)(angularVelocity.x * VELOCITY_PRECISION)));
            bytes.AddRange(BitConverter.GetBytes((short)(angularVelocity.y * VELOCITY_PRECISION)));
            bytes.AddRange(BitConverter.GetBytes((short)(angularVelocity.z * VELOCITY_PRECISION)));

            // 5. State (1 byte)
            bytes.Add(isSleeping ? (byte)1 : (byte)0);

            return bytes.ToArray();
        }

        public static SimplifiedTransform FromBytes(byte[] bytes)
        {
            SimplifiedTransform transform = new SimplifiedTransform();
            int index = 0;

            transform.position.x = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.position.y = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.position.z = BitConverter.ToSingle(bytes, index); index += sizeof(float);

            transform.rotation.c1 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.c2 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.c3 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.loss = bytes[index]; index += 1;

            transform.velocity.x = BitConverter.ToInt16(bytes, index) / VELOCITY_PRECISION; index += sizeof(short);
            transform.velocity.y = BitConverter.ToInt16(bytes, index) / VELOCITY_PRECISION; index += sizeof(short);
            transform.velocity.z = BitConverter.ToInt16(bytes, index) / VELOCITY_PRECISION; index += sizeof(short);

            transform.angularVelocity.x = BitConverter.ToInt16(bytes, index) / VELOCITY_PRECISION; index += sizeof(short);
            transform.angularVelocity.y = BitConverter.ToInt16(bytes, index) / VELOCITY_PRECISION; index += sizeof(short);
            transform.angularVelocity.z = BitConverter.ToInt16(bytes, index) / VELOCITY_PRECISION; index += sizeof(short);

            transform.isSleeping = bytes[index] == 1;

            return transform;
        }

        public void Apply(Transform target)
        {
            target.position = position;
            target.rotation = rotation.ExpandQuat();
        }

        public void Apply(Rigidbody target)
        {
            target.MovePosition(position);
            target.MoveRotation(rotation.ExpandQuat());
            target.velocity = velocity;
            target.angularVelocity = angularVelocity;
            if (isSleeping) target.Sleep();
        }
    }
}