using System;
using System.Text;

using Entanglement.Gamemodes;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class GamemodeEventMessageHandler : NetworkMessageHandler<GamemodeEventData>
    {
        public override byte? MessageIndex => BuiltInMessageType.GamemodeEvent;

        public override NetworkMessage CreateMessage(GamemodeEventData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] msgBytes = Encoding.UTF8.GetBytes(data.message ?? "");
            message.messageData = new byte[sizeof(byte) + sizeof(long) * 2 + sizeof(int) + sizeof(ushort) + msgBytes.Length];

            int index = 0;
            message.messageData[index++] = (byte)data.type;

            Array.Copy(BitConverter.GetBytes(data.a), 0, message.messageData, index, sizeof(long));
            index += sizeof(long);
            Array.Copy(BitConverter.GetBytes(data.b), 0, message.messageData, index, sizeof(long));
            index += sizeof(long);
            Array.Copy(BitConverter.GetBytes(data.value), 0, message.messageData, index, sizeof(int));
            index += sizeof(int);

            Array.Copy(BitConverter.GetBytes((ushort)msgBytes.Length), 0, message.messageData, index, sizeof(ushort));
            index += sizeof(ushort);
            Array.Copy(msgBytes, 0, message.messageData, index, msgBytes.Length);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0) return;

            int index = 0;
            GamemodeEventType type = (GamemodeEventType)message.messageData[index++];

            long a = BitConverter.ToInt64(message.messageData, index);
            index += sizeof(long);
            long b = BitConverter.ToInt64(message.messageData, index);
            index += sizeof(long);
            int value = BitConverter.ToInt32(message.messageData, index);
            index += sizeof(int);

            ushort msgLen = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);
            string msg = msgLen > 0 ? Encoding.UTF8.GetString(message.messageData, index, msgLen) : "";

            GamemodeHandler.ApplyEvent(sender, new GamemodeEventData { type = type, a = a, b = b, value = value, message = msg });
        }
    }

    public class GamemodeEventData : NetworkMessageData {
        public GamemodeEventType type;
        public long a;
        public long b;
        public int value;
        public string message;
    }
}
