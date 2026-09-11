using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using Entanglement.Network;

namespace Entanglement.Sync
{
    // What kind of file this is, so the receiver knows where to save it and how to load it.
    // Custom1-4 are for third-party mods - see docs/Modding.md. Only one handler can be
    // registered per category, so a shared bucket means only one mod using it at a time; a mod
    // with real sync needs should filter by filename prefix inside its own handler.
    public enum FileTransferCategory : byte {
        CustomItem = 0,
        Playermodel = 1,
        Custom1 = 2,
        Custom2 = 3,
        Custom3 = 4,
        Custom4 = 5,
        CustomMap = 6,
    }

    // One in-flight transfer, either sending our file out in chunks or reassembling one coming in
    public class FileTransfer {
        public ushort id;
        public long peer;
        public FileTransferCategory category;
        public string fileName;
        public int totalBytes;
        public bool outgoing;

        // Outgoing
        public byte[] sourceBytes;
        public int sentBytes;

        // Incoming
        public byte[] receiveBuffer;
        public int receivedBytes;

        public Action<FileTransfer> onComplete;
        public Action<FileTransfer> onFailed;

        public float lastActivity;

        // Outgoing sends wait a beat after Begin so Reliable announce messages can land first
        public float sendReadyTime;

        // Last 25% milestone we logged, so progress lines don't spam every chunk
        public int lastLoggedProgress;
    }

    // Chunked file transfer over the reliable Transaction channel, no third-party host involved.
    // Begin allocates the receive buffer; completion is implicit once all bytes have arrived.
    public static class FileTransferManager
    {
        public const int chunkSize = 16000;          // Comfortably under Steam's reliable message ceiling
        public const int chunksPerFrame = 4;          // ~64KB/frame/transfer, throttled so it can't hog a frame
        public const int maxFileBytes = 200 * 1024 * 1024; // 200MB hard ceiling, matches item/model realistic sizes
        public const float timeoutSeconds = 180f;

        static ushort nextId = 1;
        static readonly Dictionary<ushort, FileTransfer> outgoing = new Dictionary<ushort, FileTransfer>();
        static readonly Dictionary<ushort, FileTransfer> incoming = new Dictionary<ushort, FileTransfer>();

        // Begins waiting for the player to Accept/Deny before chunks are kept
        static readonly Dictionary<ushort, FileTransfer> pendingConsent = new Dictionary<ushort, FileTransfer>();

        public static bool HasPendingConsent => pendingConsent.Count > 0;
        public static int PendingConsentCount => pendingConsent.Count;

        public static FileTransfer LargestPendingConsent() {
            FileTransfer biggest = null;
            foreach (FileTransfer t in pendingConsent.Values)
                if (biggest == null || t.totalBytes > biggest.totalBytes)
                    biggest = t;
            return biggest;
        }

        public static List<FileTransfer> GetPendingConsentTransfers() {
            return new List<FileTransfer>(pendingConsent.Values);
        }

        // Read-only status for the download UI / join gate
        public static bool HasActiveDownloads => incoming.Count > 0;
        public static int ActiveDownloadCount => incoming.Count;

        // Largest active download, so the readout tracks the big model instead of flickering onto tiny files
        public static FileTransfer LargestActiveDownload() {
            FileTransfer biggest = null;
            foreach (FileTransfer t in incoming.Values)
                if (biggest == null || t.totalBytes > biggest.totalBytes)
                    biggest = t;
            return biggest;
        }

        // Combined 0..1 progress across every active download
        public static float TotalDownloadProgress() {
            long total = 0, received = 0;
            foreach (FileTransfer t in incoming.Values) {
                total += t.totalBytes;
                received += t.receivedBytes;
            }
            return total > 0 ? Mathf.Clamp01((float)received / total) : 0f;
        }

        public static ushort SendFile(long peer, string filePath, FileTransferCategory category, Action<FileTransfer> onComplete = null, Action<FileTransfer> onFailed = null) {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(filePath); }
            catch (Exception e) {
                EntangleLogger.Warn($"[FileTransfer] Failed to read {filePath}: {e.Message}");
                onFailed?.Invoke(null);
                return 0;
            }

            if (bytes.Length > maxFileBytes) {
                EntangleLogger.Warn($"[FileTransfer] {filePath} is {bytes.Length / 1024 / 1024}MB, over the {maxFileBytes / 1024 / 1024}MB sync limit. Not sending.");
                onFailed?.Invoke(null);
                return 0;
            }

            ushort id = nextId++;
            if (nextId == 0) nextId = 1;

            FileTransfer transfer = new FileTransfer {
                id = id,
                peer = peer,
                category = category,
                fileName = Path.GetFileName(filePath),
                totalBytes = bytes.Length,
                outgoing = true,
                sourceBytes = bytes,
                onComplete = onComplete,
                onFailed = onFailed,
                lastActivity = Time.time,
            };

            // Give Reliable ItemSync announcements a moment to arrive before Transaction chunks
            transfer.sendReadyTime = Time.time + 0.35f;
            outgoing[id] = transfer;

