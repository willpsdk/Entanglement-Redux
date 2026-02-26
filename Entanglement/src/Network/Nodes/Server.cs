using System;
using System.Collections.Generic;

using MelonLoader;
using Steamworks;
using UnityEngine;

using Entanglement.Representation;
using Entanglement.Data;
using StressLevelZero;

namespace Entanglement.Network
{
    public enum LobbyType { Private, FriendsOnly, Public }

    public class Server : Node
    {
        public static byte maxPlayers = 8;
        public static bool isLocked = false;
        public static LobbyType lobbyType = LobbyType.Public;

        public const byte serverMinimum = 1;
        public const byte serverCapacity = 250;

        public Dictionary<ulong, float> userBeats = new Dictionary<ulong, float>();

        public static Server instance = null;
        private static float lastServerStart = 0f;

        protected CallResult<LobbyCreated_t> lobbyCreatedCallback;

        public static void StartServer()
        {
            if (Time.realtimeSinceStartup - lastServerStart < 3f)
            {
                EntangleLogger.Log("Please wait a moment before starting another server.");
                return;
            }
            lastServerStart = Time.realtimeSinceStartup;

            if (instance != null)
                instance.Shutdown();

            if (SteamIntegration.isConnected)
            {
                EntangleLogger.Error("Already in a server!");
                return;
            }

            // FIX: Ensure clean slate when restarting servers to prevent ghost ID crashes
            foreach (var rep in PlayerRepresentation.representations.Values)
            {
                rep.DeleteRepresentations();
            }
            PlayerRepresentation.representations.Clear();
            SteamIntegration.byteIds.Clear();

            EntangleLogger.Log($"Started a new server instance!");
            activeNode = instance = new Server();

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = false;
        }

        private Server()
        {
            lobbyCreatedCallback = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);

            ELobbyType eType = ELobbyType.k_ELobbyTypePrivate;
            if (!isLocked)
            {
                eType = lobbyType == LobbyType.Public ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly;
            }

            EntangleLogger.Log($"Creating a Steam lobby with a capacity of {maxPlayers} players!");
            SteamAPICall_t call = SteamMatchmaking.CreateLobby(eType, maxPlayers);
            lobbyCreatedCallback.Set(call);
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool bIOFailure)
        {
            if (result.m_eResult != EResult.k_EResultOK || bIOFailure)
            {
                EntangleLogger.Error($"Failed to create lobby! Reason: {result.m_eResult}");
                return;
            }

            SteamIntegration.lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            SteamIntegration.hostUser = SteamIntegration.currentUser;

            SteamMatchmaking.SetLobbyData(SteamIntegration.lobbyId, "name", $"{SteamFriends.GetPersonaName()}'s Server");
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobbyId, "version", EntanglementMod.VersionString);

            SteamIntegration.UpdateActivity();

