

namespace Entanglement.Network
{
    public class HeartbeatMessageHandler : NetworkMessageHandler<EmptyMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.Heartbeat;

        public override NetworkMessage CreateMessage(EmptyMessageData data) => new NetworkMessage();

        // Can be handled by either clients or the server, but are not sent back.
        public override void HandleMessage(NetworkMessage message, long sender) {
            if (Node.activeNode is Server server) {
                // Reset the heart beat of this user.
                if (server.userBeats.ContainsKey(sender))
                    server.userBeats[sender] = 0f;
            } 
            else if (Node.activeNode is Client client) {
                // A check incase for some reason another client is sending a heartbeat to us.
                if (client.hostId == sender)
                    client.hostHeartbeat = 0f;
            }
        }
    }
}
