using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using Entanglement.Network;

namespace Entanglement.Sync
{
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

        // 64-bit: custom maps run to several GB, which overflows an int byte count.
        public long totalBytes;
        public bool outgoing;

        // Outgoing - streamed off disk, never held in memory.
        public FileStream sourceStream;
        public long sentBytes;

        // Incoming - streamed to a .part file, never held in memory.
        public FileStream writeStream;
        public string tempPath;
        public long receivedBytes;

        public ulong rollingHash = 14695981039346656037UL;

        // Final hash announced by the sender once every chunk is out. 0 until it arrives.
        public ulong expectedHash;
        public bool hashReceived;
        public bool bytesComplete;

        public void CloseStreams() {
            try { if (sourceStream != null) sourceStream.Dispose(); } catch { }
            try { if (writeStream != null) writeStream.Dispose(); } catch { }
            sourceStream = null;
            writeStream = null;
        }

        public void DiscardTemp() {
            CloseStreams();
            try { if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }

        public Action<FileTransfer> onComplete;
        public Action<FileTransfer> onFailed;

        public float lastActivity;

        // Outgoing sends wait a beat after Begin so Reliable announce messages can land first.
        public float sendReadyTime;

        // Last 25% milestone we logged, so progress lines don't spam every chunk
        public int lastLoggedProgress;

    }

    public static class FileTransferManager
    {
        public const int chunkSize = 16000;          // Comfortably under Steam's reliable message ceiling
        public const int chunksPerFrame = 4;          // ~64KB/frame/transfer for ordinary items/models
        public const int bigChunksPerFrame = 16;      // ~256KB/frame once a transfer is clearly a map-sized payload
        public const long bigTransferBytes = 256L * 1024 * 1024;

        static int ChunksPerFrameFor(FileTransfer transfer) {
            return transfer.totalBytes >= bigTransferBytes ? bigChunksPerFrame : chunksPerFrame;
        }
        public const long maxFileBytes = 12L * 1024 * 1024 * 1024; // 12GB
        public const float timeoutSeconds = 180f;                  // Silence, not total duration

        static ushort nextId = 1;
        static readonly Dictionary<ushort, FileTransfer> outgoing = new Dictionary<ushort, FileTransfer>();
        // Keyed by peer+id so two senders allocating from 1 don't clobber each other.
        static readonly Dictionary<string, FileTransfer> incoming = new Dictionary<string, FileTransfer>();
        static readonly Dictionary<string, FileTransfer> pendingConsent = new Dictionary<string, FileTransfer>();

        static string RecvKey(long peer, ushort transferId) {
            return peer.ToString() + ":" + transferId.ToString();
        }

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

