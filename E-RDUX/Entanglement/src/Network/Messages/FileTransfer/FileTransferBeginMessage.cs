using System;
using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class FileTransferBeginMessageHandler : NetworkMessageHandler<FileTransferBeginData>
    {
        public override byte? MessageIndex => BuiltInMessageType.FileTransferBegin;

        public override NetworkMessage CreateMessage(FileTransferBeginData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] nameBytes = Encoding.UTF8.GetBytes(data.fileName);
            message.messageData = new byte[sizeof(ushort) + sizeof(byte) + sizeof(int) + sizeof(byte) + nameBytes.Length];

            int index = 0;
            message.messageData[index++] = (byte)(data.transferId & 0xFF);
            message.messageData[index++] = (byte)(data.transferId >> 8);
            message.messageData[index++] = (byte)data.category;

            byte[] sizeBytes = BitConverter.GetBytes(data.totalBytes);
            Array.Copy(sizeBytes, 0, message.messageData, index, sizeof(int));
            index += sizeof(int);

            message.messageData[index++] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, message.messageData, index, nameBytes.Length);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length < 8)
                return;

            int index = 0;
            ushort transferId = (ushort)(message.messageData[index] | (message.messageData[index + 1] << 8));
            index += 2;

            FileTransferCategory category = (FileTransferCategory)message.messageData[index++];

            int totalBytes = BitConverter.ToInt32(message.messageData, index);
            index += sizeof(int);

            byte nameLen = message.messageData[index++];
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
        public int totalBytes;
        public string fileName;
    }
}
