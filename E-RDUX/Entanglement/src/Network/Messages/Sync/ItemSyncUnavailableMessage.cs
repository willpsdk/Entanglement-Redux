using System;
using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    // "I don't have that item's file either" - lets a waiting requester stop waiting instead of
    // hanging until the transfer timeout
    [Net.SkipHandleOnLoading]
    public class ItemSyncUnavailableMessageHandler : NetworkMessageHandler<ItemSyncUnavailableData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ItemSyncUnavailable;

        public override NetworkMessage CreateMessage(ItemSyncUnavailableData data)
        {
            NetworkMessage message = new NetworkMessage();
            message.messageData = Encoding.UTF8.GetBytes(data.title);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                return;

            string title = Encoding.UTF8.GetString(message.messageData);
            CustomItemSync.OnItemUnavailable(sender, title);
        }
    }

    public class ItemSyncUnavailableData : NetworkMessageData {
        public string title;
    }
}
