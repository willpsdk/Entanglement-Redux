using System;
using System.Linq;
using System.Collections.Generic;

using UnityEngine;

using Entanglement.Extensions;

namespace Entanglement.Data
{
    public struct SimplifiedTransform {
        // Upgraded Size: Position (12) + Rotation (7) + Velocity (12) + AngVelocity (12) + IsSleeping (1) = 44 bytes total
        public const ushort size = (sizeof(float) * 3 + SimplifiedQuaternion.size + sizeof(float) * 6 + 1);
        public const ushort size_small = (sizeof(short) * 3 + SimplifiedQuaternion.size + sizeof(float) * 6 + 1);

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
            
            if (rb) {
                this.velocity = rb.velocity;
                this.angularVelocity = rb.angularVelocity;
                this.isSleeping = rb.IsSleeping();
            } else {
                this.velocity = Vector3.zero;
                this.angularVelocity = Vector3.zero;
                this.isSleeping = true;
            }
        }

        public byte[] GetBytes() {
            List<byte> bytes = new List<byte>();

            bytes.AddRange(BitConverter.GetBytes(position.x));
            bytes.AddRange(BitConverter.GetBytes(position.y));
            bytes.AddRange(BitConverter.GetBytes(position.z));

            bytes.AddRange(BitConverter.GetBytes(rotation.c1));
            bytes.AddRange(BitConverter.GetBytes(rotation.c2));
            bytes.AddRange(BitConverter.GetBytes(rotation.c3));
            bytes.Add(rotation.loss);

            bytes.AddRange(BitConverter.GetBytes(velocity.x));
            bytes.AddRange(BitConverter.GetBytes(velocity.y));
            bytes.AddRange(BitConverter.GetBytes(velocity.z));

            bytes.AddRange(BitConverter.GetBytes(angularVelocity.x));
            bytes.AddRange(BitConverter.GetBytes(angularVelocity.y));
            bytes.AddRange(BitConverter.GetBytes(angularVelocity.z));

            bytes.Add(isSleeping ? (byte)1 : (byte)0);

            return bytes.ToArray();
        }

        public byte[] GetSmallBytes(Vector3 root) {
            List<byte> bytes = new List<byte>();

            bytes.AddRange(root.InverseTransformPosition(position).GetShortBytes());

            bytes.AddRange(BitConverter.GetBytes(rotation.c1));
            bytes.AddRange(BitConverter.GetBytes(rotation.c2));
            bytes.AddRange(BitConverter.GetBytes(rotation.c3));
            bytes.Add(rotation.loss);

            bytes.AddRange(BitConverter.GetBytes(velocity.x));
            bytes.AddRange(BitConverter.GetBytes(velocity.y));
            bytes.AddRange(BitConverter.GetBytes(velocity.z));

            bytes.AddRange(BitConverter.GetBytes(angularVelocity.x));
            bytes.AddRange(BitConverter.GetBytes(angularVelocity.y));
            bytes.AddRange(BitConverter.GetBytes(angularVelocity.z));

            bytes.Add(isSleeping ? (byte)1 : (byte)0);

            return bytes.ToArray();
        }

        public static SimplifiedTransform SimplyTransform(Transform transform) => SimplyTransform(transform.position, transform.rotation);

        public static SimplifiedTransform SimplyTransform(Vector3 position, Quaternion rotation) {
            SimplifiedTransform simplified = new SimplifiedTransform();
            simplified.position = position;
            simplified.rotation = SimplifiedQuaternion.SimplifyQuat(rotation);
            return simplified;
        }

        public static SimplifiedTransform FromBytes(byte[] bytes) {
            SimplifiedTransform transform = new SimplifiedTransform();
            int index = 0;

            transform.position.x = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.position.y = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.position.z = BitConverter.ToSingle(bytes, index); index += sizeof(float);

            transform.rotation.c1 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.c2 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.c3 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.loss = bytes[index]; index += 1;

            transform.velocity.x = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.velocity.y = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.velocity.z = BitConverter.ToSingle(bytes, index); index += sizeof(float);

            transform.angularVelocity.x = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.angularVelocity.y = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.angularVelocity.z = BitConverter.ToSingle(bytes, index); index += sizeof(float);

            transform.isSleeping = bytes[index] == 1;

            return transform;
        }

        public static SimplifiedTransform FromSmallBytes(byte[] bytes, Vector3 root) {
            SimplifiedTransform transform = new SimplifiedTransform();
            int index = 0;

            transform.position = root.TransformPosition(bytes.FromShortBytes(index)); index += sizeof(short) * 3;

            transform.rotation.c1 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.c2 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.c3 = BitConverter.ToInt16(bytes, index); index += sizeof(short);
            transform.rotation.loss = bytes[index]; index += 1;

            transform.velocity.x = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.velocity.y = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.velocity.z = BitConverter.ToSingle(bytes, index); index += sizeof(float);

            transform.angularVelocity.x = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.angularVelocity.y = BitConverter.ToSingle(bytes, index); index += sizeof(float);
            transform.angularVelocity.z = BitConverter.ToSingle(bytes, index); index += sizeof(float);

            transform.isSleeping = bytes[index] == 1;

            return transform;
        }

        public void Apply(Transform target) {
            target.position = position;
            target.rotation = rotation.ExpandQuat();
        }

        public void Apply(Rigidbody target) {
            target.MovePosition(position);
            target.MoveRotation(rotation.ExpandQuat());
            target.velocity = velocity;
            target.angularVelocity = angularVelocity;
            if (isSleeping) target.Sleep();
        }
    }
}