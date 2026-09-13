using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    // "I don't have the custom map file the host loaded, please send it"
    [Net.SkipHandleOnLoading]
    public class MapSyncRequestMessageHandler : NetworkMessageHandler<MapSyncRequestData>
    {
        public override byte? MessageIndex => BuiltInMessageType.MapSyncRequest;

        public override NetworkMessage CreateMessage(MapSyncRequestData data)
        {
            NetworkMessage message = new NetworkMessage();
            message.messageData = Encoding.UTF8.GetBytes(data.mapFileName ?? "");
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                return;

            string mapFileName = Encoding.UTF8.GetString(message.messageData);
            CustomMapSync.OnMapRequested(sender, mapFileName);
        }
    }

    public class MapSyncRequestData : NetworkMessageData {
        public string mapFileName;
    }
}
