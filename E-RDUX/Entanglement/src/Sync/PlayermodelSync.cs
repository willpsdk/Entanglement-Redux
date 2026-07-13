using System;
using System.Collections.Generic;
using System.IO;

using MelonLoader;

using Entanglement.Network;
using Entanglement.Representation;
using Entanglement.Compat.Playermodels;

namespace Entanglement.Sync
{
    // Piggybacks on LoadCustomPlayerMessage, which already broadcasts which model a player is
    // wearing - this adds requesting the file if we don't have it
    public static class PlayermodelSync
    {
        static readonly Dictionary<string, List<long>> waitingRepsByFileName = new Dictionary<string, List<long>>();
        static readonly HashSet<string> requestedFiles = new HashSet<string>();

        static bool initialized;

        public static void Initialize() {
            if (initialized) return;
            initialized = true;

            FileTransferManager.RegisterCategoryHandler(FileTransferCategory.Playermodel, OnModelFileReceived, OnModelFileFailed);
        }

        // ownerUserId is the model's actual owner (from the payload), not necessarily the P2P
        // sender - the host relays this message on the owner's behalf
        public static void RequestModelIfMissing(long ownerUserId, string modelPath, long repOwnerId) {
            if (!SyncPrefs.playermodelSyncEnabled.Value) return;
            if (!SteamIntegration.hasLobby) return;
            if (SyncPrefs.IsUserBlocked(ownerUserId)) return;

            string fullPath = Path.Combine(PlayermodelsPatch.playerModelsPath, modelPath);
            if (File.Exists(fullPath))
                return; // Caller already checked, but guard against a race with a concurrent request

            string fileName = Path.GetFileName(modelPath);

            if (!waitingRepsByFileName.TryGetValue(fileName, out List<long> waiters)) {
                waiters = new List<long>();
                waitingRepsByFileName[fileName] = waiters;
            }
            if (!waiters.Contains(repOwnerId))
                waiters.Add(repOwnerId);

            if (!requestedFiles.Add(fileName))
                return; // Already requested this exact file

            EntangleLogger.Log($"[PlayermodelSync] Missing playermodel '{modelPath}', asking {ownerUserId} for it");

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayermodelSyncRequest, new PlayermodelSyncRequestData { modelPath = modelPath });
            if (message != null)
                Node.activeNode?.SendMessage(ownerUserId, NetworkChannel.Reliable, message.GetBytes());
        }

        // We're the model's owner: someone doesn't have the file, send it if we still have it
        public static void OnModelRequested(long requester, string modelPath) {
            if (!SyncPrefs.playermodelSyncEnabled.Value) return;
            if (SyncPrefs.IsUserBlocked(requester)) return;

            string fullPath = Path.Combine(PlayermodelsPatch.playerModelsPath, modelPath);
            if (!File.Exists(fullPath)) {
                EntangleLogger.Warn($"[PlayermodelSync] {requester} asked for '{modelPath}' but we no longer have that file");
                return;
            }

            if (SyncPrefs.IsPathBlacklisted(modelPath)) {
                EntangleLogger.Log($"[PlayermodelSync] Not sending '{modelPath}' - blacklisted");
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length / 1024 > SyncPrefs.maxSyncSizeKB.Value) {
                EntangleLogger.Log($"[PlayermodelSync] Not sending '{modelPath}' - {fileInfo.Length / 1024}KB is over the {SyncPrefs.maxSyncSizeKB.Value}KB sync limit");
                return;
            }

            EntangleLogger.Log($"[PlayermodelSync] Sending '{modelPath}' to {requester}");
            FileTransferManager.SendFile(requester, fullPath, FileTransferCategory.Playermodel);
        }

        static void OnModelFileReceived(FileTransfer transfer) {
            string destPath = Path.Combine(PlayermodelsPatch.playerModelsPath, transfer.fileName);

            try {
                FileTransferManager.WriteReceivedFile(transfer, destPath);
            }
            catch (Exception e) {
                EntangleLogger.Error($"[PlayermodelSync] Failed writing {destPath}: {e.Message}");
                return;
            }

            requestedFiles.Remove(transfer.fileName);

            if (waitingRepsByFileName.TryGetValue(transfer.fileName, out List<long> waiters)) {
                foreach (long repOwnerId in waiters) {
                    if (PlayerRepresentation.representations.TryGetValue(repOwnerId, out PlayerRepresentation rep))
                        PlayerSkinLoader.ApplyPlayermodel(rep, destPath);
                }
                waitingRepsByFileName.Remove(transfer.fileName);
            }

            EntangleLogger.Log($"[PlayermodelSync] Applied '{transfer.fileName}' after sync");
        }

        static void OnModelFileFailed(FileTransfer transfer) {
            if (transfer == null) return;
            EntangleLogger.Warn($"[PlayermodelSync] Failed to receive '{transfer.fileName}' from {transfer.peer}");
            requestedFiles.Remove(transfer.fileName);
            waitingRepsByFileName.Remove(transfer.fileName);
        }
    }
}
