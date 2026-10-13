using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Entanglement.Extensions;

using MelonLoader;

namespace Entanglement.Network
{
    public class ShortIdMessageHandler : NetworkMessageHandler<ShortIdMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ShortId;

        public override NetworkMessage CreateMessage(ShortIdMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(long) + sizeof(byte)];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.userId), ref index);

            message.messageData[index++] = data.byteId;

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            long userId = BitConverter.ToInt64(message.messageData, index);
            index += sizeof(long);

            byte byteId = message.messageData[index++];

            // From a client it's a request: they're missing a number, so they get the whole list.
            if (Server.instance != null) {
                if (sender != SteamIntegration.currentUserId)
                    SendIdsTo(sender, false);
                return;
            }

            if (userId == SteamIntegration.currentUserId) {
                if (SteamIntegration.localByteId != byteId)
                    EntangleLogger.Debug("[Join] Our player number is " + byteId + " (was " + SteamIntegration.localByteId + ")");
                SteamIntegration.localByteId = byteId;
            }
            SteamIntegration.RegisterUser(userId, byteId);
        }

        /// <summary>Host only: the player's own number and everyone else's, and their number to everyone
        /// if it's new.</summary>
        public static void SendIdsTo(long userId, bool announce) {
            Server server = Server.instance;
            if (server == null || !server.connectedUsers.Contains(userId))
                return;

            bool known = SteamIntegration.GetByteId(userId) != 255;
            byte theirs = SteamIntegration.RegisterUser(userId);
            if (theirs == 255)
                return;

            foreach (KeyValuePair<byte, long> pair in SteamIntegration.byteIds.ToArray()) {
                if (pair.Value == userId)
                    continue;
                NetworkMessage other = NetworkMessage.CreateMessage(BuiltInMessageType.ShortId,
                    new ShortIdMessageData { userId = pair.Value, byteId = pair.Key });
                server.SendMessage(userId, NetworkChannel.Reliable, other.GetBytes());
            }

            NetworkMessage own = NetworkMessage.CreateMessage(BuiltInMessageType.ShortId,
                new ShortIdMessageData { userId = userId, byteId = theirs });
            if (announce || !known)
                server.BroadcastMessage(NetworkChannel.Reliable, own.GetBytes());
            else
                server.SendMessage(userId, NetworkChannel.Reliable, own.GetBytes());
        }
    }

    public class ShortIdMessageData : NetworkMessageData
    {
        public long userId;
        public byte byteId;
    }
}
