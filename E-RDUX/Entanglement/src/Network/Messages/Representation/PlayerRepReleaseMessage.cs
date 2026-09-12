using System;

using Entanglement.Representation;

namespace Entanglement.Network
{
    /// <summary>
    /// Fusion PlayerRepRelease analogue: DetachObject on the PlayerRep stub Hand.
    /// </summary>
    [Net.SkipHandleOnLoading]
    public class PlayerRepReleaseMessageHandler : NetworkMessageHandler<PlayerRepReleaseMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PlayerRepRelease;

        public override NetworkMessage CreateMessage(PlayerRepReleaseMessageData data)
        {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[sizeof(byte) * 2];

            int index = 0;
            message.messageData[index++] = SteamIntegration.GetByteId(data.userId);
            message.messageData[index++] = data.handedness;

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length < 2)
                throw new IndexOutOfRangeException();

            int index = 0;
            long userId = SteamIntegration.GetLongId(message.messageData[index++]);
            byte handedness = message.messageData[index++];

            if (userId == SteamIntegration.currentUserId)
                return;

            if (PlayerRepGrabber.TryGetGrabber(userId, out PlayerRepGrabber grabber))
                grabber.Detach(handedness);

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
            }
        }
    }

    public class PlayerRepReleaseMessageData : NetworkMessageData
    {
        public long userId;
        public byte handedness; // 1 left, 2 right
    }
}
