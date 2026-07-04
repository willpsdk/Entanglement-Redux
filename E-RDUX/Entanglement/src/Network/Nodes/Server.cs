using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

using MelonLoader;

using Steamworks;

using Entanglement.Representation;
using Entanglement.Data;

using StressLevelZero;

using UnityEngine;

namespace Entanglement.Network
{
    // Maps onto Steam's ELobbyType, kept as its own enum so BoneMenu can display it
    public enum ServerVisibility : byte {
        Private,
        FriendsOnly,
        Public,
    }

    public class Server : Node {
        // Static preferences
        public static byte maxPlayers = 8;
        public static bool isLocked = false;
        public static ServerVisibility visibility = ServerVisibility.Private;

        // Hard locked settings
        public const byte serverMinimum = 1;
        public const byte serverCapacity = 250; // Steam lobbies max out at 250 members

        // The value of the dict increases with time. When a user sends a heartbeat reset it to 0.
        // If its ever greater than a certain amount of seconds we should disconnect them as they have likely lost connection.
        public Dictionary<long, float> userBeats = new Dictionary<long, float>();

        // There can only be one server, otherwise things will break
        public static Server instance = null;

        public static void StartServer() {
            if (instance != null)
                instance.Shutdown();

            if (SteamIntegration.isConnected) {
                EntangleLogger.Error("Already in a server!");
                return;
            }

            EntangleLogger.Log($"Started a new server instance!");
            activeNode = instance = new Server();

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = false;
        }

        //
        // Actual code below
        //

        private CallResult<LobbyCreated_t> lobbyCreatedResult;

        private Server() {
            EntangleLogger.Log($"Creating a Steam lobby with a capacity of {maxPlayers} players!");

            lobbyCreatedResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyCreatedResult.Set(SteamMatchmaking.CreateLobby(GetLobbyType(), maxPlayers));
        }

        public static ELobbyType GetLobbyType() {
            switch (visibility) {
                default:
                case ServerVisibility.Private:
                    return ELobbyType.k_ELobbyTypePrivate;
                case ServerVisibility.FriendsOnly:
                    return ELobbyType.k_ELobbyTypeFriendsOnly;
                case ServerVisibility.Public:
                    return ELobbyType.k_ELobbyTypePublic;
            }
        }

