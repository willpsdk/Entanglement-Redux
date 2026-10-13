using System;
using System.Text;

using Entanglement.Patching;

namespace Entanglement.Network
{
    [Net.HandleOnLoaded]
    public class MonoMatStateMessageHandler : NetworkMessageHandler<MonoMatStateMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.MonoMatState;

        public override NetworkMessage CreateMessage(MonoMatStateMessageData data) {
            NetworkMessage message = new NetworkMessage();

            byte[] utf8 = Encoding.UTF8.GetBytes(data.objectPath ?? string.Empty);
            message.messageData = new byte[sizeof(int) * 2 + sizeof(byte) * 2 + utf8.Length];

            int index = 0;
            Array.Copy(BitConverter.GetBytes(data.smallBullets), 0, message.messageData, index, sizeof(int));
            index += sizeof(int);
            Array.Copy(BitConverter.GetBytes(data.mediumBullets), 0, message.messageData, index, sizeof(int));
            index += sizeof(int);
            message.messageData[index++] = (byte)(data.opened ? 1 : 0);
            message.messageData[index++] = (byte)(data.unlocked ? 1 : 0);
            Array.Copy(utf8, 0, message.messageData, index, utf8.Length);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < sizeof(int) * 2 + 2)
                return;

            if (Server.instance != null && sender != SteamIntegration.lobbyOwnerId)
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, message.GetBytes(), sender);

            int index = 0;
            int smallBullets = BitConverter.ToInt32(message.messageData, index); index += sizeof(int);
            int mediumBullets = BitConverter.ToInt32(message.messageData, index); index += sizeof(int);
            bool opened = message.messageData[index++] != 0;
            bool unlocked = message.messageData[index++] != 0;

            string objectPath = Encoding.UTF8.GetString(
                message.messageData, index, message.messageData.Length - index);

            MonoMatSync.ApplyState(objectPath, smallBullets, mediumBullets, opened, unlocked);
        }
    }

    public class MonoMatStateMessageData : NetworkMessageData {
        public string objectPath;
        public int smallBullets;
        public int mediumBullets;
        public bool opened;
        public bool unlocked;
    }
}
