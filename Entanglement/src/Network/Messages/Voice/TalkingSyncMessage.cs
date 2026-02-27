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
            if (message.messageData == null || message.messageData.Length < sizeof(ulong) + 1)
                return;

            try
            {
                int index = 0;
                ulong userId = BitConverter.ToUInt64(message.messageData, index);
                index += sizeof(ulong);

                bool isTalking = message.messageData[index] != 0;

                // Update the player representation talking state
                if (Representation.PlayerRepresentation.representations.TryGetValue(userId, out var rep))
                {
                    rep.SetTalking(isTalking);
                }

                // Server relays to other clients
                if (Server.instance != null)
                {
                    byte[] msgBytes = message.GetBytes();
                    Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, msgBytes, sender);
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error handling TalkingSyncMessage: {ex.Message}");
            }
        }
    }

    public class TalkingSyncData : NetworkMessageData
    {
        public ulong userId;
        public bool isTalking;
    }
}
