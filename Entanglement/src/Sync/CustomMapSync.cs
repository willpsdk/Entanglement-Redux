using System;
using System.Collections.Generic;
using System.IO;

using Entanglement.Network;
using Entanglement.Compat;
using Entanglement.Compat.CustomMaps;

using Steamworks;

namespace Entanglement.Sync
{
    public static class CustomMapSync
    {
        public const string LobbyMapFileKey = "map_file";
        public const string LobbyMapPackageKey = "map_ts";

        static readonly HashSet<string> requestedFiles = new HashSet<string>();
        static bool initialized;
        static string currentMapFile;
        static bool announcedWithMap;
        static string playingTitle;
        static int mapEpoch;
        static int seenMapEpoch;

        public static string PlayingTitle => playingTitle;
        public static string CurrentMapFile => currentMapFile;
        public static bool HasCurrentMap => !string.IsNullOrEmpty(currentMapFile);

        public static void Initialize() {
            if (initialized) return;
            initialized = true;

            FileTransferManager.RegisterCategoryHandler(FileTransferCategory.CustomMap, OnMapFileReceived, OnMapFileFailed);
            Thunderstore.RegisterInstaller(FileTransferCategory.CustomMap, OnThunderstoreReady);
        }

        public static string LobbyMapFile(CSteamID lobbyId) {
            if (lobbyId == CSteamID.Nil)
                return "";
            return Path.GetFileName(SteamMatchmaking.GetLobbyData(lobbyId, LobbyMapFileKey) ?? "");
        }

        public static bool HasLocalMap(string mapFileName) {
            if (string.IsNullOrEmpty(mapFileName))
                return false;
            return File.Exists(Path.Combine(CustomMapsPatch.customMapsPath, Path.GetFileName(mapFileName)));
        }

        // Remembers the map even when solo so Start Server can advertise it to joiners.
        public static void NoteLocalMap(string mapFileName) {
            mapFileName = ResolveMapFileName(mapFileName);
            if (string.IsNullOrEmpty(mapFileName))
                return;
            currentMapFile = mapFileName;
            announcedWithMap = true;
            SetPlaying(mapFileName);
        }

        // Host writes the map into lobby data before the scene load, so a joiner can download it first.
        public static void BeginHostMap(string mapFileName) {
            NoteLocalMap(mapFileName);
            if (string.IsNullOrEmpty(currentMapFile))
                return;
            EntangleLogger.Log("[CustomMapSync] Host loading custom map " + currentMapFile);
            PublishLobbyMap();
        }

