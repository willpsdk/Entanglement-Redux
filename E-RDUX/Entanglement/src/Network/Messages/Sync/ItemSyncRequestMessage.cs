using System;
using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    // "I don't have item X you just spawned, please send it" - requester to the original spawner
    [Net.SkipHandleOnLoading]
    public class ItemSyncRequestMessageHandler : NetworkMessageHandler<ItemSyncRequestData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ItemSyncRequest;

        public override NetworkMessage CreateMessage(ItemSyncRequestData data)
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
            CustomItemSync.OnItemRequested(sender, title);
        }
    }

    public class ItemSyncRequestData : NetworkMessageData {
        public string title;
    }
}
