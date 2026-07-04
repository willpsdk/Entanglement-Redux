using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using MelonLoader;

using Steamworks;

using Entanglement.Representation;
using Entanglement.Compat.Playermodels;
using Entanglement.Objects;
using Entanglement.Compat;
using Entanglement.Data;

namespace Entanglement.Network {
    public abstract class Node {
        public List<long> connectedUsers = new List<long>();
        public Dictionary<long, string> userNames = new Dictionary<long, string>();

        // Reset per frame, but used in Entanglement -> Stats to see the network load
        public uint sentByteCount, recievedByteCount;

        public static Node activeNode;

        public static bool isServer => activeNode is Server;

        // Every channel is polled for incoming packets each frame
        public static readonly NetworkChannel[] allChannels = {
            NetworkChannel.Reliable,
            NetworkChannel.Unreliable,
            NetworkChannel.Attack,
            NetworkChannel.Object,
            NetworkChannel.Transaction
        };

        protected Callback<LobbyChatUpdate_t> lobbyChatUpdateCallback;

        // Subscribes to lobby member joins/leaves, the Steam equivalent of Discord's OnMemberConnect/OnMemberDisconnect
        public void RegisterLobbyCallbacks() {
            if (lobbyChatUpdateCallback == null)
                lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        }

        public void OnLobbyChatUpdate(LobbyChatUpdate_t update) {
            if (update.m_ulSteamIDLobby != SteamIntegration.lobby.m_SteamID)
                return;

            long userId = (long)update.m_ulSteamIDUserChanged;

            if (userId == SteamIntegration.currentUserId)
                return;

            if ((update.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
                OnUserJoined(userId);
            else
                OnUserLeft(userId);
        }

        public void OnUserJoined(long userId) {
            CreatePlayerRep(userId);

            // Send PlayerModel
            if (PlayermodelsPatch.lastLoadedPath != null) {
                string path = PlayermodelsPatch.lastLoadedPath;
                LoadCustomPlayerMessageData msgData = new LoadCustomPlayerMessageData();
                msgData.userId = SteamIntegration.currentUserId;
                msgData.modelPath = Path.GetFileName(path);
                msgData.requestCallback = true;
                SendMessage(userId, NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.PlayerModel, msgData).GetBytes());
            }

            UserConnectedEvent(userId);
        }

        public void OnUserLeft(long userId) {
            if (PlayerRepresentation.representations.TryGetValue(userId, out PlayerRepresentation rep)) {
                EntangleNotif.PlayerLeave($"{rep.playerName}");

                rep.DeleteRepresentations();
                PlayerRepresentation.representations.Remove(userId);
            }

            userNames.Remove(userId);
            connectedUsers.Remove(userId);
            SteamIntegration.RemoveUser(userId);
            SteamIntegration.CloseSession(userId);

            UserDisconnectEvent(userId);
        }

        public void CreatePlayerRep(long userId)
        {
            if (connectedUsers.Contains(userId))
                return;

            connectedUsers.Add(userId);
            SteamIntegration.FetchUser(userId, OnUserFetched);
        }

        public void CleanData() {
            foreach (long user in connectedUsers)
                SteamIntegration.CloseSession(user);

            connectedUsers.Clear();
            userNames.Clear();
            ObjectSync.OnCleanup();

            foreach (PlayerRepresentation playerRep in PlayerRepresentation.representations.Values)
                playerRep.DeleteRepresentations();

            PlayerRepresentation.representations.Clear();
            SteamIntegration.byteIds.Clear();
            SteamIntegration.localByteId = 0;
            SteamIntegration.lastByteId = 1;

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = PlayerScripts.reloadLevelOnDeath;

            if (lobbyChatUpdateCallback != null) {
                lobbyChatUpdateCallback.Dispose();
                lobbyChatUpdateCallback = null;
            }

            CleanupEvent();
        }

        public void OnUserFetched(long userId, string userName) {
            if (PlayerRepresentation.representations.ContainsKey(userId))
                return;

            PlayerRepresentation.representations.Add(userId, new PlayerRepresentation(userName, userId));
            userNames.Add(userId, userName);

            EntangleNotif.PlayerJoin($"{userName}");
        }

        // Steam P2P packets are polled rather than pushed, this pump runs once a frame from SteamIntegration.Tick
        public void ReceiveMessages() {
            foreach (NetworkChannel channel in allChannels) {
                while (SteamNetworking.IsP2PPacketAvailable(out uint packetSize, (int)channel)) {
                    byte[] buffer = new byte[packetSize];

                    if (!SteamNetworking.ReadP2PPacket(buffer, packetSize, out uint bytesRead, out CSteamID remoteId, (int)channel))
                        continue;

                    OnMessageReceived((long)remoteId.m_SteamID, buffer);
                }
            }
        }

        public void OnMessageReceived(long userId, byte[] data)
        {
            if (data.Length <= 0) // Idk
                throw new Exception("Data was invalid!");

            NetworkMessage message = new NetworkMessage();

            message.messageType = data[0];
            message.messageData = new byte[data.Length - sizeof(byte)];

            for (int b = sizeof(byte); b < data.Length; b++)
                message.messageData[b - sizeof(byte)] = data[b];

            recievedByteCount += (uint)data.Length;
            NetworkMessage.ReadMessage(message, userId);
        }

        public void SendMessage(long userId, NetworkChannel channel, byte[] data) {
            if (SteamIntegration.hasLobby) {
                SteamIntegration.SendPacket(userId, channel, data);
                sentByteCount += (uint)data.Length;
            }
        }

        // Sends to owner if client
        // Sends to all if server
        public virtual void BroadcastMessage(NetworkChannel channel, byte[] data) { }

        // Forces send in every direction (for P2P-like messages, lowers latency but not good for certain things!)
        public void BroadcastMessageP2P(NetworkChannel channel, byte[] data) {
            connectedUsers.ForEach((user) => { SendMessage(user, channel, data); });

            if (!isServer)
                SendMessage(SteamIntegration.lobbyOwnerId, channel, data);
        }

        public virtual void Tick() { }

        public virtual void UserConnectedEvent(long userId) { }

        public virtual void UserDisconnectEvent(long userId) { }

        public virtual void CleanupEvent() { }

        // The active node's shutdown is called upon closing the game
        public virtual void Shutdown() { }
    }
}
