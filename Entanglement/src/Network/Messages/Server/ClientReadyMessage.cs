using System;

namespace Entanglement.Network
{
    // Sent by a client once its scene is ready; the host replies with the world state replay
    public class ClientReadyMessageHandler : NetworkMessageHandler<EmptyMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ClientReady;

        public override NetworkMessage CreateMessage(EmptyMessageData data) => new NetworkMessage();

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            Server.instance?.ReplayWorldStateTo(sender);
        }
    }
}
