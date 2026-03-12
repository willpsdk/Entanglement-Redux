using System;
using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Managers;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class VoiceDataMessageHandler : NetworkMessageHandler<VoiceDataMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.VoiceData;

        public override NetworkMessage CreateMessage(VoiceDataMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            ushort length = (ushort)(data.compressedVoiceData != null ? data.compressedVoiceData.Length : 0);
            message.messageData = new byte[sizeof(byte) + sizeof(ushort) + length];

            int index = 0;
            message.messageData[index++] = SteamIntegration.GetByteId(data.userId);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(length), ref index);

            if (length > 0)
                message.messageData = message.messageData.AddBytes(data.compressedVoiceData, ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            // Voice chat disabled
            return;
        }
    }

    public class VoiceDataMessageData : NetworkMessageData
    {
        public ulong userId;
        public byte[] compressedVoiceData;
    }
}
