using System;

using Entanglement.Extensions;
using Entanglement.Objects;

namespace Entanglement.Network
{
    [Net.HandleOnLoaded]
    public class ClawTargetMessageHandler : NetworkMessageHandler<ClawTargetMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ClawTarget;

        public override NetworkMessage CreateMessage(ClawTargetMessageData data) {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(long) + sizeof(byte)];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.prey), ref index);
            message.messageData[index++] = (byte)(data.valid ? 1 : 0);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < sizeof(long) + sizeof(byte))
                return;

            // The host elects prey, and so does whoever is running the claw right now (its prey).
            if (sender != SteamIntegration.lobbyOwnerId && sender != BossClawSync.Owner)
                return;

            int index = 0;
            long prey = BitConverter.ToInt64(message.messageData, index);
            index += sizeof(long);
            bool valid = message.messageData[index] != 0;

            BossClawSync.ApplyRemote(prey, valid);

            if (Server.instance != null)
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, message.GetBytes(), sender);
        }
    }

    public class ClawTargetMessageData : NetworkMessageData {
        public long prey;
        public bool valid;
    }
}
