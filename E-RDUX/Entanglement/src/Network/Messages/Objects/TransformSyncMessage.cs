using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Objects;

using StressLevelZero.Pool;

using UnityEngine;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class TransformSyncMessageHandler : NetworkMessageHandler<TransformSyncMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.TransformSync;

        public override NetworkMessage CreateMessage(TransformSyncMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(ushort) + SimplifiedTransform.size + sizeof(float) * 6];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.objectId), ref index);

            message.messageData = message.messageData.AddBytes(data.simplifiedTransform.GetBytes(), ref index);

            // Velocities let the receiver dead reckon between packets instead of freezing on the last state
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.velocity.x), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.velocity.y), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.velocity.z), ref index);

            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.angularVelocity.x), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.angularVelocity.y), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.angularVelocity.z), ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            ushort objectId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            if (ObjectSync.TryGetSyncable(objectId, out Syncable syncable)) {
                if (syncable is TransformSyncable) {
                    TransformSyncable syncObj = syncable.Cast<TransformSyncable>();

                    SimplifiedTransform simpleTransform = SimplifiedTransform.FromBytes(message.messageData.ToList().GetRange(index, SimplifiedTransform.size).ToArray());

                    int velocityIndex = index + SimplifiedTransform.size;

                    Vector3 velocity = Vector3.zero;
                    Vector3 angularVelocity = Vector3.zero;

                    if (message.messageData.Length >= velocityIndex + sizeof(float) * 6) {
                        velocity.x = BitConverter.ToSingle(message.messageData, velocityIndex);
                        velocity.y = BitConverter.ToSingle(message.messageData, velocityIndex + sizeof(float));
                        velocity.z = BitConverter.ToSingle(message.messageData, velocityIndex + sizeof(float) * 2);

                        angularVelocity.x = BitConverter.ToSingle(message.messageData, velocityIndex + sizeof(float) * 3);
                        angularVelocity.y = BitConverter.ToSingle(message.messageData, velocityIndex + sizeof(float) * 4);
                        angularVelocity.z = BitConverter.ToSingle(message.messageData, velocityIndex + sizeof(float) * 5);
                    }

                    syncObj.ApplyTransform(simpleTransform, velocity, angularVelocity);
                }
            }

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, msgBytes, sender);
            }
        }
    }

    public class TransformSyncMessageData : NetworkMessageData {
        public ushort objectId;
        public SimplifiedTransform simplifiedTransform;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public bool resting; // Final pose of a body that just fell asleep, sent reliably
    }
}