        public void OnLobbyCreated(LobbyCreated_t result, bool bIOFailure)
        {
            if (bIOFailure || result.m_eResult != EResult.k_EResultOK) {
                EntangleLogger.Error($"Failed to create a Steam lobby with result {result.m_eResult}!");

                instance = null;
                activeNode = Client.instance;
                return;
            }

            SteamIntegration.lobby = new CSteamID(result.m_ulSteamIDLobby);

            // Lobby metadata, used by the lobby browser to filter and display servers
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, "entanglement", "true");
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, "version", EntanglementMod.VersionString);
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, "host_name", SteamIntegration.currentUserName);
            SteamMatchmaking.SetLobbyJoinable(SteamIntegration.lobby, !isLocked);

            RegisterLobbyCallbacks();

            SteamIntegration.UpdateActivity();
        }

        public override void Tick() {
            if (EntanglementMod.sceneChange != null) {
                EntangleLogger.Log($"Notifying clients of scene change to {EntanglementMod.sceneChange}...");

                LevelChangeMessageData levelChangeData = new LevelChangeMessageData() { sceneIndex = (byte)EntanglementMod.sceneChange, sceneReload = true };
                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.LevelChange, levelChangeData);

                byte[] msgBytes = message.GetBytes();
                foreach (long user in connectedUsers)
                    SendMessage(user, NetworkChannel.Reliable, msgBytes);

                EntanglementMod.sceneChange = null;
            }

            base.Tick();
        }

        public void UpdateLobbyConfig() {
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                return;

            SteamMatchmaking.SetLobbyType(SteamIntegration.lobby, GetLobbyType());
            SteamMatchmaking.SetLobbyMemberLimit(SteamIntegration.lobby, maxPlayers);
            SteamMatchmaking.SetLobbyJoinable(SteamIntegration.lobby, !isLocked);

            SteamIntegration.UpdateActivity();

            if (maxPlayers < connectedUsers.Count) {
                uint usersToDisconnect = (uint)connectedUsers.Count - maxPlayers;

                DisconnectMessageData disconnectData = new DisconnectMessageData();
                disconnectData.disconnectReason = (byte)DisconnectReason.ServerFull;

                NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
                byte[] disconnectBytes = disconnectMsg.GetBytes();

                for (int i = 0; i < usersToDisconnect; i++)
                    SendMessage(connectedUsers[i], NetworkChannel.Reliable, disconnectBytes);
            }
        }

        public void CloseLobby() {
            DisconnectMessageData disconnectData = new DisconnectMessageData();
            disconnectData.disconnectReason = (byte)DisconnectReason.ServerClosed;

            NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
            byte[] disconnectBytes = disconnectMsg.GetBytes();
            foreach (long user in connectedUsers) {
                SendMessage(user, NetworkChannel.Reliable, disconnectBytes);
            }

            if (SteamIntegration.hasLobby)
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobby);

            SteamIntegration.lobby = CSteamID.Nil;

            CleanData();
        }

        public override void Shutdown() {
            if (SteamIntegration.hasLobby && !SteamIntegration.isHost) {
                EntangleLogger.Error("Unable to close the server as a client!");
                return;
            }

            CloseLobby();
            SteamIntegration.DefaultRichPresence();

            instance = null;
            activeNode = Client.instance;
        }

        public override void UserConnectedEvent(long userId) {
            // currentSceneIndex shouldn't ever be larger than 255 so a byte is fine
            LevelChangeMessageData levelChangeData = new LevelChangeMessageData() { sceneIndex = (byte)StressLevelZero.Utilities.BoneworksSceneManager.currentSceneIndex };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.LevelChange, levelChangeData);
            SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());

            SteamIntegration.UpdateActivity();

            foreach (KeyValuePair<byte, long> valuePair in SteamIntegration.byteIds) {
                if (valuePair.Value == userId) continue;

                ShortIdMessageData addMessageData = new ShortIdMessageData()
                {
                    userId = valuePair.Value,
                    byteId = valuePair.Key,
                };
                NetworkMessage addMessage = NetworkMessage.CreateMessage((byte)BuiltInMessageType.ShortId, addMessageData);
                SendMessage(userId, NetworkChannel.Reliable, addMessage.GetBytes());
            }

            ShortIdMessageData idMessageData = new ShortIdMessageData() {
                userId = userId,
                byteId = SteamIntegration.RegisterUser(userId)
            };
            NetworkMessage idMessage = NetworkMessage.CreateMessage((byte)BuiltInMessageType.ShortId, idMessageData);
            BroadcastMessage(NetworkChannel.Reliable, idMessage.GetBytes());

            userBeats.Add(userId, 0f);
        }

        public override void UserDisconnectEvent(long userId) {
            SteamIntegration.UpdateActivity();

            userBeats.Remove(userId);
        }

        public override void BroadcastMessage(NetworkChannel channel, byte[] data) => BroadcastMessageP2P(channel, data);

        // Unique to a server host; allows preventing a message sent to the host being sent back
        public void BroadcastMessageExcept(NetworkChannel channel, byte[] data, long toIgnore) => connectedUsers.ForEach((user) => {
            if (user != toIgnore) {
                SendMessage(user, channel, data);
            }
        });

        public void KickUser(long userId, string userName = null, DisconnectReason reason = DisconnectReason.Kicked) {
            DisconnectMessageData disconnectData = new DisconnectMessageData();
            disconnectData.disconnectReason = (byte)reason;

            NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
            byte[] disconnectBytes = disconnectMsg.GetBytes();

            SendMessage(userId, NetworkChannel.Reliable, disconnectBytes);

            if (userName != null)
                EntangleLogger.Log($"Kicked {userName} from the server.");
        }

        public void TeleportTo(long userId) {
            if (PlayerRepresentation.representations.ContainsKey(userId)) {
                PlayerRepresentation rep = PlayerRepresentation.representations[userId];

                PlayerScripts.playerRig.Teleport(rep.repRoot.position);
                PlayerScripts.playerRig.physicsRig.ResetHands(Handedness.BOTH);
            }
        }
    }
}
