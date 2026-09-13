using System;

using Entanglement.Extensions;
using Entanglement.Representation;

namespace Entanglement.Network
{
    /// <summary>
    /// Fusion PlayerRepGrab analogue: reliably tells remotes to AttachObject a Grip onto that
    /// player's PlayerRep stub Hand (see PlayerRepGrabber).
    /// </summary>
    [Net.SkipHandleOnLoading]
    public class PlayerRepGrabMessageHandler : NetworkMessageHandler<PlayerRepGrabMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PlayerRepGrab;

        public override NetworkMessage CreateMessage(PlayerRepGrabMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            // userId + objectId + handedness + gripIndex
            message.messageData = new byte[sizeof(ushort) + sizeof(byte) * 3];

            int index = 0;
            message.messageData[index++] = SteamIntegration.GetByteId(data.userId);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.objectId), ref index);
            message.messageData[index++] = data.handedness;
            message.messageData[index++] = data.gripIndex;

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length < sizeof(ushort) + sizeof(byte) * 3)
                throw new IndexOutOfRangeException();

            int index = 0;
            long userId = SteamIntegration.GetLongId(message.messageData[index++]);

            ushort objectId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            byte handedness = message.messageData[index++];
            byte gripIndex = message.messageData[index++];

            // Ignore our own echo
            if (userId == SteamIntegration.currentUserId)
                return;

            if (PlayerRepGrabber.TryGetGrabber(userId, out PlayerRepGrabber grabber))
                grabber.Attach(handedness, objectId, gripIndex);

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
            }
        }
    }

    public class PlayerRepGrabMessageData : NetworkMessageData
    {
        public long userId;
        public ushort objectId;
        public byte handedness; // 1 left, 2 right
        public byte gripIndex;
    }
}
