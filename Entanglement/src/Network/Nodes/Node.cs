using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using MelonLoader;
using Steamworks;

using Entanglement.Representation;
using Entanglement.Compat.Playermodels;
using Entanglement.Objects;
using Entanglement.Compat;
using Entanglement.Data;

namespace Entanglement.Network
{
    public abstract class Node
    {
        public List<ulong> connectedUsers = new List<ulong>();
        public Dictionary<ulong, CSteamID> userDatas = new Dictionary<ulong, CSteamID>();

        // Reset per frame, but used in Entanglement -> Stats to see the network load
        public uint sentByteCount, recievedByteCount;

        public static Node activeNode;

        public static bool isServer => activeNode is Server;

        protected Callback<P2PSessionRequest_t> sessionRequestCallback;

        public Node()
        {
            sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
        }

        private void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            CSteamID remoteId = request.m_steamIDRemote;
            SteamNetworking.AcceptP2PSessionWithUser(remoteId);
        }

        public void ConnectToSteamServer()
        {
            SteamIntegration.UpdateVoice(SteamIntegration.voiceStatus);
        }

        public void OnSteamUserJoined(ulong lobbyId, ulong userId)
        {
            CreatePlayerRep(userId);

            if (PlayermodelsPatch.lastLoadedPath != null)
            {
                string path = PlayermodelsPatch.lastLoadedPath;
                LoadCustomPlayerMessageData msgData = new LoadCustomPlayerMessageData();
                msgData.userId = SteamIntegration.currentUser.m_SteamID;
                msgData.modelPath = Path.GetFileName(path);
                msgData.requestCallback = true;
                SendMessage(userId, NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.PlayerModel, msgData).GetBytes());
            }

            UserConnectedEvent(lobbyId, userId);
        }

        public void OnSteamUserLeft(ulong lobbyId, ulong userId)
        {
            if (PlayerRepresentation.representations.ContainsKey(userId))
            {
                EntangleNotif.PlayerLeave($"{PlayerRepresentation.representations[userId].playerName}");
                PlayerRepresentation.representations[userId].DeleteRepresentations();
                PlayerRepresentation.representations.Remove(userId);
            }

            userDatas.Remove(userId);
            connectedUsers.Remove(userId);
            SteamIntegration.RemoveUser(userId);

            SteamNetworking.CloseP2PSessionWithUser(new CSteamID(userId));

            UserDisconnectEvent(lobbyId, userId);
        }

        public void CreatePlayerRep(ulong userId)
        {
            if (connectedUsers.Contains(userId))
                return;

            connectedUsers.Add(userId);

            CSteamID steamId = new CSteamID(userId);
            string username = SteamFriends.GetFriendPersonaName(steamId);

            PlayerRepresentation.representations.Add(userId, new PlayerRepresentation(username, userId));
            userDatas.Add(userId, steamId);

            EntangleNotif.PlayerJoin($"{username}");
        }

        public void CleanData()
        {
            connectedUsers.Clear();
            userDatas.Clear();
            ObjectSync.OnCleanup();

            foreach (PlayerRepresentation playerRep in PlayerRepresentation.representations.Values)
                playerRep.DeleteRepresentations();

            PlayerRepresentation.representations.Clear();
            SteamIntegration.byteIds.Clear();
            SteamIntegration.lastByteId = 1;

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = PlayerScripts.reloadLevelOnDeath;

            CleanupEvent();
        }

        public void OnSteamMessageRecieved(ulong userId, byte channelId, byte[] data)
        {
            if (data.Length <= 0)
                return;

            try
            {
                NetworkMessage message = new NetworkMessage();

                message.messageType = data[0];
                message.messageData = new byte[data.Length - sizeof(byte)];

                for (int b = sizeof(byte); b < data.Length; b++)
                    message.messageData[b - sizeof(byte)] = data[b];

                recievedByteCount += (uint)data.Length;
                NetworkMessage.ReadMessage(message, userId);
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error processing message from {userId}: {ex.Message}");
            }
        }

        public void SendMessage(ulong userId, NetworkChannel channel, byte[] data)
        {
            if (!SteamIntegration.hasLobby)
                return;

            try
            {
                EP2PSend sendType = channel == NetworkChannel.Unreliable ? EP2PSend.k_EP2PSendUnreliable : EP2PSend.k_EP2PSendReliable;
                bool success = SteamNetworking.SendP2PPacket(new CSteamID(userId), data, (uint)data.Length, sendType, (int)channel);

                if (success)
                {
                    sentByteCount += (uint)data.Length;
                }
                else
                {
                    EntangleLogger.Error($"Failed to send P2P packet to user {userId}. Channel: {channel}, Size: {data.Length} bytes");

                    // If this is a client talking to server, we might want to disconnect
                    if (this is Client client && userId == client.hostUser.m_SteamID)
                    {
                        EntangleLogger.Verbose("Attempting to reconnect to host...");
                    }
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Exception in SendMessage to {userId}: {ex.Message}");
            }
        }

        public virtual void BroadcastMessage(NetworkChannel channel, byte[] data)
        {
            BroadcastMessageP2P(channel, data);
        }

        public virtual void BroadcastMessageP2P(NetworkChannel channel, byte[] data)
        {
            foreach (ulong user in connectedUsers.ToArray())
            {
                SendMessage(user, channel, data);
            }
        }

        public virtual void Tick()
        {
            for (int channel = 0; channel <= 4; channel++)
            {
                uint msgSize;
                while (SteamNetworking.IsP2PPacketAvailable(out msgSize, channel))
                {
                    byte[] data = new byte[msgSize];
                    uint bytesRead;
                    CSteamID remoteId;

                    if (SteamNetworking.ReadP2PPacket(data, msgSize, out bytesRead, out remoteId, channel))
                    {
                        OnSteamMessageRecieved(remoteId.m_SteamID, (byte)channel, data);
                    }
                }
            }
        }

        public virtual void UserConnectedEvent(ulong lobbyId, ulong userId) { }

        public virtual void UserDisconnectEvent(ulong lobbyId, ulong userId) { }

        public virtual void CleanupEvent() { }

        public virtual void Shutdown() { }
    }
}