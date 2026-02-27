using System;
using System.Collections.Generic;
using System.Linq;

using MelonLoader;
using Steamworks;
using UnityEngine;

using Entanglement.Data;
using Entanglement.Representation;
using Entanglement.Objects;

namespace Entanglement.Network
{
    public class Client : Node
    {
        public static bool nameTagsVisible = true;
        public static Client instance = null;

        public static void StartClient()
        {
            if (instance != null)
                throw new Exception("Can't create another client instance!");

            EntangleLogger.Log($"Entanglement: Redux - Started client!");
            EntangleLogger.Verbose("StartClient() called - Initializing Client node.");
            activeNode = instance = new Client();
        }

        public CSteamID hostUser;
        public byte currentScene = 0;
        public float hostHeartbeat;

        private float heartbeatTimer = 0f;
        private const float HEARTBEAT_INTERVAL = 5f;
        private const float HEARTBEAT_TIMEOUT = 40f;

        protected Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        protected Callback<LobbyEnter_t> lobbyEnterCallback;

        private Client()
        {
            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        }

        public void JoinLobby(CSteamID lobbyToJoin)
        {
            EntangleLogger.Verbose($"JoinLobby() called for Lobby ID: {lobbyToJoin.m_SteamID}");

            if (Server.instance != null)
            {
                EntangleLogger.Log("Entanglement: Redux - Shutting down local server to join a new lobby...");
                Server.instance.Shutdown();
            }
            else if (SteamIntegration.hasLobby)
            {
                EntangleLogger.Log("Entanglement: Redux - Leaving current lobby to join a new one...");
                DisconnectFromServer(false);
            }

            EntangleLogger.Verbose("Requesting SteamMatchmaking.JoinLobby...");
            SteamMatchmaking.JoinLobby(lobbyToJoin);
        }

        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t pCallback)
        {
            EntangleLogger.Verbose($"Lobby join requested via Steam overlay/invite. Lobby ID: {pCallback.m_steamIDLobby}");
            JoinLobby(pCallback.m_steamIDLobby);
        }

        private void OnLobbyEntered(LobbyEnter_t result)
        {
            EntangleLogger.Verbose($"OnLobbyEntered callback fired. EChatRoomEnterResponse: {result.m_EChatRoomEnterResponse}");

            if (Server.instance != null)
                return;

            if (result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                EntangleLogger.Error("Entanglement: Redux - Failed to join Steam Lobby!");
                return;
            }

            SteamIntegration.lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            SteamIntegration.hostUser = SteamMatchmaking.GetLobbyOwner(SteamIntegration.lobbyId);
            hostUser = SteamIntegration.hostUser;

            EntangleLogger.Verbose($"Successfully joined lobby. Host resolved to SteamID: {hostUser.m_SteamID}");
            EntangleLogger.Log($"Entanglement: Redux - Joined {SteamFriends.GetFriendPersonaName(hostUser)}'s server!");
            EntangleNotif.JoinServer(SteamFriends.GetFriendPersonaName(hostUser));

            ConnectToSteamServer();

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobbyId);
            EntangleLogger.Verbose($"Fetching existing {memberCount} lobby members to create representations...");

            for (int i = 0; i < memberCount; i++)
            {
                CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(SteamIntegration.lobbyId, i);
                if (memberId != SteamIntegration.currentUser && memberId != hostUser)
                {
                    CreatePlayerRep(memberId.m_SteamID);
                }
            }

            SteamIntegration.UpdateActivity();
            ObjectSync.OnCleanup();

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = false;

            if (!PlayerRepresentation.representations.ContainsKey(hostUser.m_SteamID))
            {
                PlayerRepresentation.representations.Add(hostUser.m_SteamID, new PlayerRepresentation(SteamFriends.GetFriendPersonaName(hostUser), hostUser.m_SteamID));
            }

            EntangleLogger.Verbose("Constructing and sending BuiltInMessageType.Connection to Host...");
            ConnectionMessageData connectionData = new ConnectionMessageData();
            connectionData.packedVersion = BitConverter.ToUInt16(new byte[] { EntanglementVersion.versionMajor, EntanglementVersion.versionMinor }, 0);

            NetworkMessage conMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Connection, connectionData);
            SendMessage(hostUser.m_SteamID, NetworkChannel.Reliable, conMsg.GetBytes());

            SteamIntegration.RegisterUser(hostUser.m_SteamID, 0);
        }

        public override void UserConnectedEvent(ulong lobbyId, ulong userId)
        {
            EntangleLogger.Verbose($"[Client] UserConnectedEvent fired for SteamID: {userId}");
            SteamIntegration.UpdateActivity();
        }

        public override void UserDisconnectEvent(ulong lobbyId, ulong userId)
        {
            EntangleLogger.Verbose($"[Client] UserDisconnectEvent fired for SteamID: {userId}. Cleaning up representations.");
            SteamIntegration.UpdateActivity();

            if (PlayerRepresentation.representations.ContainsKey(userId))
            {
                PlayerRepresentation.representations[userId].DeleteRepresentations();
                PlayerRepresentation.representations.Remove(userId);
            }
            SteamIntegration.RemoveUser(userId);
        }

        public void DisconnectFromServer(bool notif = true)
        {
            EntangleLogger.Verbose("DisconnectFromServer() called - Cleaning up client state.");

            if (notif)
                EntangleNotif.LeftServer();

            if (SteamIntegration.hasLobby)
            {
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobbyId);
            }

            SteamIntegration.lobbyId = CSteamID.Nil;
            SteamIntegration.DefaultRichPresence();

            foreach (var rep in PlayerRepresentation.representations.Values)
            {
                rep.DeleteRepresentations();
            }
            PlayerRepresentation.representations.Clear();
            SteamIntegration.byteIds.Clear();

            CleanData();
        }

        public override void BroadcastMessage(NetworkChannel channel, byte[] data) => SendMessage(hostUser.m_SteamID, channel, data);

        public override void Tick()
        {
            // Handle heartbeat sending and timeout detection
            heartbeatTimer += Time.deltaTime;
            hostHeartbeat += Time.deltaTime;

            if (heartbeatTimer >= HEARTBEAT_INTERVAL)
            {
                heartbeatTimer = 0f;
                NetworkMessage heartbeatMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Heartbeat, new EmptyMessageData());
                SendMessage(hostUser.m_SteamID, NetworkChannel.Unreliable, heartbeatMsg.GetBytes());
            }

            // Check if host has timed out
            if (hostHeartbeat > HEARTBEAT_TIMEOUT)
            {
                EntangleLogger.Error($"Server host timed out after {HEARTBEAT_TIMEOUT} seconds. Disconnecting...");
                DisconnectFromServer(true);
                return;
            }

            base.Tick();
        }

        public override void Shutdown()
        {
            EntangleLogger.Verbose("Client Shutdown() called. Unregistering Steam callbacks.");
            DisconnectFromServer();

            if (lobbyJoinRequested != null)
            {
                lobbyJoinRequested.Unregister();
                lobbyJoinRequested.Dispose();
                lobbyJoinRequested = null;
            }
            if (lobbyEnterCallback != null)
            {
                lobbyEnterCallback.Unregister();
                lobbyEnterCallback.Dispose();
                lobbyEnterCallback = null;
            }
        }
    }
}