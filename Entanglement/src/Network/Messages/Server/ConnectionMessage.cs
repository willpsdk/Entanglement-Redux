using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using MelonLoader;

using UnityEngine;

using Entanglement.Data;

namespace Entanglement.Network {
    public class ConnectionMessageHandler : NetworkMessageHandler<ConnectionMessageData> {
        private static readonly Dictionary<long, float> lastConnectionTimes = new Dictionary<long, float>();
        private const float CONNECTION_DEBOUNCE_SECONDS = 2f;

        public override byte? MessageIndex => BuiltInMessageType.Connection;

        public override NetworkMessage CreateMessage(ConnectionMessageData data) {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(ushort) + 16];

            int index = 0;

            foreach (byte b in BitConverter.GetBytes(data.packedVersion))
                message.messageData[index++] = b;

            message.messageData[index++] = data.revision;
            foreach (byte b in BitConverter.GetBytes(data.build))
                message.messageData[index++] = b;

            return message;
        }
        
        // Connection messages are only handled by the server
        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            byte clientVersionMajor = message.messageData[0];
            byte clientVersionMinor = message.messageData[1];
            byte clientRevision = message.messageData.Length > 2 ? message.messageData[2] : (byte)0;
            ulong clientBuild = message.messageData.Length >= 3 + sizeof(ulong) ? BitConverter.ToUInt64(message.messageData, 3) : 0UL;

            // Ignore rapid repeats from the same user - they're a replayed backlog, not new joins
            if (lastConnectionTimes.TryGetValue(sender, out float lastTime) && Time.realtimeSinceStartup - lastTime < CONNECTION_DEBOUNCE_SECONDS)
                return;

            lastConnectionTimes[sender] = Time.realtimeSinceStartup;

            bool isSameVersion = clientVersionMajor == EntanglementVersion.versionMajor && clientVersionMinor == EntanglementVersion.versionMinor;

            EntangleLogger.Log($"A client connected with version '{clientVersionMajor}.{clientVersionMinor}.*'...");

            DisconnectMessageData disconnectData = new DisconnectMessageData();

            if (!isSameVersion) {
                if (clientVersionMajor < EntanglementVersion.minVersionMajorSupported || clientVersionMinor < EntanglementVersion.minVersionMinorSupported) {
                    EntangleLogger.Log($"A client was removed for having an outdated client!");
                    disconnectData.disconnectReason = (byte)DisconnectReason.OutdatedClient;
                }

                if (clientVersionMajor > EntanglementVersion.versionMajor || clientVersionMinor > EntanglementVersion.versionMinor) {
                    EntangleLogger.Log($"A client was removed for having too new of a client! Please update your mod!");
                    disconnectData.disconnectReason = (byte)DisconnectReason.OutdatedServer;
                }
            }
            else if (clientRevision != EntanglementVersion.protocolRevision) {
                bool older = clientRevision < EntanglementVersion.protocolRevision;
                string theirs = clientRevision == 0 ? "an older one" : "revision " + clientRevision;
                EntangleLogger.Warn($"{PlayerName(sender)} has a different build of Entanglement Redux ({theirs}, this one is revision {EntanglementVersion.protocolRevision}); turned away.");
                disconnectData.disconnectReason = (byte)(older ? DisconnectReason.OutdatedClient : DisconnectReason.OutdatedServer);
                disconnectData.additionalReason = "Different Entanglement Redux build: the host has revision "
                    + EntanglementVersion.protocolRevision + ", you have " + theirs + ". Everyone needs the same DLL.";
                Entanglement.UI.GameNotify.Show(PlayerName(sender) + " has a different build of Entanglement Redux, so they can't join. Everyone needs the same DLL.", 8f);
            }
            else {
                if (clientBuild != 0 && clientBuild != EntanglementVersion.BuildId) {
                    EntangleLogger.Warn($"{PlayerName(sender)} is on the same revision but a different DLL (theirs {clientBuild:x16}, this one {EntanglementVersion.BuildId:x16}).");
                    Entanglement.UI.GameNotify.Show(PlayerName(sender) + " has a different copy of Entanglement Redux. If anything acts up, give everyone the same DLL.", 8f);
                }
                if (Server.instance.connectedUsers.Count >= Server.maxPlayers) {
                    EntangleLogger.Log("A client was removed since the server is full!");
                    disconnectData.disconnectReason = (byte)DisconnectReason.ServerFull;
                }
            }

            if (BanList.bannedUsers.Any(tuple => tuple.Item1 == sender))
                disconnectData.disconnectReason = (byte)DisconnectReason.Banned;

            if (disconnectData.disconnectReason != (byte)DisconnectReason.Unknown) {
                EntangleLogger.Log($"Disconnecting sender for reason {Enum.GetName(typeof(DisconnectReason), disconnectData.disconnectReason)}...");
                NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
                Server.instance?.SendMessage(sender, NetworkChannel.Reliable, disconnectMsg.GetBytes());
            }
        }

        static string PlayerName(long userId) {
            try {
                string name;
                if (Node.activeNode != null && Node.activeNode.userNames.TryGetValue(userId, out name) && !string.IsNullOrEmpty(name))
                    return name;
            }
            catch { }
            return userId.ToString();
        }
    }

    public class ConnectionMessageData : NetworkMessageData {
        public ushort packedVersion;
        public byte revision;
        public ulong build;
    }
}