            FileTransferBeginData beginData = new FileTransferBeginData {
                transferId = id,
                category = category,
                totalBytes = bytes.Length,
                fileName = transfer.fileName,
            };

            // Begin goes Reliable so it is less likely to race ahead of category announcements
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.FileTransferBegin, beginData);
            if (message != null)
                Node.activeNode?.SendMessage(peer, NetworkChannel.Reliable, message.GetBytes());

            EntangleLogger.Log($"[FileTransfer] Sending {transfer.fileName} ({bytes.Length / 1024}KB) to {peer}");
            return id;
        }

        // One handler per category, registered once - not per transfer, which avoids any race
        // between "expect this file" and the Begin message arriving
        static readonly Dictionary<FileTransferCategory, Action<FileTransfer>> categoryHandlers = new Dictionary<FileTransferCategory, Action<FileTransfer>>();
        static readonly Dictionary<FileTransferCategory, Action<FileTransfer>> categoryFailHandlers = new Dictionary<FileTransferCategory, Action<FileTransfer>>();

        public static void RegisterCategoryHandler(FileTransferCategory category, Action<FileTransfer> onComplete, Action<FileTransfer> onFailed = null) {
            categoryHandlers[category] = onComplete;
            if (onFailed != null) categoryFailHandlers[category] = onFailed;
        }

        internal static void OnBeginReceived(long sender, FileTransferBeginData data) {
            if (data.totalBytes <= 0 || data.totalBytes > maxFileBytes) {
                EntangleLogger.Warn($"[FileTransfer] Rejecting transfer {data.fileName} from {sender}, size {data.totalBytes} is invalid or over the limit");
                return;
            }

            categoryHandlers.TryGetValue(data.category, out Action<FileTransfer> onComplete);
            categoryFailHandlers.TryGetValue(data.category, out Action<FileTransfer> onFailed);

            FileTransfer transfer = new FileTransfer {
                id = data.transferId,
                peer = sender,
                category = data.category,
                fileName = data.fileName,
                totalBytes = data.totalBytes,
                outgoing = false,
                receiveBuffer = new byte[data.totalBytes],
                onComplete = onComplete,
                onFailed = onFailed,
                lastActivity = Time.time,
            };

            bool needsConsent = SyncPrefs.requireDownloadConsent.Value
                || data.category == FileTransferCategory.CustomMap;

            if (needsConsent && !SyncPrefs.IsUserTrusted(sender)) {
                pendingConsent[data.transferId] = transfer;
                EntangleLogger.Log($"[FileTransfer] Waiting for consent to download {data.fileName} ({data.totalBytes / 1024}KB) from {sender}");
                ModThatIsNotMod.Notifications.SendNotification($"Download needs permission:\n{data.fileName}\nCircle menu → Downloads → Accept or Decline", 6f);
                return;
            }

            incoming[data.transferId] = transfer;

            EntangleLogger.Log($"[FileTransfer] Downloading {data.fileName} ({data.totalBytes / 1024}KB) from {sender}...");

            if (onComplete == null)
                EntangleLogger.Warn($"[FileTransfer] No registered handler for category {data.category}, {data.fileName} will download but won't be used");
        }

        // Logs at 25% steps so a big download shows progress without one line per chunk
        static void LogProgress(FileTransfer transfer, int currentBytes) {
            if (transfer.totalBytes <= 0)
                return;

            int pct = (int)(100L * currentBytes / transfer.totalBytes);
            int milestone = pct / 25 * 25;
            if (milestone <= transfer.lastLoggedProgress || milestone <= 0 || milestone >= 100)
                return;

            transfer.lastLoggedProgress = milestone;
            string dir = transfer.outgoing ? $"to {transfer.peer}" : $"from {transfer.peer}";
            EntangleLogger.Log($"[FileTransfer] {(transfer.outgoing ? "Sending" : "Downloading")} {transfer.fileName} {dir}: {pct}% ({currentBytes / 1024}/{transfer.totalBytes / 1024}KB)");
        }

        internal static void OnChunkReceived(long sender, FileTransferChunkData data) {
            FileTransfer transfer;
            if (incoming.TryGetValue(data.transferId, out transfer)) {
                if (transfer.peer != sender) return;
            }
            else if (pendingConsent.TryGetValue(data.transferId, out transfer)) {
                if (transfer.peer != sender) return;
                // Buffer bytes while the player decides; Accept moves the transfer into incoming
            }
            else return;

            transfer.lastActivity = Time.time;

            if (transfer.receivedBytes + data.chunk.Length > transfer.receiveBuffer.Length) {
                EntangleLogger.Warn($"[FileTransfer] Chunk overrun for {transfer.fileName}, aborting transfer");
                incoming.Remove(data.transferId);
                pendingConsent.Remove(data.transferId);
                transfer.onFailed?.Invoke(transfer);
                return;
            }

            Buffer.BlockCopy(data.chunk, 0, transfer.receiveBuffer, transfer.receivedBytes, data.chunk.Length);
            transfer.receivedBytes += data.chunk.Length;

            LogProgress(transfer, transfer.receivedBytes);

            if (transfer.receivedBytes >= transfer.totalBytes) {
                if (pendingConsent.ContainsKey(data.transferId)) {
                    EntangleLogger.Log($"[FileTransfer] {transfer.fileName} fully buffered, still waiting for consent");
                    return;
                }
                incoming.Remove(data.transferId);
                EntangleLogger.Log($"[FileTransfer] Finished downloading {transfer.fileName} ({transfer.totalBytes / 1024}KB) from {sender}");
                transfer.onComplete?.Invoke(transfer);
            }
        }

        // Pumps every active outgoing transfer a bounded number of chunks per frame. Called from
        // Mod.OnUpdate, same spot the old DataTransaction.Process() ran from.
        public static void Tick() {
            if (outgoing.Count > 0) {
                List<ushort> finished = null;

                foreach (var pair in outgoing) {
                    FileTransfer transfer = pair.Value;

                    // Wait for Begin + item announce to land before pumping chunks
                    if (Time.time < transfer.sendReadyTime)
                        continue;

                    for (int i = 0; i < chunksPerFrame && transfer.sentBytes < transfer.totalBytes; i++) {
                        int remaining = transfer.totalBytes - transfer.sentBytes;
                        int size = Math.Min(chunkSize, remaining);

                        byte[] chunk = new byte[size];
                        Buffer.BlockCopy(transfer.sourceBytes, transfer.sentBytes, chunk, 0, size);

                        FileTransferChunkData chunkData = new FileTransferChunkData { transferId = transfer.id, chunk = chunk };
                        NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.FileTransferChunk, chunkData);
                        if (message != null)
                            Node.activeNode?.SendMessage(transfer.peer, NetworkChannel.Transaction, message.GetBytes());

                        transfer.sentBytes += size;
                    }

                    LogProgress(transfer, transfer.sentBytes);

                    if (transfer.sentBytes >= transfer.totalBytes) {
                        EntangleLogger.Log($"[FileTransfer] Finished sending {transfer.fileName} ({transfer.totalBytes / 1024}KB) to {transfer.peer}");
                        (finished ?? (finished = new List<ushort>())).Add(pair.Key);
                        transfer.onComplete?.Invoke(transfer);
                    }
                }

                if (finished != null)
                    foreach (ushort id in finished)
                        outgoing.Remove(id);
            }

            // Sweep stalled incoming transfers (a disconnect mid-transfer would otherwise leak forever)
            if (incoming.Count > 0) {
                List<ushort> timedOut = null;
                foreach (var pair in incoming) {
                    if (Time.time - pair.Value.lastActivity > timeoutSeconds)
                        (timedOut ?? (timedOut = new List<ushort>())).Add(pair.Key);
                }
                if (timedOut != null) {
                    foreach (ushort id in timedOut) {
                        FileTransfer transfer = incoming[id];
                        incoming.Remove(id);
                        int pct = transfer.totalBytes > 0 ? (int)(100L * transfer.receivedBytes / transfer.totalBytes) : 0;
                        EntangleLogger.Warn($"[FileTransfer] Timed out downloading {transfer.fileName} from {transfer.peer} - stalled at {pct}% ({transfer.receivedBytes / 1024}/{transfer.totalBytes / 1024}KB) after {timeoutSeconds:F0}s of silence");
                        transfer.onFailed?.Invoke(transfer);
                    }
                }
            }
        }


        public static bool AcceptPending(ushort transferId) {
            if (!pendingConsent.TryGetValue(transferId, out FileTransfer transfer))
                return false;

            pendingConsent.Remove(transferId);
            incoming[transferId] = transfer;
            EntangleLogger.Log($"[FileTransfer] Accepted download {transfer.fileName} from {transfer.peer}");

            if (transfer.receivedBytes >= transfer.totalBytes && transfer.totalBytes > 0) {
                incoming.Remove(transferId);
                EntangleLogger.Log($"[FileTransfer] Finished downloading {transfer.fileName} ({transfer.totalBytes / 1024}KB) from {transfer.peer}");
                transfer.onComplete?.Invoke(transfer);
            }
            return true;
        }

        public static bool DenyPending(ushort transferId) {
            if (!pendingConsent.TryGetValue(transferId, out FileTransfer transfer))
                return false;

            pendingConsent.Remove(transferId);
            EntangleLogger.Log($"[FileTransfer] Denied download {transfer.fileName} from {transfer.peer}");
            transfer.onFailed?.Invoke(transfer);
            return true;
        }

        public static void AcceptAllPending() {
            foreach (ushort id in new List<ushort>(pendingConsent.Keys))
                AcceptPending(id);
        }

        public static void DenyAllPending() {
            foreach (ushort id in new List<ushort>(pendingConsent.Keys))
                DenyPending(id);
        }

        public static void Clear() {
            outgoing.Clear();
            incoming.Clear();
            pendingConsent.Clear();
        }

        public static void WriteReceivedFile(FileTransfer transfer, string destinationPath) {
            string dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(destinationPath, transfer.receiveBuffer);
        }
    }
}
