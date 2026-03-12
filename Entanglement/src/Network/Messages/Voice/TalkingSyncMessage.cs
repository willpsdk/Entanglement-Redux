using System;
using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Representation;
using MelonLoader;
using UnityEngine;

namespace Entanglement.Network
{
    /// <summary>
    /// Syncs player talking state across the network for mouth animations
    /// </summary>
    public class TalkingSyncMessageHandler : NetworkMessageHandler<TalkingSyncData>
    {
        public override byte? MessageIndex => BuiltInMessageType.TalkingSync;

        public override NetworkMessage CreateMessage(TalkingSyncData data)
        {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[sizeof(ulong) + sizeof(bool)];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.userId), ref index);
            message.messageData[index++] = data.isTalking ? (byte)1 : (byte)0;

            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            // Voice chat disabled
            return;
        }
    }

    public class TalkingSyncData : NetworkMessageData
    {
        public ulong userId;
        public bool isTalking;
    }
}
