using System;
using System.Collections.Generic;
using System.Text;

using Entanglement.Gamemodes;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class GamemodeStateMessageHandler : NetworkMessageHandler<GamemodeStateData>
    {
        public override byte? MessageIndex => BuiltInMessageType.GamemodeState;

        public override NetworkMessage CreateMessage(GamemodeStateData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] idBytes = Encoding.UTF8.GetBytes(data.activeModeId ?? "");
            List<byte> bytes = new List<byte>();

            bytes.Add((byte)idBytes.Length);
            bytes.AddRange(idBytes);
            bytes.Add(data.roundActive ? (byte)1 : (byte)0);
            bytes.AddRange(BitConverter.GetBytes(data.roundTimeRemaining));

            bytes.Add((byte)Math.Min(data.scores.Count, 255));
            int count = 0;
            foreach (var pair in data.scores) {
                if (count++ >= 255) break;
                bytes.AddRange(BitConverter.GetBytes(pair.Key));
                bytes.AddRange(BitConverter.GetBytes(pair.Value));
            }

            bytes.Add((byte)Math.Min(data.teams.Count, 255));
            count = 0;
            foreach (var pair in data.teams) {
                if (count++ >= 255) break;
                bytes.AddRange(BitConverter.GetBytes(pair.Key));
                bytes.Add(pair.Value);
            }

            bytes.Add((byte)Math.Min(data.eliminated.Count, 255));
            count = 0;
            foreach (long id in data.eliminated) {
                if (count++ >= 255) break;
                bytes.AddRange(BitConverter.GetBytes(id));
            }

            message.messageData = bytes.ToArray();
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (Node.isServer) return;
            if (message.messageData.Length <= 0) return;

            int index = 0;
            byte idLen = message.messageData[index++];
            string activeModeId = Encoding.UTF8.GetString(message.messageData, index, idLen);
            index += idLen;

            bool roundActive = message.messageData[index++] != 0;

            float roundTimeRemaining = BitConverter.ToSingle(message.messageData, index);
            index += sizeof(float);

            GamemodeStateData data = new GamemodeStateData {
                activeModeId = activeModeId,
                roundActive = roundActive,
                roundTimeRemaining = roundTimeRemaining,
                scores = new Dictionary<long, int>(),
                teams = new Dictionary<long, byte>(),
                eliminated = new List<long>(),
            };

            byte scoreCount = message.messageData[index++];
            for (int i = 0; i < scoreCount; i++) {
                long userId = BitConverter.ToInt64(message.messageData, index);
                index += sizeof(long);
                int score = BitConverter.ToInt32(message.messageData, index);
                index += sizeof(int);
                data.scores[userId] = score;
            }

            byte teamCount = message.messageData[index++];
            for (int i = 0; i < teamCount; i++) {
                long userId = BitConverter.ToInt64(message.messageData, index);
                index += sizeof(long);
                byte team = message.messageData[index++];
                data.teams[userId] = team;
            }

            if (index < message.messageData.Length) {
                byte eliminatedCount = message.messageData[index++];
                for (int i = 0; i < eliminatedCount; i++) {
                    long userId = BitConverter.ToInt64(message.messageData, index);
                    index += sizeof(long);
                    data.eliminated.Add(userId);
                }
            }

            GamemodeHandler.ApplyState(data);
        }
    }

    public class GamemodeStateData : NetworkMessageData {
        public string activeModeId;
        public bool roundActive;
        public float roundTimeRemaining;
        public Dictionary<long, int> scores = new Dictionary<long, int>();
        public Dictionary<long, byte> teams = new Dictionary<long, byte>();
        public List<long> eliminated = new List<long>();
    }
}
