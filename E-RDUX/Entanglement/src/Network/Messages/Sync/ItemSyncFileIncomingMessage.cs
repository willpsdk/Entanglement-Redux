using System;
using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    // "Sending item X as file Y" - announced right before the actual P2P byte transfer starts,
    // so the requester can map the (title-less) file transfer back to the item title it asked for
    [Net.SkipHandleOnLoading]
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
            string title = Encoding.UTF8.GetString(message.messageData, 1, titleLen);
            string fileName = Encoding.UTF8.GetString(message.messageData, 1 + titleLen, message.messageData.Length - 1 - titleLen);

            CustomItemSync.OnFileIncoming(sender, title, fileName);
        }
    }

    public class ItemSyncFileIncomingData : NetworkMessageData {
        public string title;
        public string fileName;
    }
}