            ConnectToSteamServer();
        }

        public override void Tick()
        {
            if (EntanglementMod.sceneChange != null)
            {
                EntangleLogger.Log($"Notifying clients of scene change to {EntanglementMod.sceneChange}...");

                LevelChangeMessageData levelChangeData = new LevelChangeMessageData() { sceneIndex = (byte)EntanglementMod.sceneChange, sceneReload = true };
                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.LevelChange, levelChangeData);

                byte[] msgBytes = message.GetBytes();
                foreach (ulong user in connectedUsers)
                    SendMessage(user, NetworkChannel.Reliable, msgBytes);

                EntanglementMod.sceneChange = null;
            }

            base.Tick();
        }

        public void UpdateLobbyConfig()
        {
            if (!SteamIntegration.hasLobby) return;

            ELobbyType eType = ELobbyType.k_ELobbyTypePrivate;
            if (!isLocked)
            {
                eType = lobbyType == LobbyType.Public ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly;
            }

            SteamMatchmaking.SetLobbyType(SteamIntegration.lobbyId, eType);
            SteamMatchmaking.SetLobbyMemberLimit(SteamIntegration.lobbyId, maxPlayers);

            if (maxPlayers < connectedUsers.Count)
            {
                uint usersToDisconnect = (uint)connectedUsers.Count - maxPlayers;

                DisconnectMessageData disconnectData = new DisconnectMessageData();
                disconnectData.disconnectReason = (byte)DisconnectReason.ServerFull;

                NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
                byte[] disconnectBytes = disconnectMsg.GetBytes();

                for (int i = 0; i < usersToDisconnect; i++)
                    SendMessage(connectedUsers[i], NetworkChannel.Reliable, disconnectBytes);
            }
        }

        public void CloseLobby()
        {
            DisconnectMessageData disconnectData = new DisconnectMessageData();
            disconnectData.disconnectReason = (byte)DisconnectReason.ServerClosed;

            NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
            byte[] disconnectBytes = disconnectMsg.GetBytes();
            foreach (ulong user in connectedUsers)
            {
                SendMessage(user, NetworkChannel.Reliable, disconnectBytes);
            }

            if (SteamIntegration.hasLobby)
            {
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobbyId);
            }
            SteamIntegration.lobbyId = CSteamID.Nil;

            // FIX: Wipe Server representations on closure
            foreach (var rep in PlayerRepresentation.representations.Values)
            {
                rep.DeleteRepresentations();
            }
            PlayerRepresentation.representations.Clear();
            SteamIntegration.byteIds.Clear();

            CleanData();
        }

        public override void Shutdown()
        {
            if (SteamIntegration.hasLobby && !SteamIntegration.isHost)
            {
                EntangleLogger.Error("Unable to close the server as a client!");
                return;
            }

            CloseLobby();
            SteamIntegration.DefaultRichPresence();

            instance = null;
            activeNode = Client.instance;
        }

        public override void UserConnectedEvent(ulong lobbyId, ulong userId)
        {
            LevelChangeMessageData levelChangeData = new LevelChangeMessageData() { sceneIndex = (byte)StressLevelZero.Utilities.BoneworksSceneManager.currentSceneIndex };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.LevelChange, levelChangeData);
            SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());

            SteamIntegration.UpdateActivity();

            foreach (KeyValuePair<byte, ulong> valuePair in SteamIntegration.byteIds)
            {
                if (valuePair.Value == userId) continue;

                ShortIdMessageData addMessageData = new ShortIdMessageData()
                {
                    userId = valuePair.Value,
                    byteId = valuePair.Key,
                };
                NetworkMessage addMessage = NetworkMessage.CreateMessage((byte)BuiltInMessageType.ShortId, addMessageData);
                SendMessage(userId, NetworkChannel.Reliable, addMessage.GetBytes());
            }

            ShortIdMessageData idMessageData = new ShortIdMessageData()
            {
                userId = userId,
                byteId = SteamIntegration.RegisterUser(userId)
            };
            NetworkMessage idMessage = NetworkMessage.CreateMessage((byte)BuiltInMessageType.ShortId, idMessageData);
            BroadcastMessage(NetworkChannel.Reliable, idMessage.GetBytes());

            userBeats.Add(userId, 0f);
        }

        public override void UserDisconnectEvent(ulong lobbyId, ulong userId)
        {
            SteamIntegration.UpdateActivity();
            userBeats.Remove(userId);

            // FIX: Properly remove users out of the server array when they crash/disconnect
            if (PlayerRepresentation.representations.ContainsKey(userId))
            {
                PlayerRepresentation.representations[userId].DeleteRepresentations();
                PlayerRepresentation.representations.Remove(userId);
            }
            SteamIntegration.RemoveUser(userId);
        }

        public override void BroadcastMessage(NetworkChannel channel, byte[] data) => BroadcastMessageP2P(channel, data);

        public void BroadcastMessageExcept(NetworkChannel channel, byte[] data, ulong toIgnore) => connectedUsers.ForEach((user) => {
            if (user != toIgnore)
            {
                SendMessage(user, channel, data);
            }
        });

        public void KickUser(ulong userId, string userName = null, DisconnectReason reason = DisconnectReason.Kicked)
        {
            DisconnectMessageData disconnectData = new DisconnectMessageData();
            disconnectData.disconnectReason = (byte)reason;

            NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
            byte[] disconnectBytes = disconnectMsg.GetBytes();

            SendMessage(userId, NetworkChannel.Reliable, disconnectBytes);

            if (userName != null)
                EntangleLogger.Log($"Kicked {userName} from the server.");
        }

        public void TeleportTo(ulong userId)
        {
            if (PlayerRepresentation.representations.ContainsKey(userId))
            {
                PlayerRepresentation rep = PlayerRepresentation.representations[userId];

                PlayerScripts.playerRig.Teleport(rep.repRoot.position);
                PlayerScripts.playerRig.physicsRig.ResetHands(Handedness.BOTH);
            }
        }
    }
}