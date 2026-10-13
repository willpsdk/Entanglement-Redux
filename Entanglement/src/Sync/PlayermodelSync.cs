using System;
using System.Collections.Generic;
using System.IO;

using MelonLoader;

using Entanglement.Network;
using Entanglement.Representation;
using Entanglement.Compat.Playermodels;
using Entanglement.UI;

namespace Entanglement.Sync
{
    public static class PlayermodelSync
    {
        static readonly Dictionary<string, List<long>> waitingRepsByFileName = new Dictionary<string, List<long>>();

        static readonly Dictionary<string, float> requestedFiles = new Dictionary<string, float>();
        const float RequestTimeout = 45f;

        static readonly Dictionary<long, string> wanted = new Dictionary<long, string>();

        public static void Want(long userId, string modelPath) {
            if (string.IsNullOrWhiteSpace(modelPath))
                wanted.Remove(userId);
            else
                wanted[userId] = Path.GetFileName(modelPath);
        }

        public static void Forget(long userId) {
            wanted.Remove(userId);
        }

        /// <summary>A rep was just made: put on the model its player said they wear, if we have it.</summary>
        public static void ApplyWanted(long userId) {
            string fileName;
            if (!wanted.TryGetValue(userId, out fileName) || string.IsNullOrEmpty(fileName))
                return;
            string fullPath = Path.Combine(PlayermodelsPatch.playerModelsPath, fileName);
            if (!File.Exists(fullPath))
                return;
            PlayerRepresentation rep;
            if (!PlayerRepresentation.representations.TryGetValue(userId, out rep) || rep == null)
                return;
            if (rep.isCustomSkinned && rep.currentSkinPath == fullPath)
                return;
            try { PlayerSkinLoader.ApplyPlayermodel(rep, fullPath); }
            catch (Exception e) { EntangleLogger.Warn("[PlayermodelSync] Could not put " + fileName + " on " + userId + ": " + e.Message); }
        }

        static void ApplyToEveryoneWearing(string fileName) {
            foreach (KeyValuePair<long, string> pair in new List<KeyValuePair<long, string>>(wanted)) {
                if (string.Equals(pair.Value, fileName, StringComparison.OrdinalIgnoreCase))
                    ApplyWanted(pair.Key);
            }
        }

        static bool initialized;

        public static void Initialize() {
            if (initialized) return;
            initialized = true;

            FileTransferManager.RegisterCategoryHandler(FileTransferCategory.Playermodel, OnModelFileReceived, OnModelFileFailed);
            Thunderstore.RegisterInstaller(FileTransferCategory.Playermodel, OnThunderstoreReady);
        }

        public static void ClearRequested(string modelPath) {
            if (string.IsNullOrEmpty(modelPath))
                return;
            requestedFiles.Remove(Path.GetFileName(modelPath));
        }

        /// <summary>The owner answered that they can't share it (see CustomItemSync.OnItemUnavailable).</summary>
        public static void OnUnavailable(long sender, string modelPath, string why) {
            string fileName = Path.GetFileName(modelPath ?? "");
            requestedFiles.Remove(fileName);
            waitingRepsByFileName.Remove(fileName);
            EntangleLogger.Warn("[PlayermodelSync] " + sender + " can't share '" + fileName + "': " + why);
            string who;
            try { who = SteamIntegration.GetUserName(sender); }
            catch { who = null; }
            if (string.IsNullOrEmpty(who))
                who = "That player";
            GameNotify.Show(who + " can't share their player model " + Path.GetFileNameWithoutExtension(fileName) + ": " + why + ".", 8f);
        }

        public static void RequestModelIfMissing(long ownerUserId, string modelPath, long repOwnerId) {
            if (!SyncPrefs.playermodelSyncEnabled.Value) return;
            if (!SteamIntegration.hasLobby) return;
            if (SyncPrefs.IsUserBlocked(ownerUserId)) return;

            string fullPath = Path.Combine(PlayermodelsPatch.playerModelsPath, modelPath);
            if (File.Exists(fullPath))
                return;

            string fileName = Path.GetFileName(modelPath);

            if (!waitingRepsByFileName.TryGetValue(fileName, out List<long> waiters)) {
                waiters = new List<long>();
                waitingRepsByFileName[fileName] = waiters;
            }
            if (!waiters.Contains(repOwnerId))
                waiters.Add(repOwnerId);

            // Already asked, and still waiting for the answer.
            float askedAt;
            if (requestedFiles.TryGetValue(fileName, out askedAt) && UnityEngine.Time.realtimeSinceStartup - askedAt < RequestTimeout)
                return;
            requestedFiles[fileName] = UnityEngine.Time.realtimeSinceStartup;

            EntangleLogger.Log($"[PlayermodelSync] Missing playermodel '{modelPath}', asking {ownerUserId} for it");

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayermodelSyncRequest, new PlayermodelSyncRequestData { modelPath = modelPath });
            if (message != null)
                Node.activeNode?.SendMessage(ownerUserId, NetworkChannel.Reliable, message.GetBytes());
        }

