using System;
using System.Collections.Generic;
using System.Linq;

using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Objects;

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

            // FIXED: Only allocate space for one ushort (objectId) + the simplified transform size
            message.messageData = new byte[sizeof(ushort) + SimplifiedTransform.size];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.objectId), ref index);
            message.messageData = message.messageData.AddBytes(data.simplifiedTransform.GetBytes(), ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
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
                    syncObj.ApplyTransform(simpleTransform);
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
    }
}