        // Call after lobby create if the host already loaded a map while solo.
        public static void PublishLobbyMap() {
            if (string.IsNullOrEmpty(currentMapFile))
                return;
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                return;

            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, LobbyMapFileKey, currentMapFile);
            string packed = "";
            ThunderstorePackage package;
            string fullPath = Path.Combine(CustomMapsPatch.customMapsPath, currentMapFile);
            if (File.Exists(fullPath) && Thunderstore.TryMatchFile(fullPath, out package))
                packed = package.author + "/" + package.name + "/" + package.version;
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, LobbyMapPackageKey, packed);
            EntangleLogger.Log("[CustomMapSync] Lobby map_file=" + currentMapFile + (packed.Length > 0 ? " ts=" + packed : " (local/P2P)"));
        }

        public static string ResolveMapFileName(string pathOrName) {
            if (string.IsNullOrEmpty(pathOrName))
                return "";
            string file = Path.GetFileName(pathOrName);
            if (file.EndsWith(".bcm", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cma", StringComparison.OrdinalIgnoreCase))
                return file;
            try {
                if (Directory.Exists(CustomMapsPatch.customMapsPath)) {
                    string[] files = Directory.GetFiles(CustomMapsPatch.customMapsPath);
                    for (int i = 0; i < files.Length; i++) {
                        string name = Path.GetFileName(files[i]);
                        if (string.Equals(Path.GetFileNameWithoutExtension(name), file, StringComparison.OrdinalIgnoreCase))
                            return name;
                    }
                }
            }
            catch { }
            return file;
        }

        public static void SetPlaying(string fileName) {
            string title = TitleFromFile(fileName);
            if (string.IsNullOrEmpty(title))
                return;
            playingTitle = title;
            mapEpoch++;
            RefreshPresence();
        }

        public static bool ConsumeMapShellAnnounce() {
            if (!announcedWithMap)
                return false;
            announcedWithMap = false;
            if (string.IsNullOrEmpty(currentMapFile))
                return false;
            EntangleLogger.Log("[CustomMapSync] Custom map uses a blank Boneworks scene. Clients will get the map offer instead of that scene index.");
            return true;
        }

        public static void OnIncomingLevel() {
            if (mapEpoch != seenMapEpoch) {
                seenMapEpoch = mapEpoch;
                return;
            }
            ClearPlaying();
        }

        public static void ClearPlaying() {
            if (string.IsNullOrEmpty(playingTitle))
                return;
            playingTitle = "";
            RefreshPresence();
        }

        static void RefreshPresence() {
            try { SteamIntegration.UpdateActivity(); }
            catch { }
        }

        public static void OnHostSceneAnnounced() {
            currentMapFile = "";
            announcedWithMap = false;
            ClearPlaying();
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                return;
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, LobbyMapFileKey, "");
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, LobbyMapPackageKey, "");
        }

        static string TitleFromFile(string fileName) {
            string stem = Path.GetFileNameWithoutExtension(fileName ?? "");
            if (string.IsNullOrEmpty(stem))
                return "Custom Map";

            var built = new System.Text.StringBuilder(stem.Length);
            bool cap = true;
            for (int i = 0; i < stem.Length; i++) {
                char c = stem[i];
                if (c == '_' || c == '-' || c == ' ') {
                    if (built.Length > 0 && built[built.Length - 1] != ' ')
                        built.Append(' ');
                    cap = true;
                    continue;
                }
                if (cap && c >= 'a' && c <= 'z')
                    c = (char)(c - 32);
                cap = false;
                built.Append(c);
            }
            string title = built.ToString().Trim();
            if (title.Length == 0)
                return "Custom Map";
            if (title.Length > 40)
                return title.Substring(0, 40);
            return title;
        }

        public static void SendCurrentTo(long userId) {
            if (string.IsNullOrEmpty(currentMapFile) || !(Node.activeNode is Server))
                return;

            NetworkMessage message = NetworkMessage.CreateMessage(CompatMessageType.CustomMap, new LoadCustomMapMessageData {
                mapPath = currentMapFile,
            });
            if (message != null)
                Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
        }

        public static void ClearRequested(string mapFileName) {
            if (string.IsNullOrEmpty(mapFileName))
                return;
            requestedFiles.Remove(Path.GetFileName(mapFileName));
        }

        public static void RequestMapIfMissing(long hostUserId, string mapFileName) {
            if (!SyncPrefs.mapSyncEnabled.Value) return;
            if (!SteamIntegration.hasLobby) return;
            if (SyncPrefs.IsUserBlocked(hostUserId)) return;

            if (string.IsNullOrEmpty(mapFileName)) return;
            mapFileName = Path.GetFileName(mapFileName);
            DownloadGate.ExpectMap(mapFileName);

            string fullPath = Path.Combine(CustomMapsPatch.customMapsPath, mapFileName);
            if (File.Exists(fullPath)) {
                NoteLocalMap(mapFileName);
                Entanglement.Patching.ClientSceneGate.AllowNetworkLoad();
                CustomMapsPatch.TryLoadMap(mapFileName);
                return;
            }

            if (!requestedFiles.Add(mapFileName))
                return;

            EntangleLogger.Log("[CustomMapSync] Missing map " + mapFileName + ", asking the host for it");
            Entanglement.UI.GameNotify.Show("Host is on " + mapFileName + ". Waiting to see if they can send it.", 6f);

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MapSyncRequest, new MapSyncRequestData { mapFileName = mapFileName });
            if (message != null)
                Node.activeNode?.SendMessage(hostUserId, NetworkChannel.Reliable, message.GetBytes());
        }

        public static void OnMapRequested(long requester, string mapFileName) {
            if (!SyncPrefs.mapSyncEnabled.Value) return;
            if (SyncPrefs.IsUserBlocked(requester)) return;

            if (!Node.isServer) {
                EntangleLogger.Log("[CustomMapSync] Not sending '" + mapFileName + "' - only the host sends content");
                return;
            }

            mapFileName = Path.GetFileName(mapFileName);
            string fullPath = Path.Combine(CustomMapsPatch.customMapsPath, mapFileName);
            if (!File.Exists(fullPath)) {
                EntangleLogger.Warn($"[CustomMapSync] {requester} asked for '{mapFileName}' but we don't have that file");
                return;
            }

            if (SyncPrefs.IsPathBlacklisted(mapFileName)) {
                EntangleLogger.Log("[CustomMapSync] Not sending " + mapFileName + " - blacklisted");
                return;
            }

            // Not a Thunderstore package: sent directly if it is small enough.
            void SendDirect() {
                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length / 1024 > SyncPrefs.maxSyncSizeKB.Value) {
                    EntangleLogger.Log("[CustomMapSync] Not sending " + mapFileName + " - it is over the sync limit and it is not a Thunderstore package");
                    Entanglement.UI.GameNotify.Show(mapFileName + " is too big to send. Put it on Thunderstore instead.", 8f);
                    return;
                }

                EntangleLogger.Log("[CustomMapSync] Offering P2P transfer of " + mapFileName + " to " + requester);
                FileTransferManager.SendFile(requester, fullPath, FileTransferCategory.CustomMap);
            }

            if (Thunderstore.TryOfferFile(requester, fullPath, FileTransferCategory.CustomMap, "", SendDirect))
                return;
            SendDirect();
        }

        static void OnMapFileReceived(FileTransfer transfer) {
            string destPath = Path.Combine(CustomMapsPatch.customMapsPath, transfer.fileName);

            try {
                FileTransferManager.WriteReceivedFile(transfer, destPath);
            }
            catch (Exception e) {
                EntangleLogger.Error($"[CustomMapSync] Failed writing {destPath}: {e.Message}");
                return;
            }

            requestedFiles.Remove(transfer.fileName);
            SetPlaying(transfer.fileName);
            CustomMapsPatch.AddToMenu(transfer.fileName);
            EntangleLogger.Log("[CustomMapSync] Saved map " + transfer.fileName + ", loading it");
            Entanglement.Patching.ClientSceneGate.AllowNetworkLoad();
            CustomMapsPatch.TryLoadMap(transfer.fileName);
        }

        static void OnThunderstoreReady(string fileName, string note) {
            requestedFiles.Remove(fileName);
            SetPlaying(fileName);
            CustomMapsPatch.AddToMenu(fileName);
            EntangleLogger.Log("[CustomMapSync] Loaded map from Thunderstore: " + fileName);
            Entanglement.Patching.ClientSceneGate.AllowNetworkLoad();
            try {
                CustomMapsPatch.TryLoadMap(fileName);
            }
            catch (Exception e) {
                EntangleLogger.Warn("[CustomMapSync] Saved " + fileName + " but it did not load: " + e.Message);
                Entanglement.UI.GameNotify.Show("Saved " + fileName + " but it would not load. " + e.Message, 8f);
            }
        }

        static void OnMapFileFailed(FileTransfer transfer) {
            if (transfer == null) return;
            EntangleLogger.Warn($"[CustomMapSync] Failed to receive map '{transfer.fileName}' from {transfer.peer}");
            requestedFiles.Remove(transfer.fileName);
        }
    }
}
