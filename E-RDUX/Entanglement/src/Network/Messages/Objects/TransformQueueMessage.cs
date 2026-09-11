using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Objects;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class TransformQueueMessageHandler : NetworkMessageHandler<TransformQueueMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.TransformQueue;

        public override NetworkMessage CreateMessage(TransformQueueMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            // userId + objectId + isAdd + handedness (0 unknown / 1 left / 2 right)
            message.messageData = new byte[sizeof(ushort) + sizeof(byte) * 3];

            int index = 0;
            message.messageData[index++] = SteamIntegration.GetByteId(data.userId);

            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.objectId), ref index);

            message.messageData[index++] = Convert.ToByte(data.isAdd);
            message.messageData[index++] = data.handedness;

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            long userId = SteamIntegration.GetLongId(message.messageData[index++]);

            ushort objectId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            if (ObjectSync.TryGetSyncable(objectId, out Syncable syncable)) {
                bool isAdd = Convert.ToBoolean(message.messageData[index++]);

                // Optional trailing byte — older peers omit it
                byte handedness = 0;
                if (index < message.messageData.Length)
                    handedness = message.messageData[index++];

                if (isAdd) {
                    syncable.EnqueueOwner(userId);
                    TransformSyncable held = syncable.TryCast<TransformSyncable>();
                    if (held)
                        held.SetPreferredHeldHand(handedness);
                }
                else {
                    syncable.DequeueOwner(userId);
                }
            }

            if (Server.instance != null)
            {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessage(NetworkChannel.Object, msgBytes);
            }
        }
    }

    public class TransformQueueMessageData : NetworkMessageData
    {
        public long userId;
        public ushort objectId;
        public bool isAdd;
        public byte handedness; // 0 unknown, 1 left, 2 right — Fusion-like grip hand hint
    }
}
