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

            message.messageData = new byte[sizeof(ushort) + sizeof(byte) * 2];

            int index = 0;
            message.messageData[index++] = SteamIntegration.GetByteId(data.userId);

            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.objectId), ref index);

            message.messageData[index++] = Convert.ToByte(data.isAdd);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            ulong userId = SteamIntegration.GetLongId(message.messageData[index++]);

            ushort objectId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            bool isAdd = Convert.ToBoolean(message.messageData[index++]);

            if (Server.instance != null)
            {
                // Server is authority: normalize owner identity to actual sender to prevent queue desync/spoof.
                userId = sender;
            }

            if (ObjectSync.TryGetSyncable(objectId, out Syncable syncable)) {
                // Try to enqueue the user
                if (isAdd) {
                    syncable.EnqueueOwner(userId);
                }
                // Remove the user from the queue
                else {
                    syncable.DequeueOwner(userId);
                }
            }

            if (Server.instance != null)
            {
                TransformQueueMessageData relayData = new TransformQueueMessageData
                {
                    userId = userId,
                    objectId = objectId,
                    isAdd = isAdd,
                };

                NetworkMessage relayMessage = NetworkMessage.CreateMessage(BuiltInMessageType.TransformQueue, relayData);
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, relayMessage.GetBytes(), sender);
            }
        }
    }

    public class TransformQueueMessageData : NetworkMessageData
    {
        public ulong userId;
        public ushort objectId;
        public bool isAdd;
    }
}
