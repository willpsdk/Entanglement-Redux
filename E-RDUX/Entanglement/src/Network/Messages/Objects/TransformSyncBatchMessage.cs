using System;
using System.Collections.Generic;

using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Objects;

using UnityEngine;

namespace Entanglement.Network
{
    // Packs many TransformSync updates into one packet, so an NPC (one syncable per bone) costs
    // one packet per frame instead of flooding the channel with one per bone per physics step
    [Net.SkipHandleOnLoading]
    public class TransformSyncBatchMessageHandler : NetworkMessageHandler<TransformSyncBatchData>
    {
        public const int entrySize = sizeof(ushort) + SimplifiedTransform.size + sizeof(float) * 6;
        public const int maxEntriesPerMessage = 24; // Keeps a full batch under the ~1200 byte P2P payload

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

                message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(entry.objectId), ref index);
                message.messageData = message.messageData.AddBytes(entry.simplifiedTransform.GetBytes(), ref index);

                message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(entry.velocity.x), ref index);
                message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(entry.velocity.y), ref index);
                message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(entry.velocity.z), ref index);

                message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(entry.angularVelocity.x), ref index);
                message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(entry.angularVelocity.y), ref index);
                message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(entry.angularVelocity.z), ref index);
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

                byte[] transformBytes = new byte[SimplifiedTransform.size];
                Array.Copy(message.messageData, index, transformBytes, 0, SimplifiedTransform.size);
                SimplifiedTransform simpleTransform = SimplifiedTransform.FromBytes(transformBytes);
                index += SimplifiedTransform.size;

                Vector3 velocity;
                velocity.x = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                velocity.y = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                velocity.z = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);

                Vector3 angularVelocity;
                angularVelocity.x = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                angularVelocity.y = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                angularVelocity.z = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);

                if (ObjectSync.TryGetSyncable(objectId, out Syncable syncable) && syncable is TransformSyncable)
                    syncable.Cast<TransformSyncable>().ApplyTransform(simpleTransform, velocity, angularVelocity);
            }

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, msgBytes, sender);
            }
        }
    }

    public class TransformSyncBatchData : NetworkMessageData {
        public List<TransformSyncMessageData> entries = new List<TransformSyncMessageData>();
    }

    // Collects per-body sync states across the physics steps and flushes them once per frame,
    // keyed by object id so only the newest state per body is sent
    public static class TransformSyncBatcher
    {
        static readonly Dictionary<ushort, TransformSyncMessageData> pending = new Dictionary<ushort, TransformSyncMessageData>();

        public static void Enqueue(TransformSyncMessageData data) => pending[data.objectId] = data;

        public static void Flush()
        {
            if (pending.Count == 0)
                return;

            if (Node.activeNode == null || !SteamIntegration.hasLobby) {
                pending.Clear();
                return;
            }

            TransformSyncBatchData batch = new TransformSyncBatchData();

            foreach (TransformSyncMessageData entry in pending.Values) {
                batch.entries.Add(entry);

                if (batch.entries.Count >= TransformSyncBatchMessageHandler.maxEntriesPerMessage) {
                    Send(batch);
                    batch = new TransformSyncBatchData();
                }
            }

            if (batch.entries.Count > 0)
                Send(batch);

            pending.Clear();
        }

        static void Send(TransformSyncBatchData batch)
        {
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSyncBatch, batch);
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
        }

        public static void Clear() => pending.Clear();
    }
}
