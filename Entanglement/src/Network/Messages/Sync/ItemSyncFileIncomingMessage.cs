using System;
using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    [Net.HandleOnLoaded]
    public class ItemSyncFileIncomingMessageHandler : NetworkMessageHandler<ItemSyncFileIncomingData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ItemSyncFileIncoming;

        public override NetworkMessage CreateMessage(ItemSyncFileIncomingData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] titleBytes = Encoding.UTF8.GetBytes(data.title);
            byte[] nameBytes = Encoding.UTF8.GetBytes(data.fileName);

            message.messageData = new byte[sizeof(byte) + titleBytes.Length + nameBytes.Length];
            int index = 0;
            message.messageData[index++] = (byte)titleBytes.Length;
            Array.Copy(titleBytes, 0, message.messageData, index, titleBytes.Length);
            index += titleBytes.Length;
            Array.Copy(nameBytes, 0, message.messageData, index, nameBytes.Length);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                return;

            byte titleLen = message.messageData[0];
            // Without this a truncated packet reads past the end, or hands GetString a negative count.
            if (1 + titleLen > message.messageData.Length)
                return;

            string title = Encoding.UTF8.GetString(message.messageData, 1, titleLen);

            int nameLen = message.messageData.Length - 1 - titleLen;
            string fileName = nameLen > 0
                ? Encoding.UTF8.GetString(message.messageData, 1 + titleLen, nameLen)
                : "";

            if (string.IsNullOrEmpty(fileName))
                return;

            CustomItemSync.OnFileIncoming(sender, title, fileName);
        }
    }

    public class ItemSyncFileIncomingData : NetworkMessageData {
        public string title;
        public string fileName;
    }
}
