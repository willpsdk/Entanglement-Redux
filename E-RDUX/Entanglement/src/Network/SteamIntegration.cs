using System;
using System.Linq;
using System.Collections.Generic;

using Steamworks;

using MelonLoader;

using Entanglement.Extensions;

namespace Entanglement.Network {
    public static class SteamIntegration {
        public const string notHosting = "This user isn't hosting a game!";

        public static string targetScene = "undefined";

        public static bool isInvalid;

        // The local user, fetched once SteamAPI initializes
        public static long currentUserId;
        public static string currentUserName = "Unknown";

        // The lobby we are currently inside of (hosting or connected), CSteamID.Nil when not in one
        public static CSteamID lobby = CSteamID.Nil;

        public static bool hasLobby => lobby.m_SteamID != 0;

        public static long lobbyOwnerId => hasLobby ? (long)SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID : 0L;

        public static bool isHost => hasLobby && lobbyOwnerId == currentUserId;

        public static bool isConnected => hasLobby && lobbyOwnerId != currentUserId;

        public static Dictionary<byte, long> byteIds = new Dictionary<byte, long>();

        // An ID of 0 is reserved for the host
        public static byte localByteId = 0;
        public static byte lastByteId = 1;

        // Kept alive for the lifetime of the game, Steamworks callbacks unregister when collected
        private static Callback<P2PSessionRequest_t> sessionRequestCallback;
        private static Callback<P2PSessionConnectFail_t> sessionConnectFailCallback;
        private static Callback<PersonaStateChange_t> personaStateCallback;

        // Users whose persona name hasn't downloaded yet, resolved via PersonaStateChange_t
        private static Dictionary<ulong, List<Action<long, string>>> pendingUserFetches = new Dictionary<ulong, List<Action<long, string>>>();

        public static long GetLongId(byte shortId) {
            if (shortId == 0) return lobbyOwnerId;

            return byteIds.TryIdx(shortId);
        }

        public static byte GetByteId(long longId) {
            if (longId == currentUserId) return localByteId;

            return byteIds.FirstOrDefault(o => o.Value == longId).Key;
        }

        public static byte CreateByteId() => lastByteId++;

        public static void RegisterUser(long userId, byte byteId) => byteIds.Add(byteId, userId);

        public static byte RegisterUser(long userId) {
            byte byteId = CreateByteId();
            RegisterUser(userId, byteId);
            return byteId;
        }

        public static void RemoveUser(long userId) => byteIds.Remove(GetByteId(userId));

        public static void Initialize() {
            try {
                if (!SteamAPI.Init()) {
                    EntangleLogger.Error("Failed to initialize the Steam API! Continuing without Entanglement!\nMake sure Steam is running and you are logged in, then launch the game through Steam.");
                    isInvalid = true;
                    return;
                }

                currentUserId = (long)SteamUser.GetSteamID().m_SteamID;
                currentUserName = SteamFriends.GetPersonaName();

                EntangleLogger.Log($"Current Steam User: {currentUserName}");

                sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
                sessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnSessionConnectFail);
                personaStateCallback = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);

