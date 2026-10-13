using System;

using Entanglement.Sync;

namespace Entanglement.Network
{
    public class FileTransferEndMessageHandler : NetworkMessageHandler<FileTransferEndData>
    {
        public override byte? MessageIndex => BuiltInMessageType.FileTransferEnd;

        public override NetworkMessage CreateMessage(FileTransferEndData data) {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(ushort) + sizeof(ulong)];

            int index = 0;
            message.messageData[index++] = (byte)(data.transferId & 0xFF);
            message.messageData[index++] = (byte)(data.transferId >> 8);

            byte[] hashBytes = BitConverter.GetBytes(data.hash);
            Array.Copy(hashBytes, 0, message.messageData, index, sizeof(ulong));

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < sizeof(ushort) + sizeof(ulong))
                return;

            int index = 0;
            ushort transferId = (ushort)(message.messageData[index] | (message.messageData[index + 1] << 8));
            index += sizeof(ushort);

            ulong hash = BitConverter.ToUInt64(message.messageData, index);

            FileTransferManager.OnEndReceived(sender, transferId, hash);
        }
    }

    public class FileTransferEndData : NetworkMessageData {
        public ushort transferId;
        public ulong hash;
    }
}
