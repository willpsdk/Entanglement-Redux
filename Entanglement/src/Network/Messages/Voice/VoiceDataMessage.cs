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
            if (message.messageData == null || message.messageData.Length < sizeof(byte) + sizeof(ushort))
                return;

            int index = 0;
            ulong userId = SteamIntegration.GetLongId(message.messageData[index++]);
            ushort length = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            if (length <= 0 || index + length > message.messageData.Length)
                return;

            byte[] compressedData = new byte[length];
            Buffer.BlockCopy(message.messageData, index, compressedData, 0, length);

            VoiceChatManager.ReceiveVoicePacket(userId, compressedData);

            if (Server.instance != null)
            {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, msgBytes, sender);
            }
        }
    }

    public class VoiceDataMessageData : NetworkMessageData
    {
        public ulong userId;
        public byte[] compressedVoiceData;
    }
}
