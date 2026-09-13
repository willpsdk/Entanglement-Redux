using System;
using System.Collections.Generic;
using System.IO;

using Entanglement.Network;
using Entanglement.Compat.CustomMaps;

namespace Entanglement.Sync
{
    // Host broadcasts a custom map filename. Clients missing the file request it over P2P,
    // save into CustomMaps/, then TryLoadMap. Downloads always go through consent.
    public static class CustomMapSync
    {
        static readonly HashSet<string> requestedFiles = new HashSet<string>();
        static bool initialized;

        public static void Initialize() {
            if (initialized) return;
            initialized = true;

            FileTransferManager.RegisterCategoryHandler(FileTransferCategory.CustomMap, OnMapFileReceived, OnMapFileFailed);
        }

        public static void RequestMapIfMissing(long hostUserId, string mapFileName) {
            if (!SyncPrefs.mapSyncEnabled.Value) return;
            if (!SteamIntegration.hasLobby) return;
            if (SyncPrefs.IsUserBlocked(hostUserId)) return;

            if (string.IsNullOrEmpty(mapFileName)) return;
            mapFileName = Path.GetFileName(mapFileName);

            string fullPath = Path.Combine(CustomMapsPatch.customMapsPath, mapFileName);
            if (File.Exists(fullPath)) {
                CustomMapsPatch.TryLoadMap(mapFileName);
                return;
            }

            if (!requestedFiles.Add(mapFileName))
                return;

            EntangleLogger.Log($"[CustomMapSync] Missing map '{mapFileName}', asking {hostUserId} for it");

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MapSyncRequest, new MapSyncRequestData { mapFileName = mapFileName });
            if (message != null)
                Node.activeNode?.SendMessage(hostUserId, NetworkChannel.Reliable, message.GetBytes());
        }

        public static void OnMapRequested(long requester, string mapFileName) {
            if (!SyncPrefs.mapSyncEnabled.Value) return;
            if (SyncPrefs.IsUserBlocked(requester)) return;

            mapFileName = Path.GetFileName(mapFileName);
            string fullPath = Path.Combine(CustomMapsPatch.customMapsPath, mapFileName);
            if (!File.Exists(fullPath)) {
                EntangleLogger.Warn($"[CustomMapSync] {requester} asked for '{mapFileName}' but we don't have that file");
                return;
            }

            if (SyncPrefs.IsPathBlacklisted(mapFileName)) {
                EntangleLogger.Log($"[CustomMapSync] Not sending '{mapFileName}' - blacklisted");
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length / 1024 > SyncPrefs.maxSyncSizeKB.Value) {
                EntangleLogger.Log($"[CustomMapSync] Not sending '{mapFileName}' - {fileInfo.Length / 1024}KB is over the {SyncPrefs.maxSyncSizeKB.Value}KB sync limit");
                return;
            }

            EntangleLogger.Log($"[CustomMapSync] Sending map '{mapFileName}' to {requester}");
            FileTransferManager.SendFile(requester, fullPath, FileTransferCategory.CustomMap);
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
            EntangleLogger.Log($"[CustomMapSync] Saved map '{transfer.fileName}', loading it");
            CustomMapsPatch.TryLoadMap(transfer.fileName);
        }

        static void OnMapFileFailed(FileTransfer transfer) {
            if (transfer == null) return;
            EntangleLogger.Warn($"[CustomMapSync] Failed to receive map '{transfer.fileName}' from {transfer.peer}");
            requestedFiles.Remove(transfer.fileName);
        }
    }
}
