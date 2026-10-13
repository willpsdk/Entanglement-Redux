using System;
using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    public class FileTransferBeginMessageHandler : NetworkMessageHandler<FileTransferBeginData>
    {
        public override byte? MessageIndex => BuiltInMessageType.FileTransferBegin;

        public override NetworkMessage CreateMessage(FileTransferBeginData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] nameBytes = Encoding.UTF8.GetBytes(data.fileName);
            message.messageData = new byte[sizeof(ushort) + sizeof(byte) + sizeof(long) + sizeof(byte) + nameBytes.Length];

            int index = 0;
            message.messageData[index++] = (byte)(data.transferId & 0xFF);
            message.messageData[index++] = (byte)(data.transferId >> 8);
            message.messageData[index++] = (byte)data.category;

            byte[] sizeBytes = BitConverter.GetBytes(data.totalBytes);
            Array.Copy(sizeBytes, 0, message.messageData, index, sizeof(long));
            index += sizeof(long);

            message.messageData[index++] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, message.messageData, index, nameBytes.Length);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            // 2 id + 1 category + 8 size + 1 name length
            if (message.messageData == null || message.messageData.Length < 12)
                return;

            int index = 0;
            ushort transferId = (ushort)(message.messageData[index] | (message.messageData[index + 1] << 8));
            index += 2;

            FileTransferCategory category = (FileTransferCategory)message.messageData[index++];

            long totalBytes = BitConverter.ToInt64(message.messageData, index);
            index += sizeof(long);

            byte nameLen = message.messageData[index++];
            if (index + nameLen > message.messageData.Length)
                return;

            string fileName = Encoding.UTF8.GetString(message.messageData, index, nameLen);

            FileTransferManager.OnBeginReceived(sender, new FileTransferBeginData {
                transferId = transferId,
                category = category,
                totalBytes = totalBytes,
                fileName = fileName,
            });
        }
    }

    public class FileTransferBeginData : NetworkMessageData {
        public ushort transferId;
        public FileTransferCategory category;
        public long totalBytes;
        public string fileName;
    }
}
