using System;
using System.Text;

using Entanglement.Gamemodes;

namespace Entanglement.Network
{
    /// <summary>
    /// Host → clients: selected/ready/started + gamemode id. Fusion metadata ideology, thin first cut.
    /// </summary>
    [Net.SkipHandleOnLoading]
    public class GamemodeStateMessageHandler : NetworkMessageHandler<GamemodeStateMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.GamemodeState;

        public override NetworkMessage CreateMessage(GamemodeStateMessageData data) {
            NetworkMessage message = new NetworkMessage();
            byte[] idBytes = Encoding.UTF8.GetBytes(data.gamemodeId ?? "");
            if (idBytes.Length > 200)
                Array.Resize(ref idBytes, 200);

            message.messageData = new byte[3 + 1 + idBytes.Length];
            int i = 0;
            message.messageData[i++] = data.selected ? (byte)1 : (byte)0;
            message.messageData[i++] = data.ready ? (byte)1 : (byte)0;
            message.messageData[i++] = data.started ? (byte)1 : (byte)0;
            message.messageData[i++] = (byte)idBytes.Length;
            Buffer.BlockCopy(idBytes, 0, message.messageData, i, idBytes.Length);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < 4)
                return;
            // Clients only — host is source of truth.
            if (Node.isServer)
                return;

            bool selected = message.messageData[0] != 0;
            bool ready = message.messageData[1] != 0;
            bool started = message.messageData[2] != 0;
            int len = message.messageData[3];
            string id = "";
            if (len > 0 && message.messageData.Length >= 4 + len)
                id = Encoding.UTF8.GetString(message.messageData, 4, len);

            GamemodeManager.ApplyRemoteState(id, selected, ready, started);
        }
    }

    public class GamemodeStateMessageData : NetworkMessageData {
        public string gamemodeId;
        public bool selected;
        public bool ready;
        public bool started;
    }
}