        public static ulong HashChunk(ulong hash, byte[] bytes, int count) {
            if (bytes == null)
                return hash;

            for (int i = 0; i < count; i++) {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        // 0 is the "no hash" sentinel, so never let real content hash to it.
        public static ulong SealHash(ulong hash) => hash == 0UL ? 1UL : hash;

        static string IncomingFolder() {
            string folder = Path.Combine(MelonLoader.MelonUtils.UserDataDirectory, "Entanglement", "Incoming");
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static ushort SendFile(long peer, string filePath, FileTransferCategory category, Action<FileTransfer> onComplete = null, Action<FileTransfer> onFailed = null) {
            // Opened, not read. A multi-GB map must never be materialised in memory.
            FileStream source;
            long length;
            try {
                source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, false);
                length = source.Length;
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[FileTransfer] Failed to open {filePath}: {e.Message}");
                onFailed?.Invoke(null);
                return 0;
            }

            if (length <= 0 || length > maxFileBytes) {
                try { source.Dispose(); } catch { }
                EntangleLogger.Warn($"[FileTransfer] {filePath} is {length / 1024 / 1024}MB, outside the {maxFileBytes / 1024 / 1024}MB sync limit. Not sending.");
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
                totalBytes = length,
                outgoing = true,
                sourceStream = source,
                onComplete = onComplete,
                onFailed = onFailed,
                lastActivity = Time.time,
            };

            outgoing[id] = transfer;

            FileTransferBeginData beginData = new FileTransferBeginData {
                transferId = id,
                category = category,
                totalBytes = length,
                fileName = transfer.fileName,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.FileTransferBegin, beginData);
            if (message != null)
                Node.activeNode?.SendMessage(peer, NetworkChannel.Transaction, message.GetBytes());

            EntangleLogger.Log($"[FileTransfer] Sending {transfer.fileName} ({length / 1024 / 1024}MB) to {peer}");
            return id;
        }

        static readonly Dictionary<FileTransferCategory, Action<FileTransfer>> categoryHandlers = new Dictionary<FileTransferCategory, Action<FileTransfer>>();
        static readonly Dictionary<FileTransferCategory, Action<FileTransfer>> categoryFailHandlers = new Dictionary<FileTransferCategory, Action<FileTransfer>>();

        public static void RegisterCategoryHandler(FileTransferCategory category, Action<FileTransfer> onComplete, Action<FileTransfer> onFailed = null) {
            categoryHandlers[category] = onComplete;
            if (onFailed != null) categoryFailHandlers[category] = onFailed;
        }

        internal static void OnBeginReceived(long sender, FileTransferBeginData data) {
            if (!SyncPrefs.allowPeerFiles.Value) {
                EntangleLogger.Warn("[FileTransfer] Rejected " + data.fileName + " from " + sender + ". Files have to come from Thunderstore, not another player.");
                ModThatIsNotMod.Notifications.SendNotification("Blocked a file from another player. Maps and mods come from Thunderstore.", 6f);
                return;
            }

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
                onComplete = onComplete,
                onFailed = onFailed,
                lastActivity = Time.time,
            };

            // One .part file per peer+transfer, streamed to disk as chunks arrive.
            try {
                transfer.tempPath = Path.Combine(IncomingFolder(),
                    sender.ToString() + "_" + data.transferId.ToString() + ".part");
                transfer.writeStream = new FileStream(transfer.tempPath, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, false);
            }
            catch (Exception e) {
                EntangleLogger.Error($"[FileTransfer] Could not open a staging file for {data.fileName}: {e.Message}");
                return;
            }

            bool needsConsent = SyncPrefs.requireDownloadConsent.Value
                || data.category == FileTransferCategory.CustomMap;

            if (needsConsent && !SyncPrefs.IsUserTrusted(sender)) {
                pendingConsent[RecvKey(sender, data.transferId)] = transfer;
                EntangleLogger.Log($"[FileTransfer] Waiting for consent to download {data.fileName} ({data.totalBytes / 1024}KB) from {sender}");
                Entanglement.UI.GameNotify.Show(data.fileName + " is waiting for you in Downloads.", 6f);
                Entanglement.UI.GameRadial.ForceOpenDownloads();
                return;
            }

            incoming[RecvKey(sender, data.transferId)] = transfer;

            EntangleLogger.Log($"[FileTransfer] Downloading {data.fileName} ({data.totalBytes / 1024}KB) from {sender}...");

            if (onComplete == null)
                EntangleLogger.Warn($"[FileTransfer] No registered handler for category {data.category}, {data.fileName} will download but won't be used");
        }

        // Logs at 25% steps so a big download shows progress without one line per chunk
        static void LogProgress(FileTransfer transfer, long currentBytes) {
            if (transfer.totalBytes <= 0)
                return;

            int pct = (int)(100L * currentBytes / transfer.totalBytes);
            int milestone = pct / 25 * 25;
            if (milestone <= transfer.lastLoggedProgress || milestone <= 0 || milestone >= 100)
                return;

            transfer.lastLoggedProgress = milestone;
            string dir = transfer.outgoing ? $"to {transfer.peer}" : $"from {transfer.peer}";
            EntangleLogger.Log("[FileTransfer] " + (transfer.outgoing ? "Sending" : "Downloading") + " "
                + transfer.fileName + " " + dir + ": " + pct + "% ("
                + (currentBytes / 1024 / 1024) + "/" + (transfer.totalBytes / 1024 / 1024) + "MB)");
        }

        internal static void OnChunkReceived(long sender, FileTransferChunkData data) {
            string key = RecvKey(sender, data.transferId);
            FileTransfer transfer;
            if (incoming.TryGetValue(key, out transfer)) {
                // ok
            }
            else if (pendingConsent.TryGetValue(key, out transfer)) {
                // Buffer bytes while the player decides; Accept moves the transfer into incoming
            }
            else return;

            transfer.lastActivity = Time.time;

            if (transfer.receivedBytes + data.chunk.Length > transfer.totalBytes) {
                EntangleLogger.Warn($"[FileTransfer] Chunk overrun for {transfer.fileName}, aborting transfer");
                incoming.Remove(key);
                pendingConsent.Remove(key);
                transfer.DiscardTemp();
                transfer.onFailed?.Invoke(transfer);
                return;
            }

            try { transfer.writeStream.Write(data.chunk, 0, data.chunk.Length); }
            catch (Exception e) {
                EntangleLogger.Error($"[FileTransfer] Write failed for {transfer.fileName}: {e.Message}. Out of disk space?");
                incoming.Remove(key);
                pendingConsent.Remove(key);
                transfer.DiscardTemp();
                transfer.onFailed?.Invoke(transfer);
                return;
            }

            transfer.rollingHash = HashChunk(transfer.rollingHash, data.chunk, data.chunk.Length);
            transfer.receivedBytes += data.chunk.Length;

            LogProgress(transfer, transfer.receivedBytes);

            if (transfer.receivedBytes >= transfer.totalBytes) {
                transfer.bytesComplete = true;
                TryFinishIncoming(key, transfer);
            }
        }

        // The sender's final hash arrives after the last chunk, so completion needs both.
        internal static void OnEndReceived(long sender, ushort transferId, ulong hash) {
            string key = RecvKey(sender, transferId);
            FileTransfer transfer;
            if (!incoming.TryGetValue(key, out transfer) && !pendingConsent.TryGetValue(key, out transfer))
                return;

            transfer.expectedHash = hash;
            transfer.hashReceived = true;
            transfer.lastActivity = Time.time;
            TryFinishIncoming(key, transfer);
        }

        static void TryFinishIncoming(string key, FileTransfer transfer) {
            if (!transfer.bytesComplete || !transfer.hashReceived)
                return;

            if (pendingConsent.ContainsKey(key)) {
                EntangleLogger.Log($"[FileTransfer] {transfer.fileName} fully downloaded, still waiting for consent");
                return;
            }

            incoming.Remove(key);

            // Close the staging file before anything verifies or moves it.
            try { transfer.writeStream?.Flush(); } catch { }
            transfer.CloseStreams();

            ulong actual = SealHash(transfer.rollingHash);
            if (actual != transfer.expectedHash) {
                EntangleLogger.Warn($"[FileTransfer] {transfer.fileName} from {transfer.peer} failed its integrity check "
                    + $"(expected {transfer.expectedHash:X16}, got {actual:X16}). Discarding it.");
                Entanglement.UI.GameNotify.Show("Threw out " + transfer.fileName + ". It did not match what they sent.", 6f);
                transfer.DiscardTemp();
                transfer.onFailed?.Invoke(transfer);
                return;
            }

            EntangleLogger.Log($"[FileTransfer] Finished downloading {transfer.fileName} ({transfer.totalBytes / 1024 / 1024}MB) from {transfer.peer}");
            transfer.onComplete?.Invoke(transfer);

            // Handlers move the staging file into place; drop it if one forgot to.
            transfer.DiscardTemp();
        }

        public static void Tick() {
            if (outgoing.Count > 0) {
                List<ushort> finished = null;

                foreach (var pair in outgoing) {
                    FileTransfer transfer = pair.Value;

                    // Wait for Begin + item announce to land before pumping chunks
                    if (Time.time < transfer.sendReadyTime)
                        continue;

                    int budget = ChunksPerFrameFor(transfer);
                    for (int i = 0; i < budget && transfer.sentBytes < transfer.totalBytes; i++) {
                        long remaining = transfer.totalBytes - transfer.sentBytes;
                        int size = (int)Math.Min((long)chunkSize, remaining);

                        byte[] chunk = new byte[size];
                        int read;
                        try { read = transfer.sourceStream.Read(chunk, 0, size); }
                        catch (Exception e) {
                            EntangleLogger.Error("[FileTransfer] Read failed for " + transfer.fileName + ": " + e.Message);
                            read = 0;
                        }

                        if (read <= 0) {
                            EntangleLogger.Warn("[FileTransfer] " + transfer.fileName + " ended early at " + transfer.sentBytes
                                + "/" + transfer.totalBytes + " bytes; aborting send");
                            transfer.CloseStreams();
                            (finished ?? (finished = new List<ushort>())).Add(pair.Key);
                            transfer.onFailed?.Invoke(transfer);
                            break;
                        }

                        if (read != size) {
                            byte[] trimmed = new byte[read];
                            Buffer.BlockCopy(chunk, 0, trimmed, 0, read);
                            chunk = trimmed;
                        }

                        transfer.rollingHash = HashChunk(transfer.rollingHash, chunk, read);

                        FileTransferChunkData chunkData = new FileTransferChunkData { transferId = transfer.id, chunk = chunk };
                        NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.FileTransferChunk, chunkData);
                        if (message != null)
                            Node.activeNode?.SendMessage(transfer.peer, NetworkChannel.Transaction, message.GetBytes());

                        transfer.sentBytes += read;
                    }

                    LogProgress(transfer, transfer.sentBytes);

                    if (transfer.sentBytes >= transfer.totalBytes) {
                        var endData = new FileTransferEndData {
                            transferId = transfer.id,
                            hash = SealHash(transfer.rollingHash),
                        };
                        NetworkMessage endMessage = NetworkMessage.CreateMessage(BuiltInMessageType.FileTransferEnd, endData);
                        if (endMessage != null)
                            Node.activeNode?.SendMessage(transfer.peer, NetworkChannel.Reliable, endMessage.GetBytes());

                        transfer.CloseStreams();
                        EntangleLogger.Log("[FileTransfer] Finished sending " + transfer.fileName
                            + " (" + (transfer.totalBytes / 1024 / 1024) + "MB) to " + transfer.peer);
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
                List<string> timedOut = null;
                foreach (var pair in incoming) {
                    if (Time.time - pair.Value.lastActivity > timeoutSeconds)
                        (timedOut ?? (timedOut = new List<string>())).Add(pair.Key);
                }
                if (timedOut != null) {
                    foreach (string key in timedOut) {
                        FileTransfer transfer = incoming[key];
                        incoming.Remove(key);
                        int pct = transfer.totalBytes > 0 ? (int)(100L * transfer.receivedBytes / transfer.totalBytes) : 0;
                        EntangleLogger.Warn("[FileTransfer] Timed out downloading " + transfer.fileName + " from " + transfer.peer
                            + " - stalled at " + pct + "% (" + (transfer.receivedBytes / 1024 / 1024) + "/"
                            + (transfer.totalBytes / 1024 / 1024) + "MB) after " + timeoutSeconds.ToString("F0") + "s of silence");
                        transfer.DiscardTemp();
                        transfer.onFailed?.Invoke(transfer);
                    }
                }
            }
        }


        public static bool AcceptPending(long peer, ushort transferId) {
            string key = RecvKey(peer, transferId);
            if (!pendingConsent.TryGetValue(key, out FileTransfer transfer))
                return false;

            pendingConsent.Remove(key);
            incoming[key] = transfer;
            EntangleLogger.Log($"[FileTransfer] Accepted download {transfer.fileName} from {transfer.peer}");

            TryFinishIncoming(key, transfer);
            return true;
        }

        public static bool DenyPending(long peer, ushort transferId) {
            string key = RecvKey(peer, transferId);
            if (!pendingConsent.TryGetValue(key, out FileTransfer transfer))
                return false;

            pendingConsent.Remove(key);
            EntangleLogger.Log("[FileTransfer] Denied download " + transfer.fileName + " from " + transfer.peer);
            transfer.DiscardTemp();
            transfer.onFailed?.Invoke(transfer);
            return true;
        }

        public static void AcceptAllPending() {
            foreach (FileTransfer transfer in new List<FileTransfer>(pendingConsent.Values))
                AcceptPending(transfer.peer, transfer.id);
        }

        public static void DenyAllPending() {
            foreach (FileTransfer transfer in new List<FileTransfer>(pendingConsent.Values))
                DenyPending(transfer.peer, transfer.id);
        }

        public static void Clear() {
            foreach (FileTransfer t in outgoing.Values) t.CloseStreams();
            foreach (FileTransfer t in incoming.Values) t.DiscardTemp();
            foreach (FileTransfer t in pendingConsent.Values) t.DiscardTemp();
            outgoing.Clear();
            incoming.Clear();
        }

        public static void WriteReceivedFile(FileTransfer transfer, string destinationPath) {
            string dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Move, don't buffer: the payload can be several GB and is already on disk.
            transfer.CloseStreams();

            if (string.IsNullOrEmpty(transfer.tempPath) || !File.Exists(transfer.tempPath))
                throw new IOException("staging file for " + transfer.fileName + " is missing");

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);

            try {
                File.Move(transfer.tempPath, destinationPath);
            }
            catch (IOException) {
                // Different volume - fall back to a streamed copy rather than a full read.
                using (FileStream src = new FileStream(transfer.tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, false))
                using (FileStream dst = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, false))
                    src.CopyTo(dst, chunkSize);

                try { File.Delete(transfer.tempPath); } catch { }
            }
        }
    }
}
