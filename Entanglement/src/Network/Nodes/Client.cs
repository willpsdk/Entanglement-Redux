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

using StressLevelZero.Utilities;

using UnityEngine;

namespace Entanglement.Network {
    public class Client : Node {
        // Static preferences
        public static bool nameTagsVisible = true;

        // There can only be one client, otherwise things will break
        public static Client instance = null;

        static bool checkedCommandLine;

        public static void TryCommandLineJoin() {
            if (checkedCommandLine || instance == null)
                return;
            checkedCommandLine = true;

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) {
                if (args[i] != "+connect_lobby")
                    continue;
                CSteamID lobbyId = ParseConnectLobby(args[i + 1]);
                if (lobbyId != CSteamID.Nil)
                    instance.JoinLobby(lobbyId);
                return;
            }
        }

        public static void StartClient()
        {
            if (instance != null)
                throw new Exception("Can't create another client instance!");

            EntangleLogger.Log($"Started client!");
            activeNode = instance = new Client();
        }

        public long hostId;
        public string hostName = "Host";

        public byte currentScene = 0;
        public int pendingScene = -1;

        public float hostHeartbeat;

        // Fired when the user accepts a lobby invite or clicks "Join Game" on a friend in the Steam overlay
        private Callback<GameLobbyJoinRequested_t> joinRequestedCallback;
        private Callback<GameRichPresenceJoinRequested_t> richJoinCallback;
        private CallResult<LobbyEnter_t> lobbyEnterResult;

