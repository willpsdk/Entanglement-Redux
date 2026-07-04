using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MelonLoader;

using Entanglement.Data;
using Entanglement.Representation;
using Entanglement.Objects;

using Steamworks;

using UnityEngine;

namespace Entanglement.Network {
    public class Client : Node {
        // Static preferences
        public static bool nameTagsVisible = true;

        // There can only be one client, otherwise things will break
        public static Client instance = null;

        public static void StartClient()
        {
            if (instance != null)
                throw new Exception("Can't create another client instance!");

            EntangleLogger.Log($"Started client!");
            activeNode = instance = new Client();
        }

        //
        // Actual functionality
        //

        public long hostId;
        public string hostName = "Host";

        public byte currentScene = 0;

        // The value of the dict increases with time. When the server sends a heartbeat reset it to 0.
        // If its ever greater than a certain amount of seconds we should exit the server as the host has likely lost connection.
        public float hostHeartbeat;

        // Fired when the user accepts a lobby invite or clicks "Join Game" on a friend in the Steam overlay
        private Callback<GameLobbyJoinRequested_t> joinRequestedCallback;
        private CallResult<LobbyEnter_t> lobbyEnterResult;

        private Client() {
            joinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create((request) => JoinLobby(request.m_steamIDLobby));
            lobbyEnterResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
        }

        public void JoinLobby(CSteamID lobbyId) {
            if (SteamIntegration.hasLobby) {
                EntangleLogger.Error("You are already in a lobby!");
                return;
            }

            lobbyEnterResult.Set(SteamMatchmaking.JoinLobby(lobbyId));
        }

        public void OnLobbyEntered(LobbyEnter_t result, bool bIOFailure) {
            if (SteamIntegration.hasLobby) {
                EntangleLogger.Error("You are already in a lobby!");
                return;
            }

            if (bIOFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) {
                EntangleLogger.Error($"Failed to join the lobby! Response: {(EChatRoomEnterResponse)result.m_EChatRoomEnterResponse}");
                return;
            }

            SteamIntegration.lobby = new CSteamID(result.m_ulSteamIDLobby);

            RegisterLobbyCallbacks();

            hostId = SteamIntegration.lobbyOwnerId;
            SteamIntegration.FetchUser(hostId, OnHostUserFetched);

            // Create representations for everyone that is already inside of the lobby
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobby);
            for (int m = 0; m < memberCount; m++) {
                long userId = (long)SteamMatchmaking.GetLobbyMemberByIndex(SteamIntegration.lobby, m).m_SteamID;

                if (userId != SteamIntegration.currentUserId && userId != hostId)
                    CreatePlayerRep(userId);
            }

            SteamIntegration.UpdateActivity();

            ObjectSync.OnCleanup();

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = false;
        }

        public void OnHostUserFetched(long userId, string userName) {
            PlayerRepresentation.representations.Add(userId, new PlayerRepresentation(userName, userId));
            userNames.Add(userId, userName);

            hostName = userName;
            EntangleLogger.Log($"Joined {hostName}'s server!");
            EntangleNotif.JoinServer(hostName);

            // Test our connection by sending our connection message
            ConnectionMessageData connectionData = new ConnectionMessageData();
            connectionData.packedVersion = BitConverter.ToUInt16(new byte[] { EntanglementVersion.versionMajor, EntanglementVersion.versionMinor }, 0);

            NetworkMessage conMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Connection, connectionData);
            SendMessage(hostId, NetworkChannel.Reliable, conMsg.GetBytes());

            SteamIntegration.RegisterUser(hostId, 0);
        }

        public override void UserConnectedEvent(long userId) => SteamIntegration.UpdateActivity();

        public override void UserDisconnectEvent(long userId) => SteamIntegration.UpdateActivity();

        public void DisconnectFromServer(bool notif = true) {
            if (notif)
                EntangleNotif.LeftServer();

            if (SteamIntegration.hasLobby)
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobby);

            SteamIntegration.lobby = CSteamID.Nil; // Clear the lobby
            SteamIntegration.DefaultRichPresence();
            CleanData();
        }

        public override void BroadcastMessage(NetworkChannel channel, byte[] data) => SendMessage(hostId, channel, data);

        // Client.Shutdown is ran on closing the game
        public override void Shutdown() {
            DisconnectFromServer();
        }
    }
}
