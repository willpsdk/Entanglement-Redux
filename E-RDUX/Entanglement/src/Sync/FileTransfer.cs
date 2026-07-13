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
    }

    // Chunked file transfer over the reliable Transaction channel, no third-party host involved.
    // Begin allocates the receive buffer; completion is implicit once all bytes have arrived.
    public static class FileTransferManager
    {
        public const int chunkSize = 16000;          // Comfortably under Steam's reliable message ceiling
        public const int chunksPerFrame = 4;          // ~64KB/frame/transfer, throttled so it can't hog a frame
        public const int maxFileBytes = 200 * 1024 * 1024; // 200MB hard ceiling, matches item/model realistic sizes
        public const float timeoutSeconds = 60f;

        static ushort nextId = 1;
        static readonly Dictionary<ushort, FileTransfer> outgoing = new Dictionary<ushort, FileTransfer>();
        static readonly Dictionary<ushort, FileTransfer> incoming = new Dictionary<ushort, FileTransfer>();

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

            outgoing[id] = transfer;

            FileTransferBeginData beginData = new FileTransferBeginData {
                transferId = id,
                category = category,
                totalBytes = bytes.Length,
                fileName = transfer.fileName,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.FileTransferBegin, beginData);
            if (message != null)
                Node.activeNode?.SendMessage(peer, NetworkChannel.Transaction, message.GetBytes());

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

            incoming[data.transferId] = transfer;

            if (onComplete == null)
                EntangleLogger.Log($"[FileTransfer] Received Begin for {data.fileName} from {sender} with no registered handler for category {data.category}");
        }

        internal static void OnChunkReceived(long sender, FileTransferChunkData data) {
            if (!incoming.TryGetValue(data.transferId, out FileTransfer transfer) || transfer.peer != sender)
                return;

            transfer.lastActivity = Time.time;

            if (transfer.receivedBytes + data.chunk.Length > transfer.receiveBuffer.Length) {
                EntangleLogger.Warn($"[FileTransfer] Chunk overrun for {transfer.fileName}, aborting transfer");
                incoming.Remove(data.transferId);
                transfer.onFailed?.Invoke(transfer);
                return;
            }

            Buffer.BlockCopy(data.chunk, 0, transfer.receiveBuffer, transfer.receivedBytes, data.chunk.Length);
            transfer.receivedBytes += data.chunk.Length;

            if (transfer.receivedBytes >= transfer.totalBytes) {
                incoming.Remove(data.transferId);
                EntangleLogger.Log($"[FileTransfer] Finished receiving {transfer.fileName} ({transfer.totalBytes / 1024}KB) from {sender}");
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

                    if (transfer.sentBytes >= transfer.totalBytes) {
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
                        EntangleLogger.Warn($"[FileTransfer] Timed out receiving {transfer.fileName} from {transfer.peer}");
                        transfer.onFailed?.Invoke(transfer);
                    }
                }
            }
        }

        public static void Clear() {
            outgoing.Clear();
            incoming.Clear();
        }

        public static void WriteReceivedFile(FileTransfer transfer, string destinationPath) {
            string dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(destinationPath, transfer.receiveBuffer);
        }
    }
}
