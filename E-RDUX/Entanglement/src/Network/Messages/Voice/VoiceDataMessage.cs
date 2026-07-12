using System;

using Entanglement.Voice;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class VoiceDataMessageHandler : NetworkMessageHandler<VoiceDataMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.VoiceData;

        static readonly VoiceDataMessageData cachedData = new VoiceDataMessageData();

        public static void SendVoice(byte[] compressed, int count) {
            if (Node.activeNode == null)
                return;

            cachedData.compressed = compressed;
            cachedData.count = count;

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.VoiceData, cachedData);
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
        }

        public override NetworkMessage CreateMessage(VoiceDataMessageData data)
        {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[sizeof(byte) + data.count];

            // The host relays voice, so the speaker is identified in the payload, not by the P2P sender
            message.messageData[0] = SteamIntegration.GetByteId(SteamIntegration.currentUserId);
            Buffer.BlockCopy(data.compressed, 0, message.messageData, 1, data.count);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 1)
                return;

            long speakerId = SteamIntegration.GetLongId(message.messageData[0]);
            if (speakerId == 0)
                speakerId = sender;

            if (speakerId != SteamIntegration.currentUserId)
                VoiceManager.ReceiveVoice(speakerId, message.messageData, 1, message.messageData.Length - 1);

            if (Server.instance != null)
                Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, message.GetBytes(), sender);
        }
    }

    public class VoiceDataMessageData : NetworkMessageData {
        public byte[] compressed;
        public int count;
    }
}
