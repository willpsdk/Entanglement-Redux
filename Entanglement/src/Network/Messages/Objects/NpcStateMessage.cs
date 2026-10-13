using System;

using Entanglement.Objects;

namespace Entanglement.Network
{
    [Net.HandleOnLoaded]
    public class NpcStateMessageHandler : NetworkMessageHandler<NpcStateMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.NpcState;

        public override NetworkMessage CreateMessage(NpcStateMessageData data) {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(ushort) + sizeof(float) + sizeof(byte) + sizeof(byte)];

            int index = 0;
            message.messageData[index++] = (byte)(data.spawnId & 0xFF);
            message.messageData[index++] = (byte)(data.spawnId >> 8);

            byte[] healthBytes = BitConverter.GetBytes(data.health);
            Array.Copy(healthBytes, 0, message.messageData, index, sizeof(float));
            index += sizeof(float);

            message.messageData[index++] = data.mentalState;
            message.messageData[index++] = data.locoState;

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < 8)
                return;

            // Only the host replays world state.
            if (sender != SteamIntegration.lobbyOwnerId)
                return;

            int index = 0;
            ushort spawnId = (ushort)(message.messageData[index] | (message.messageData[index + 1] << 8));
            index += sizeof(ushort);

            float health = BitConverter.ToSingle(message.messageData, index);
            index += sizeof(float);

            byte mentalState = message.messageData[index++];
            byte locoState = message.messageData[index++];

            NpcStateSync.ApplyRemote(spawnId, health, mentalState, locoState);
        }
    }

    public class NpcStateMessageData : NetworkMessageData {
        public ushort spawnId;
        public float health;
        public byte mentalState;
        public byte locoState;
    }
}
