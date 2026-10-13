using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Entanglement.Representation;
using Entanglement.Network;

namespace Entanglement.Compat.Playermodels
{
    [Net.NoAutoRegister]
    public class LoadCustomPlayerMessageHandler : NetworkMessageHandler<LoadCustomPlayerMessageData>
    {
        public override byte? MessageIndex => CompatMessageType.PlayerModel;

        public override NetworkMessage CreateMessage(LoadCustomPlayerMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] utf8 = Encoding.UTF8.GetBytes(data.modelPath);
            message.messageData = new byte[sizeof(byte) + sizeof(long) + utf8.Length];

            int index = 0;
            byte[] userId = BitConverter.GetBytes(data.userId);
            for (int i = 0; i < sizeof(long); i++)
                message.messageData[index++] = userId[i];

            message.messageData[index++] = Convert.ToByte(data.requestCallback);

            for (int i = 0; i < utf8.Length; i++)
                message.messageData[index++] = utf8[i];

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            long userId = BitConverter.ToInt64(message.messageData, index);
            index += sizeof(long);

            bool requestCallback = Convert.ToBoolean(message.messageData[index]);
            index += sizeof(byte);

            if (requestCallback) {
                // Send PlayerModel
                if (PlayermodelsPatch.lastLoadedPath != null)
                {
                    string path = PlayermodelsPatch.lastLoadedPath;
                    LoadCustomPlayerMessageData msgData = new LoadCustomPlayerMessageData();
                    msgData.userId = SteamIntegration.currentUserId;
                    msgData.modelPath = Path.GetFileName(path);
                    Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.PlayerModel, msgData).GetBytes());
                }
            }

            int remaining = message.messageData.Length - index;
            byte[] stringBytes = new byte[remaining];
            for (int i = 0; i < remaining; i++)
                stringBytes[i] = message.messageData[index++];

            string modelPath = Encoding.UTF8.GetString(stringBytes);

            // Kept, so a rep made after this (or recreated) still gets it; see PlayermodelSync.
            Entanglement.Sync.PlayermodelSync.Want(userId, modelPath);

            if (string.IsNullOrWhiteSpace(modelPath)) {
                if (PlayerRepresentation.representations.ContainsKey(userId))
                    PlayerSkinLoader.ClearPlayermodel(PlayerRepresentation.representations[userId]);
            }
            else {
                string fileName = Path.GetFileName(modelPath);
                string fullPath = Path.Combine(PlayermodelsPatch.playerModelsPath, fileName);
                if (File.Exists(fullPath)) {
                    if (PlayerRepresentation.representations.ContainsKey(userId))
                        PlayerSkinLoader.ApplyPlayermodel(PlayerRepresentation.representations[userId], fullPath);
                    else
                        MelonLoader.MelonCoroutines.Start(RetryApplyModel(userId, fullPath));
                }
                else
                    Entanglement.Sync.PlayermodelSync.RequestModelIfMissing(userId, fileName, userId);
            }

            if (Server.instance != null)
            {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, userId);
            }
        }

        static System.Collections.IEnumerator RetryApplyModel(long userId, string fullPath) {
            float until = UnityEngine.Time.realtimeSinceStartup + 30f;
            while (UnityEngine.Time.realtimeSinceStartup < until) {
                yield return null;
                if (!PlayerRepresentation.representations.ContainsKey(userId))
                    continue;
                PlayerRepresentation rep = PlayerRepresentation.representations[userId];
                if (rep != null && !(rep.isCustomSkinned && rep.currentSkinPath == fullPath))
                    PlayerSkinLoader.ApplyPlayermodel(rep, fullPath);
                yield break;
            }
        }
    }

    public class LoadCustomPlayerMessageData : NetworkMessageData
    {
        public long userId;
        public bool requestCallback = false;
        public string modelPath;
    }
}
