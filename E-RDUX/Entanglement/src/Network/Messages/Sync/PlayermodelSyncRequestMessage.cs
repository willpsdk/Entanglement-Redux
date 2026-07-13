using System;
using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    // "I don't have your playermodel file, please send it"
    [Net.SkipHandleOnLoading]
    public class PlayermodelSyncRequestMessageHandler : NetworkMessageHandler<PlayermodelSyncRequestData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PlayermodelSyncRequest;

        public override NetworkMessage CreateMessage(PlayermodelSyncRequestData data)
        {
            NetworkMessage message = new NetworkMessage();
            message.messageData = Encoding.UTF8.GetBytes(data.modelPath);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                return;

            string modelPath = Encoding.UTF8.GetString(message.messageData);
            PlayermodelSync.OnModelRequested(sender, modelPath);
        }
    }

    public class PlayermodelSyncRequestData : NetworkMessageData {
        public string modelPath;
    }
}