                DefaultRichPresence();
            }
            catch (Exception e) {
                EntangleLogger.Error($"Failed to initialize the Steam API! Continuing without Entanglement!\nIs Steamworks.NET.dll and steam_api64.dll present, and is Steam running?\nFailed with reason: {e.Message}\nTrace: {e.StackTrace}");
                isInvalid = true;
            }
        }

        // Only accept P2P sessions from users that are inside of our lobby
        private static void OnSessionRequest(P2PSessionRequest_t request) {
            if (hasLobby && IsLobbyMember(request.m_steamIDRemote))
                SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
        }

        private static void OnSessionConnectFail(P2PSessionConnectFail_t fail) =>
            EntangleLogger.Warn($"P2P session with {fail.m_steamIDRemote.m_SteamID} failed with error {(EP2PSessionError)fail.m_eP2PSessionError}!");

        public static bool IsLobbyMember(CSteamID user) {
            if (!hasLobby) return false;

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int m = 0; m < memberCount; m++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, m) == user)
                    return true;

            return false;
        }

        public static string GetUserName(long userId) {
            if (userId == currentUserId) return currentUserName;

            return SteamFriends.GetFriendPersonaName(new CSteamID((ulong)userId));
        }

        // Replaces Discord's async UserManager.GetUser, persona names of strangers download asynchronously
        public static void FetchUser(long userId, Action<long, string> callback) {
            CSteamID steamId = new CSteamID((ulong)userId);

            if (!SteamFriends.RequestUserInformation(steamId, true)) {
                // Name is already cached locally
                callback(userId, SteamFriends.GetFriendPersonaName(steamId));
                return;
            }

            if (!pendingUserFetches.TryGetValue(steamId.m_SteamID, out var callbacks)) {
                callbacks = new List<Action<long, string>>();
                pendingUserFetches.Add(steamId.m_SteamID, callbacks);
            }

            callbacks.Add(callback);
        }

        private static void OnPersonaStateChange(PersonaStateChange_t change) {
            if (!pendingUserFetches.TryGetValue(change.m_ulSteamID, out var callbacks))
                return;

            pendingUserFetches.Remove(change.m_ulSteamID);

            string name = SteamFriends.GetFriendPersonaName(new CSteamID(change.m_ulSteamID));
            long userId = (long)change.m_ulSteamID;

            foreach (var callback in callbacks)
                callback(userId, name);
        }

        //
        // Transport
        //

        public static bool SendPacket(long userId, NetworkChannel channel, byte[] data) {
            // Every channel except Unreliable expects ordered & lossless delivery
            EP2PSend sendType = channel == NetworkChannel.Unreliable ? EP2PSend.k_EP2PSendUnreliable : EP2PSend.k_EP2PSendReliable;

            return SteamNetworking.SendP2PPacket(new CSteamID((ulong)userId), data, (uint)data.Length, sendType, (int)channel);
        }

        public static void CloseSession(long userId) => SteamNetworking.CloseP2PSessionWithUser(new CSteamID((ulong)userId));

        //
        // Rich presence
        //

        public static void DefaultRichPresence() {
            SteamFriends.ClearRichPresence();
            SteamFriends.SetRichPresence("status", $"Playing solo (Entanglement v{EntanglementMod.VersionString})");
        }

        // Shortcut method for refreshing the user's Steam rich presence
        public static void UpdateActivity() {
            if (!hasLobby) {
                DefaultRichPresence();
                return;
            }

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobby);
            int memberLimit = SteamMatchmaking.GetLobbyMemberLimit(lobby);

            SteamFriends.SetRichPresence("status", $"{(isHost ? "Hosting" : "Playing")} {ParseScene(targetScene)} ({memberCount}/{memberLimit}) - Entanglement v{EntanglementMod.VersionString}");
            SteamFriends.SetRichPresence("steam_player_group", lobby.m_SteamID.ToString());
            SteamFriends.SetRichPresence("steam_player_group_size", memberCount.ToString());

            // Keep the lobby browser entry up to date for the host
            if (isHost)
                SteamMatchmaking.SetLobbyData(lobby, "scene", ParseScene(targetScene));
        }

        /// <summary>
        /// Parses the scene name to fit the actual name in the menu
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        public static string ParseScene(string scene) {
            switch (scene.ToLower()) {
                default:
                    return scene;
                case "sandbox_blankbox":
                    return "Blankbox";
                case "scene_redactedchamber":
                    return "[REDACTED] Chamber";
                case "scene_mainmenu":
                    return "Main Menu";
                case "scene_breakroom":
                    return "Breakroom";
                case "scene_streets":
                    return "Streets";
                case "scene_tuscany":
                    return "Tuscany";
                case "zombie_warehouse":
                    return "Zombie Warehouse";
                case "scene_throneroom":
                    return "Throne Room";
                case "scene_runoff":
                    return "Runoff";
                case "scene_arena":
                    return "Arena Campaign";
                case "arena_fantasy":
                    return "Arena Gamemode";
                case "sandbox_museumbasement":
                    return "Museum Basement";
                case "sandbox_handgunbox":
                    return "Handgun Range";
                case "scene_hoverjunkers":
                    return "Hover Junkers";
                case "scene_tower":
                    return "Tower";
                case "scene_warehouse":
                    return "Warehouse";
                case "scene_towerboss":
                    return "Time Tower";
                case "scene_sewerstation":
                    return "Sewers";
                case "scene_museum":
                    return "Museum";
                case "scene_dungeon":
                    return "Dungeon";
                case "scene_subwaystation":
                    return "Central Station";
            }
        }

        public static void Tick() {
            SteamAPI.RunCallbacks();

            // Steam has no message callback, incoming packets are polled every frame instead
            Node.activeNode?.ReceiveMessages();
        }

        public static void Shutdown() {
            SteamFriends.ClearRichPresence();
            SteamAPI.Shutdown();
        }
    }
}
