using System;
using System.Collections.Generic;

using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Objects;

using UnityEngine;

namespace Entanglement.Network
{
    // Packs many transform updates into one packet. Continuous motion rides the unreliable
    // channel; the single rest pose an object sends when it falls asleep rides reliable, so
    // every machine settles it identically and then goes silent
    [Net.SkipHandleOnLoading]
    public class TransformSyncBatchMessageHandler : NetworkMessageHandler<TransformSyncBatchData>
    {
        public const int entrySize = sizeof(ushort) + SimplifiedTransform.size + sizeof(float) * 6 + sizeof(byte);
        public const int maxEntriesPerMessage = 24;

        public override byte? MessageIndex => BuiltInMessageType.TransformSyncBatch;

        public override NetworkMessage CreateMessage(TransformSyncBatchData data)
        {
            NetworkMessage message = new NetworkMessage();

            int count = data.entries.Count;
            message.messageData = new byte[sizeof(byte) + entrySize * count];

            int index = 0;
            message.messageData[index++] = (byte)count;

            for (int i = 0; i < count; i++) {
                TransformSyncMessageData entry = data.entries[i];

                message.messageData.WriteUShort(ref index, entry.objectId);
                entry.simplifiedTransform.WriteTo(message.messageData, ref index);

                message.messageData.WriteFloat(ref index, entry.velocity.x);
                message.messageData.WriteFloat(ref index, entry.velocity.y);
                message.messageData.WriteFloat(ref index, entry.velocity.z);

                message.messageData.WriteFloat(ref index, entry.angularVelocity.x);
                message.messageData.WriteFloat(ref index, entry.angularVelocity.y);
                message.messageData.WriteFloat(ref index, entry.angularVelocity.z);

                message.messageData[index++] = entry.resting ? (byte)1 : (byte)0;
            }

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            byte count = message.messageData[index++];

            for (int i = 0; i < count; i++) {
                if (message.messageData.Length < index + entrySize)
                    break;

                ushort objectId = BitConverter.ToUInt16(message.messageData, index);
                index += sizeof(ushort);

                SimplifiedTransform simpleTransform = SimplifiedTransform.FromBytes(message.messageData, index);
                index += SimplifiedTransform.size;

                Vector3 velocity;
                velocity.x = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                velocity.y = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                velocity.z = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);

                Vector3 angularVelocity;
                angularVelocity.x = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                angularVelocity.y = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                angularVelocity.z = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);

                bool resting = message.messageData[index++] != 0;

                if (ObjectSync.TryGetSyncable(objectId, out Syncable syncable) && syncable is TransformSyncable) {
                    TransformSyncable sync = syncable.Cast<TransformSyncable>();
                    if (resting)
                        sync.ApplyRestState(simpleTransform);
                    else
                        sync.ApplyTransform(simpleTransform, velocity, angularVelocity);
                }
            }

            if (Server.instance != null) {
                // Rest batches arrive reliable and must be relayed reliable; a whole batch is
                // one kind, so the first entry's flag decides the relay channel
                NetworkChannel channel = count > 0 && message.messageData[1 + entrySize - 1] != 0
                    ? NetworkChannel.Reliable
                    : NetworkChannel.Unreliable;

                Server.instance.BroadcastMessageExcept(channel, message.GetBytes(), sender);
            }
        }
    }

    public class TransformSyncBatchData : NetworkMessageData {
        public List<TransformSyncMessageData> entries = new List<TransformSyncMessageData>();
    }

    // Coalesces per-body states once a frame. Moving bodies go out unreliable; a body's single
    // rest pose goes out reliable and supersedes any pending motion for that object
    public static class TransformSyncBatcher
    {
        static readonly Dictionary<ushort, TransformSyncMessageData> pending = new Dictionary<ushort, TransformSyncMessageData>();
        static readonly Dictionary<ushort, TransformSyncMessageData> pendingRest = new Dictionary<ushort, TransformSyncMessageData>();
        static readonly TransformSyncBatchData reusedBatch = new TransformSyncBatchData();

        public static void Enqueue(TransformSyncMessageData data) {
            if (pendingRest.ContainsKey(data.objectId))
                return; // A rest pose already queued this frame is definitive

            pending[data.objectId] = data;
        }

        public static void EnqueueRest(TransformSyncMessageData data) {
            pending.Remove(data.objectId);
            pendingRest[data.objectId] = data;
        }

        public static void Flush()
        {
            if (pending.Count == 0 && pendingRest.Count == 0)
                return;

            if (Node.activeNode == null || !SteamIntegration.hasLobby) {
                pending.Clear();
                pendingRest.Clear();
                return;
            }

            FlushSet(pending, NetworkChannel.Unreliable);
            FlushSet(pendingRest, NetworkChannel.Reliable);
        }

        static void FlushSet(Dictionary<ushort, TransformSyncMessageData> set, NetworkChannel channel) {
            if (set.Count == 0)
                return;

            reusedBatch.entries.Clear();

            foreach (TransformSyncMessageData entry in set.Values) {
                reusedBatch.entries.Add(entry);

                if (reusedBatch.entries.Count >= TransformSyncBatchMessageHandler.maxEntriesPerMessage) {
                    Send(reusedBatch, channel);
                    reusedBatch.entries.Clear();
                }
            }

            if (reusedBatch.entries.Count > 0)
                Send(reusedBatch, channel);

            set.Clear();
        }

        static void Send(TransformSyncBatchData batch, NetworkChannel channel)
        {
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSyncBatch, batch);
            if (message != null)
                Node.activeNode.BroadcastMessage(channel, message.GetBytes());
        }

        public static void Clear() {
            pending.Clear();
            pendingRest.Clear();
        }
    }
}
