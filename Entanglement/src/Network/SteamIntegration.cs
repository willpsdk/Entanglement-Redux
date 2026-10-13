using System;
using System.IO;
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

        /// <summary>Active Steam AppID after init (0 if unknown / game default).</summary>
        public static uint activeAppId;

        // The local user, fetched once SteamAPI initializes
        public static long currentUserId;
        public static string currentUserName = "Unknown";

        // The lobby we are currently inside of (hosting or connected), CSteamID.Nil when not in one
        public static CSteamID lobby = CSteamID.Nil;

        public static bool hasLobby => lobby.m_SteamID != 0;

        public static long lobbyOwnerId => hasLobby ? (long)SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID : 0L;

        public static bool isHost => hasLobby && lobbyOwnerId == currentUserId;

        public static bool isConnected => hasLobby && !isHost;

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

            if (!byteIds.TryGetValue(shortId, out long userId)) {
                if (shortId != 255)
                    AskHostForIds();
                return 0;
            }
            return userId;
        }

        public static byte GetByteId(long longId) {
            if (longId == 0)
                return 0;
            if (longId == currentUserId) {
                // 0 is the host's number: until ours arrives, the host puts it right from who sent it.
                if (localByteId == 0 && !isHost)
                    AskHostForIds();
                return localByteId;
            }

            foreach (KeyValuePair<byte, long> pair in byteIds) {
                if (pair.Value == longId)
                    return pair.Key;
            }
            // 0 is reserved for host - never invent a host mapping for unknown users.
            return 255;
        }

        public static byte CreateByteId() {
            for (int tries = 0; tries < 254; tries++) {
                byte id = lastByteId++;
                if (lastByteId == 255 || lastByteId == 0)
                    lastByteId = 1;
                if (id != 0 && id != 255 && !byteIds.ContainsKey(id))
                    return id;
            }
            return 255;
        }

        public static void RegisterUser(long userId, byte byteId) {
            // One number per player: a stale one would name them twice.
            List<byte> stale = null;
            foreach (KeyValuePair<byte, long> pair in byteIds) {
                if (pair.Value == userId && pair.Key != byteId)
                    (stale ?? (stale = new List<byte>())).Add(pair.Key);
            }
            if (stale != null)
                for (int i = 0; i < stale.Count; i++)
                    byteIds.Remove(stale[i]);
            byteIds[byteId] = userId;
        }

        // Someone coming back into the lobby keeps the number they had.
        public static byte RegisterUser(long userId) {
            foreach (KeyValuePair<byte, long> pair in byteIds) {
                if (pair.Value == userId)
                    return pair.Key;
            }
            byte byteId = CreateByteId();
            RegisterUser(userId, byteId);
            return byteId;
        }

        // Messages whose first byte says which player sent them.
        static readonly HashSet<byte> namesSender = new HashSet<byte> {
            BuiltInMessageType.PlayerRepSync, BuiltInMessageType.GunShot, BuiltInMessageType.SpawnObject,
            BuiltInMessageType.HandPose, BuiltInMessageType.GripRadius, BuiltInMessageType.BalloonShot,
            BuiltInMessageType.VoiceData,
        };
        static readonly Dictionary<long, float> retoldAt = new Dictionary<long, float>();

        /// <summary>Host only. A client can only speak for itself, so the number in its message is set to
        /// the one that sent it. A client that never got its number sends the host's (0): its body was
        /// dropped here and sent back to it as the host's, which pulled the host's rep into it.</summary>
        public static void CheckSenderId(NetworkMessage message, long sender) {
            if (!Node.isServer || sender == currentUserId || message.messageData == null || message.messageData.Length == 0)
                return;
            if (!namesSender.Contains(message.messageType))
                return;

            byte want = GetByteId(sender);
            byte sent = message.messageData[0];
            if (want == sent)
                return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            float at;
            if (!retoldAt.TryGetValue(sender, out at) || now - at > IdAskEvery) {
                retoldAt[sender] = now;
                string name;
                if (Node.activeNode == null || !Node.activeNode.userNames.TryGetValue(sender, out name))
                    name = sender.ToString();
                if (want == 255 && Node.activeNode != null && !Node.activeNode.connectedUsers.Contains(sender)
                    && IsLobbyMember(new CSteamID((ulong)sender))) {
                    // Their arrival never reached us (a quick leave and rejoin): take it as a join now.
                    EntangleLogger.Warn("[Join] Hearing from " + name + ", who is in the lobby but never joined here; adding them.");
                    Node.activeNode.OnUserJoined(sender);
                }
                else {
                    EntangleLogger.Warn("[Join] " + name + " sent as player number " + sent + " but is "
                        + (want == 255 ? "not numbered" : want.ToString()) + "; sending them the numbers again.");
                    Entanglement.Network.ShortIdMessageHandler.SendIdsTo(sender, false);
                }
                want = GetByteId(sender);
            }
            if (want != 255)
                message.messageData[0] = want;
        }

        const float IdAskEvery = 3f;
        static float nextIdAsk;

        /// <summary>We don't know our own number or someone else's: the host sends the list again.</summary>
        public static void AskHostForIds() {
            if (!hasLobby || isHost || Node.isServer || Client.instance == null)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextIdAsk)
                return;
            nextIdAsk = now + IdAskEvery;

            long host = lobbyOwnerId;
            if (host == 0)
                return;
            NetworkMessage ask = NetworkMessage.CreateMessage(BuiltInMessageType.ShortId,
                new ShortIdMessageData { userId = currentUserId, byteId = localByteId });
            if (ask != null)
                Client.instance.SendMessage(host, NetworkChannel.Reliable, ask.GetBytes());
            EntangleLogger.Debug("[Join] Asked the host for player numbers (ours is " + localByteId + ")");
        }

        public static void RemoveUser(long userId) {
            byte id = 255;
            foreach (KeyValuePair<byte, long> pair in byteIds) {
                if (pair.Value != userId)
                    continue;
                id = pair.Key;
                break;
            }
            // Never remove reserved host slot 0 unless it truly mapped to this user.
            if (id != 255)
                byteIds.Remove(id);
        }

        /// <summary>
        /// Initialize Steamworks. Pass SteamVR AppID (250820) for Meta Horizon Link;
        /// pass 0 to use the Steam client's game AppID (Boneworks).
        /// </summary>
        public static void Initialize(uint appId = 0) {
            try {
                if (appId != 0)
                    PrepareAppId(appId);

                if (!SteamAPI.Init()) {
                    EntangleLogger.Error("Failed to initialize the Steam API! Continuing without Entanglement!\nMake sure Steam is running and you are logged in, then launch the game through Steam."
                        + (appId != 0 ? "\nSteamVR AppID " + appId + " was requested (Meta Horizon Link)." : ""));
                    isInvalid = true;
                    return;
                }

                activeAppId = appId;
                currentUserId = (long)SteamUser.GetSteamID().m_SteamID;
                currentUserName = SteamFriends.GetPersonaName();

                EntangleLogger.Log($"Current Steam User: {currentUserName}"
                    + (appId != 0 ? $" (AppID {appId})" : " (game AppID)"));

                sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
                sessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnSessionConnectFail);
                personaStateCallback = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);

                DefaultRichPresence();
                DiscordInvite.Start();
            }
            catch (Exception e) {
                EntangleLogger.Error($"Failed to initialize the Steam API! Continuing without Entanglement!\nIs Steamworks.NET.dll and steam_api64.dll present, and is Steam running?\nFailed with reason: {e.Message}\nTrace: {e.StackTrace}");
                isInvalid = true;
            }
        }

        /// <summary>Write steam_appid.txt + SteamAppId env so Steamworks.NET binds SteamVR for Horizon Link.</summary>
        public static void PrepareAppId(uint appId) {
            activeAppId = appId;
            try {
                Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
                Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Steam] Could not set SteamAppId env: " + e.Message);
            }

            try {
                string dir = MelonUtils.GameDirectory;
                if (string.IsNullOrEmpty(dir))
                    dir = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(dir, "steam_appid.txt");
                File.WriteAllText(path, appId.ToString());
                EntangleLogger.Log("[Steam] Prepared AppID " + appId + " at " + path);
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Steam] Could not write steam_appid.txt: " + e.Message);
            }
        }


        // Only accept P2P sessions from users that are inside of our lobby
        private static void OnSessionRequest(P2PSessionRequest_t request) {
            if (hasLobby && IsLobbyMember(request.m_steamIDRemote))
                SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
        }

        private static void OnSessionConnectFail(P2PSessionConnectFail_t fail) {
            // Sessions from a previous lobby time out after leaving, only worth reporting in one
            if (hasLobby)
                EntangleLogger.Warn($"P2P session with {fail.m_steamIDRemote.m_SteamID} failed with error {(EP2PSessionError)fail.m_eP2PSessionError}!");
        }

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

        // When a pending persona fetch gives up and we fall back to a placeholder name.
        const float personaTimeout = 6f;
        static readonly Dictionary<ulong, float> pendingUserFetchTimes = new Dictionary<ulong, float>();

        public static void FetchUser(long userId, Action<long, string> callback) {
            CSteamID steamId = new CSteamID((ulong)userId);

            if (!SteamFriends.RequestUserInformation(steamId, false)) {
                // Already cached locally
                callback(userId, SafeName(steamId, userId));
                return;
            }

            if (!pendingUserFetches.TryGetValue(steamId.m_SteamID, out var callbacks)) {
                callbacks = new List<Action<long, string>>();
                pendingUserFetches.Add(steamId.m_SteamID, callbacks);
                pendingUserFetchTimes[steamId.m_SteamID] = UnityEngine.Time.realtimeSinceStartup;
            }

            callbacks.Add(callback);
        }

        /// <summary>Never hand back an empty name - a blank player list row reads as a bug.</summary>
        static string SafeName(CSteamID steamId, long userId) {
            string name = null;
            try { name = SteamFriends.GetFriendPersonaName(steamId); }
            catch { }

            if (string.IsNullOrEmpty(name) || name == "[unknown]")
                return "Player " + (userId % 100000L);

            return name;
        }

        /// <summary>
        /// Resolves persona fetches Steam never answered. Called once a frame from Tick.
        /// </summary>
        public static void TickUserFetches() {
            if (pendingUserFetches.Count == 0)
                return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            List<ulong> expired = null;

            foreach (KeyValuePair<ulong, float> pair in pendingUserFetchTimes) {
                if (now - pair.Value < personaTimeout)
                    continue;
                if (expired == null)
                    expired = new List<ulong>();
                expired.Add(pair.Key);
            }

            if (expired == null)
                return;

            for (int i = 0; i < expired.Count; i++) {
                ulong id = expired[i];
                pendingUserFetchTimes.Remove(id);

                if (!pendingUserFetches.TryGetValue(id, out var callbacks))
                    continue;
                pendingUserFetches.Remove(id);

                long userId = (long)id;
                string name = SafeName(new CSteamID(id), userId);
                EntangleLogger.Warn("[Steam] Persona for " + userId + " never arrived; using '" + name + "'.");

                foreach (var callback in callbacks) {
                    try { callback(userId, name); }
                    catch { }
                }
            }
        }

        private static void OnPersonaStateChange(PersonaStateChange_t change) {
            if (!pendingUserFetches.TryGetValue(change.m_ulSteamID, out var callbacks))
                return;

            pendingUserFetches.Remove(change.m_ulSteamID);
            pendingUserFetchTimes.Remove(change.m_ulSteamID);

            long userId = (long)change.m_ulSteamID;
            string name = SafeName(new CSteamID(change.m_ulSteamID), userId);

            foreach (var callback in callbacks)
                callback(userId, name);
        }

        public static bool SendPacket(long userId, NetworkChannel channel, byte[] data) {
            // Every channel except Unreliable expects ordered & lossless delivery
            EP2PSend sendType = channel == NetworkChannel.Unreliable ? EP2PSend.k_EP2PSendUnreliable : EP2PSend.k_EP2PSendReliable;

            return SteamNetworking.SendP2PPacket(new CSteamID((ulong)userId), data, (uint)data.Length, sendType, (int)channel);
        }

        public static void CloseSession(long userId) {
            SteamNetworking.CloseP2PSessionWithUser(new CSteamID((ulong)userId));
        }

        public static void LobbyOccupancy(CSteamID lobbyId, out int current, out int limit, out int open) {
            current = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            limit = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
            if (limit <= 0)
                limit = Math.Max(current, (int)Server.maxPlayers);
            if (limit < current)
                limit = current;
            open = limit - current;
        }

        public static string OpenSlotLabel(int current, int limit) {
            int open = limit - current;
            if (open < 0)
                open = 0;
            if (limit <= 0)
                return current.ToString();
            if (open == 0)
                return current + "/" + limit + ", full";
            if (open == 1)
                return current + "/" + limit + ", 1 slot open";
            return current + "/" + limit + ", " + open + " slots open";
        }

        public static string OpenSlotLabel(CSteamID lobbyId) {
            LobbyOccupancy(lobbyId, out int current, out int limit, out int open);
            if (SteamMatchmaking.GetLobbyData(lobbyId, "locked") == "1")
                return current + "/" + limit + ", locked";
            return OpenSlotLabel(current, limit);
        }

        public static void DefaultRichPresence() {
            string scene = CurrentSceneName();
            SteamFriends.ClearRichPresence();
            SteamFriends.SetRichPresence("status", DiscordInvite.PlayingLine(scene, false, 0, 0));
            DiscordInvite.SetSolo(scene);
        }

        // Shortcut method for refreshing Steam presence and the Discord invite card
        public static void UpdateActivity() {
            if (!hasLobby) {
                DefaultRichPresence();
                return;
            }

            LobbyOccupancy(lobby, out int memberCount, out int memberLimit, out _);
            bool locked = isHost ? Server.isLocked : SteamMatchmaking.GetLobbyData(lobby, "locked") == "1";
            ServerVisibility visibility = Server.visibility;
            if (!isHost) {
                ServerVisibility parsed;
                if (Enum.TryParse(SteamMatchmaking.GetLobbyData(lobby, "visibility"), out parsed))
                    visibility = parsed;
            }

            bool listed = !locked && visibility != ServerVisibility.Private;
            string status = DiscordInvite.PlayingLine(CurrentSceneName(), listed, memberCount, memberLimit);

            SteamFriends.SetRichPresence("status", status);
            SteamFriends.SetRichPresence("steam_player_group", lobby.m_SteamID.ToString());
            SteamFriends.SetRichPresence("steam_player_group_size", memberCount.ToString());
            if (listed && memberCount < memberLimit)
                SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobby.m_SteamID);
            else
                SteamFriends.SetRichPresence("connect", null);

            // Keep the lobby browser entry up to date for the host
            if (isHost) {
                SteamMatchmaking.SetLobbyData(lobby, "scene", CurrentSceneName());
                SteamMatchmaking.SetLobbyData(lobby, "scene_id", targetScene ?? "");
                SteamMatchmaking.SetLobbyData(lobby, "scene_index", StressLevelZero.Utilities.BoneworksSceneManager.currentSceneIndex.ToString());
                SteamMatchmaking.SetLobbyData(lobby, "locked", Server.isLocked ? "1" : "0");
                SteamMatchmaking.SetLobbyData(lobby, "visibility", Server.visibility.ToString());
                SteamMatchmaking.SetLobbyData(lobby, "slowmo", SlowMoSync.Mode.ToString());
                SteamMatchmaking.SetLobbyData(lobby, "teleport", Server.clientTeleport ? "1" : "0");
                ModDependency.PublishHostRequirements();
            }

            // Discord join secret is the Steam lobby id. Private lobbies do not advertise a join.
            DiscordInvite.SetLobby(lobby.m_SteamID, memberCount, memberLimit, locked, CurrentSceneName(), visibility);
        }

        public static string CurrentSceneName() {
            string map = Entanglement.Sync.CustomMapSync.PlayingTitle;
            if (!string.IsNullOrEmpty(map))
                return map;

            string scene = ParseScene(targetScene);
            if (string.IsNullOrEmpty(scene) || scene == "undefined")
                return "";
            return scene;
        }

        public static int LobbySceneIndex(CSteamID lobbyId) {
            if (lobbyId == CSteamID.Nil)
                return -1;

            int index;
            string rawIndex = SteamMatchmaking.GetLobbyData(lobbyId, "scene_index");
            if (int.TryParse(rawIndex, out index) && index >= 0)
                return index;

            string sceneId = SteamMatchmaking.GetLobbyData(lobbyId, "scene_id");
            if (!string.IsNullOrEmpty(sceneId)) {
                index = Entanglement.Patching.LevelChangeAnnouncer.ResolveSceneIndex(sceneId);
                if (index >= 0)
                    return index;
            }

            return -1;
        }

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

        static float nextGhostSweep;
        static float nextPresenceRefresh;

        public static void Tick() {
            SteamAPI.RunCallbacks();
            DiscordInvite.DrainJoins();

            if (hasLobby && !isHost && UnityEngine.Time.realtimeSinceStartup >= nextPresenceRefresh) {
                nextPresenceRefresh = UnityEngine.Time.realtimeSinceStartup + 10f;
                try { UpdateActivity(); }
                catch (Exception e) { EntangleLogger.Debug("[Discord] Presence refresh: " + e.Message); }
            }

            TickUserFetches();

            if (hasLobby && UnityEngine.Time.realtimeSinceStartup >= nextGhostSweep) {
                nextGhostSweep = UnityEngine.Time.realtimeSinceStartup + 5f;
                try { Node.SweepGhostReps(); }
                catch (Exception e) { EntangleLogger.Warn("[Join] Ghost sweep: " + e.Message); }
            }

            if (hasLobby)
                Node.activeNode?.ReceiveMessages();
        }

        public static void Shutdown() {
            DiscordInvite.Stop();
            try { SteamFriends.ClearRichPresence(); }
            catch { }
            SteamAPI.Shutdown();
        }
    }
}