        private Client() {
            joinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create((request) => JoinLobby(request.m_steamIDLobby));
            richJoinCallback = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoin);
            lobbyEnterResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
        }

        void OnRichPresenceJoin(GameRichPresenceJoinRequested_t request) {
            CSteamID lobbyId = ParseConnectLobby(request.m_rgchConnect);
            if (lobbyId == CSteamID.Nil)
                return;
            JoinLobby(lobbyId);
        }

        public static CSteamID ParseConnectLobby(string connect) {
            if (string.IsNullOrEmpty(connect))
                return CSteamID.Nil;

            string[] parts = connect.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = parts.Length - 1; i >= 0; i--) {
                ulong id;
                if (ulong.TryParse(parts[i], out id) && id != 0)
                    return new CSteamID(id);
            }
            return CSteamID.Nil;
        }

        public void JoinLobby(CSteamID lobbyId) {
            if (SteamIntegration.hasLobby) {
                EntangleLogger.Error("You are already in a lobby!");
                return;
            }

            if (DownloadGate.TryDeferJoin(lobbyId))
                return;

            BeginSteamJoin(lobbyId);
        }

        public void BeginSteamJoin(CSteamID lobbyId) {
            if (SteamIntegration.hasLobby || lobbyId == CSteamID.Nil)
                return;

            lobbyEnterResult.Set(SteamMatchmaking.JoinLobby(lobbyId));
        }

        public void OnLobbyEntered(LobbyEnter_t result, bool bIOFailure) {
            if (SteamIntegration.hasLobby) {
                EntangleLogger.Error("You are already in a lobby!");
                return;
            }

            if (bIOFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) {
                EntangleLogger.Error($"Failed to join the lobby! Response: {(EChatRoomEnterResponse)result.m_EChatRoomEnterResponse}");
                Entanglement.UI.GameNotify.Show("Couldn't join that game. It may have closed or be full.", 6f);
                return;
            }

            SteamIntegration.lobby = new CSteamID(result.m_ulSteamIDLobby);

            if (!ModDependency.TryValidateLobbyMods(SteamIntegration.lobby, out string missingMods)) {
                int downloading = ModDependency.DownloadMissingFromThunderstore();
                if (downloading > 0) {
                    EntangleLogger.Error("Missing mods. Downloading " + downloading + " from Thunderstore: " + missingMods);
                    EntangleNotif.MissingMods(missingMods + "\nThose are code mods, so they need a restart after Thunderstore finishes.");
                    Entanglement.UI.GameNotify.Show("Getting " + missingMods + ". Restart when it is done, then rejoin.", 8f);
                }
                else {
                    EntangleLogger.Error("Missing required mods to join this lobby: " + missingMods);
                    EntangleNotif.MissingMods(missingMods);
                }
                Entanglement.UI.GameRadial.ForceOpenDownloads();
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobby);
                SteamIntegration.lobby = CSteamID.Nil;
                return;
            }

            int content = ModDependency.OfferMissingContent();
            if (content > 0) {
                CSteamID left = SteamIntegration.lobby;
                Entanglement.UI.GameNotify.Show("Please accept the host's packages. You will be able to join then :)", 8f);
                Entanglement.UI.GameRadial.ForceOpenDownloads();
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobby);
                SteamIntegration.lobby = CSteamID.Nil;
                DownloadGate.BeginDeferredJoin(left, true);
                return;
            }

            RegisterLobbyCallbacks();

            hostId = SteamIntegration.lobbyOwnerId;
            try { SteamNetworking.AcceptP2PSessionWithUser(new CSteamID((ulong)hostId)); } catch { }
            SteamIntegration.FetchUser(hostId, OnHostUserFetched);

            // Create representations for everyone that is already inside of the lobby
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobby);
            for (int m = 0; m < memberCount; m++) {
                long userId = (long)SteamMatchmaking.GetLobbyMemberByIndex(SteamIntegration.lobby, m).m_SteamID;

                if (userId != SteamIntegration.currentUserId && userId != hostId) {
                    try { SteamNetworking.AcceptP2PSessionWithUser(new CSteamID((ulong)userId)); } catch { }
                    CreatePlayerRep(userId);
                }
            }

            SteamIntegration.UpdateActivity();

            ObjectSync.OnCleanup();

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = false;

            Entanglement.Data.SpawnableData.EnsureLobbyUnlocks();

            LoadLobbySceneIfNeeded();
        }

        void LoadLobbySceneIfNeeded() {
            string mapFile = Entanglement.Sync.CustomMapSync.LobbyMapFile(SteamIntegration.lobby);
            if (!string.IsNullOrEmpty(mapFile)) {
                if (!Entanglement.Sync.CustomMapSync.HasLocalMap(mapFile))
                    Entanglement.Sync.CustomMapSync.RequestMapIfMissing(hostId, mapFile);
                else {
                    Entanglement.Patching.ClientSceneGate.AllowNetworkLoad();
                    Entanglement.Compat.CustomMaps.CustomMapsPatch.TryLoadMap(mapFile);
                }
                return;
            }

            int sceneIndex = SteamIntegration.LobbySceneIndex(SteamIntegration.lobby);
            if (sceneIndex < 0 || sceneIndex == BoneworksSceneManager.currentSceneIndex) {
                pendingScene = -1;
                return;
            }

            if (DownloadGate.TryHoldScene(sceneIndex, false))
                return;

            pendingScene = sceneIndex;
            Entanglement.Patching.ClientSceneGate.AllowNetworkLoad();
            BoneworksSceneManager.LoadScene(sceneIndex);
        }

        public void OnHostUserFetched(long userId, string userName) {
            if (!PlayerRepresentation.representations.ContainsKey(userId)) {
                PlayerRepresentation.representations.Add(userId, new PlayerRepresentation(userName, userId));
                Entanglement.Sync.PlayermodelSync.ApplyWanted(userId);
            }
            if (!userNames.ContainsKey(userId))
                userNames.Add(userId, userName);
            if (!connectedUsers.Contains(userId))
                connectedUsers.Add(userId);

            hostName = userName;
            EntangleLogger.Log($"Joined {hostName}'s server!");
            EntangleNotif.JoinServer(hostName);
            DownloadGate.OnJoinedServer();

            // Test our connection by sending our connection message
            ConnectionMessageData connectionData = new ConnectionMessageData();
            connectionData.packedVersion = BitConverter.ToUInt16(new byte[] { EntanglementVersion.versionMajor, EntanglementVersion.versionMinor }, 0);
            connectionData.revision = EntanglementVersion.protocolRevision;
            connectionData.build = EntanglementVersion.BuildId;
            CheckHostBuild();

            NetworkMessage conMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Connection, connectionData);
            SendMessage(hostId, NetworkChannel.Reliable, conMsg.GetBytes());

            SteamIntegration.RegisterUser(hostId, 0);
        }

        static void CheckHostBuild() {
            try {
                string tag = SteamMatchmaking.GetLobbyData(SteamIntegration.lobby, "build");
                byte revision;
                ulong build;
                if (!EntanglementVersion.TryParseBuildTag(tag, out revision, out build)) {
                    EntangleLogger.Warn("The host's lobby doesn't say which build of Entanglement Redux it runs; it is probably older than yours (revision " + EntanglementVersion.protocolRevision + ").");
                    return;
                }
                if (revision != EntanglementVersion.protocolRevision)
                    EntangleLogger.Warn("The host runs revision " + revision + " of Entanglement Redux, you run " + EntanglementVersion.protocolRevision + ".");
                else if (build != EntanglementVersion.BuildId) {
                    EntangleLogger.Warn("The host runs the same revision of Entanglement Redux from a different DLL (theirs " + build.ToString("x16") + ", yours " + EntanglementVersion.BuildId.ToString("x16") + ").");
                    Entanglement.UI.GameNotify.Show("The host has a different copy of Entanglement Redux. If anything acts up, use the same DLL.", 8f);
                }
            }
            catch { }
        }

        public override void UserConnectedEvent(long userId) => SteamIntegration.UpdateActivity();

        public override void UserDisconnectEvent(long userId) => SteamIntegration.UpdateActivity();

        public void DisconnectFromServer(bool notif = true) {
            if (notif)
                EntangleNotif.LeftServer();

            if (SteamIntegration.hasLobby) {
                EntangleLogger.Log($"Left {hostName}'s server.");
                EntangleLogger.Debug("[Join] Left from:\n" + Environment.StackTrace);
            }

            if (SteamIntegration.hasLobby)
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobby);

            SteamIntegration.lobby = CSteamID.Nil; // Clear the lobby
            SteamIntegration.DefaultRichPresence();
            DownloadGate.Reset();
            CleanData();
        }

        public override void BroadcastMessage(NetworkChannel channel, byte[] data) => SendMessage(hostId, channel, data);

        // Client.Shutdown is ran on closing the game
        public override void Shutdown() {
            DisconnectFromServer();
        }
    }
}
