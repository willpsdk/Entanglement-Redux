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
            activeNode = instance = new Client();
        }

        public CSteamID hostUser;
        public byte currentScene = 0;
        public float hostHeartbeat;

        protected Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        protected Callback<LobbyEnter_t> lobbyEnterCallback;

        private Client()
        {
            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        }

        public void JoinLobby(CSteamID lobbyToJoin)
        {
            if (SteamIntegration.hasLobby)
            {
                EntangleLogger.Error("Entanglement: Redux - You are already in a lobby!");
                return;
            }
            SteamMatchmaking.JoinLobby(lobbyToJoin);
        }

        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t pCallback)
        {
            JoinLobby(pCallback.m_steamIDLobby);
        }

        private void OnLobbyEntered(LobbyEnter_t result)
        {
            // If we are currently hosting the server, ignore this so we don't spawn a Ford for ourselves.
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

            EntangleLogger.Log($"Entanglement: Redux - Joined {SteamFriends.GetFriendPersonaName(hostUser)}'s server!");
            EntangleNotif.JoinServer(SteamFriends.GetFriendPersonaName(hostUser));

            ConnectToSteamServer();

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobbyId);
            for (int i = 0; i < memberCount; i++)
            {
                CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(SteamIntegration.lobbyId, i);
                if (memberId != SteamIntegration.currentUser && memberId != hostUser)
                {
                    // FIX: Replaced broken "CreatePlayerRep" call to properly spawn existing lobby members
                    if (!PlayerRepresentation.representations.ContainsKey(memberId.m_SteamID))
                    {
                        PlayerRepresentation.representations.Add(memberId.m_SteamID, new PlayerRepresentation(SteamFriends.GetFriendPersonaName(memberId), memberId.m_SteamID));
                    }
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

            ConnectionMessageData connectionData = new ConnectionMessageData();
            connectionData.packedVersion = BitConverter.ToUInt16(new byte[] { EntanglementVersion.versionMajor, EntanglementVersion.versionMinor }, 0);

            NetworkMessage conMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Connection, connectionData);
            SendMessage(hostUser.m_SteamID, NetworkChannel.Reliable, conMsg.GetBytes());

            SteamIntegration.RegisterUser(hostUser.m_SteamID, 0);
        }

        public override void UserConnectedEvent(ulong lobbyId, ulong userId)
        {
            SteamIntegration.UpdateActivity();
        }

        public override void UserDisconnectEvent(ulong lobbyId, ulong userId)
        {
            SteamIntegration.UpdateActivity();

            // FIX: Ensure player models are destroyed and references removed when a client leaves
            if (PlayerRepresentation.representations.ContainsKey(userId))
            {
                PlayerRepresentation.representations[userId].DeleteRepresentations();
                PlayerRepresentation.representations.Remove(userId);
            }
            SteamIntegration.RemoveUser(userId);
        }

        public void DisconnectFromServer(bool notif = true)
        {
            if (notif)
                EntangleNotif.LeftServer();

            if (SteamIntegration.hasLobby)
            {
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobbyId);
            }

            SteamIntegration.lobbyId = CSteamID.Nil;
            SteamIntegration.DefaultRichPresence();

            // FIX: Completely wipe local representations so stale holograms don't break subsequent connections
            foreach (var rep in PlayerRepresentation.representations.Values)
            {
                rep.DeleteRepresentations();
            }
            PlayerRepresentation.representations.Clear();
            SteamIntegration.byteIds.Clear();

            CleanData();
        }

        public override void BroadcastMessage(NetworkChannel channel, byte[] data) => SendMessage(hostUser.m_SteamID, channel, data);

        public override void Shutdown()
        {
            DisconnectFromServer();
        }
    }
}