        // Someone doesn't have our model. Host or client, the only answer is a Thunderstore offer.
        public static void OnModelRequested(long requester, string modelPath) {
            if (!SyncPrefs.playermodelSyncEnabled.Value) return;
            if (SyncPrefs.IsUserBlocked(requester)) return;

            string fullPath = Path.Combine(PlayermodelsPatch.playerModelsPath, modelPath);
            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists) {
                EntangleLogger.Warn("[PlayermodelSync] " + requester + " asked for '" + modelPath + "' but we no longer have that file");
                CustomItemSync.SendModelUnavailable(requester, modelPath, "they no longer have the file");
                return;
            }

            if (SyncPrefs.IsPathBlacklisted(modelPath)) {
                EntangleLogger.Log("[PlayermodelSync] Not sending '" + modelPath + "' - blacklisted");
                CustomItemSync.SendModelUnavailable(requester, modelPath, "they have blocked sharing it");
                return;
            }

            // Every way out of here used to leave the asker waiting with nothing to say why.
            Action notOffered = () => {
                EntangleLogger.Warn("[PlayermodelSync] " + modelPath + " was not sent. It has to be a Thunderstore package.");
                CustomItemSync.SendModelUnavailable(requester, modelPath, "it isn't a Thunderstore package on their side");
            };

            // Always Thunderstore - models are often over the P2P size cap (maps already offer first).
            if (Thunderstore.TryOfferFile(requester, fullPath, FileTransferCategory.Playermodel, "", notOffered))
                return;
            notOffered();
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
            PlayermodelsPatch.AddMissingToMenu();
            ApplyToEveryoneWearing(transfer.fileName);

            if (waitingRepsByFileName.TryGetValue(transfer.fileName, out List<long> waiters)) {
                foreach (long repOwnerId in waiters) {
                    if (PlayerRepresentation.representations.TryGetValue(repOwnerId, out PlayerRepresentation rep))
                        PlayerSkinLoader.ApplyPlayermodel(rep, destPath);
                }
                waitingRepsByFileName.Remove(transfer.fileName);
            }

            EntangleLogger.Log($"[PlayermodelSync] Applied '{transfer.fileName}' after sync");
        }

        static void OnThunderstoreReady(string fileName, string note) {
            requestedFiles.Remove(fileName);
            PlayermodelsPatch.AddMissingToMenu();
            if (note == "wear") {
                try { PlayermodelsPatch.Wear(fileName); }
                catch (Exception e) {
                    EntangleLogger.Warn("[PlayermodelSync] Saved " + fileName + " but it did not apply: " + e.Message);
                    GameNotify.Show("Got " + fileName + ". Wear it from Player Models.", 6f);
                }
            }
            else {
                string destPath = Path.Combine(PlayermodelsPatch.playerModelsPath, fileName);
                // Everyone wearing it now, including players whose rep didn't exist when they said so.
                ApplyToEveryoneWearing(fileName);
                if (waitingRepsByFileName.TryGetValue(fileName, out List<long> waiters)) {
                    foreach (long repOwnerId in waiters) {
                        if (PlayerRepresentation.representations.TryGetValue(repOwnerId, out PlayerRepresentation rep))
                            PlayerSkinLoader.ApplyPlayermodel(rep, destPath);
                    }
                    waitingRepsByFileName.Remove(fileName);
                }
            }
            EntangleLogger.Log("[PlayermodelSync] Applied Thunderstore model " + fileName);
        }

        static void OnModelFileFailed(FileTransfer transfer) {
            if (transfer == null) return;
            EntangleLogger.Warn($"[PlayermodelSync] Failed to receive '{transfer.fileName}' from {transfer.peer}");
            requestedFiles.Remove(transfer.fileName);
            waitingRepsByFileName.Remove(transfer.fileName);
        }
    }
}
