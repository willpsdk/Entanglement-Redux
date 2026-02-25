using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Objects;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class PuppetSyncMessageHandler : NetworkMessageHandler<PuppetSyncMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PuppetSync;

        public override NetworkMessage CreateMessage(PuppetSyncMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            // Allocate space: objectId (2) + position (12) + rotation (7) + aiState (1) = 22 bytes
            message.messageData = new byte[sizeof(ushort) + 12 + SimplifiedQuaternion.size + 1];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.objectId), ref index);

            // Send position
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.position.x), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.position.y), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.position.z), ref index);

            // Send rotation
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.rotation.c1), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.rotation.c2), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.rotation.c3), ref index);
            message.messageData = message.messageData.AddBytes(new byte[] { data.rotation.loss }, ref index);

            // Send AI State
            message.messageData = message.messageData.AddBytes(new byte[] { data.aiState }, ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            // Expected payload: objectId (2) + position (12) + rotation (7) + aiState (1) = 22 bytes
            const int expectedLength = sizeof(ushort) + 12 + SimplifiedQuaternion.size + 1;
            if (message.messageData == null || message.messageData.Length < expectedLength)
                return;

            int index = 0;
            ushort objectId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            if (ObjectSync.TryGetSyncable(objectId, out Syncable syncable))
            {
                if (syncable is PuppetSyncable)
                {
                    PuppetSyncable puppet = syncable.Cast<PuppetSyncable>();

                    Vector3 pos = new Vector3();
                    pos.x = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                    pos.y = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                    pos.z = BitConverter.ToSingle(message.messageData, index); index += sizeof(float);

                    SimplifiedQuaternion rot = new SimplifiedQuaternion();
                    rot.c1 = BitConverter.ToInt16(message.messageData, index); index += sizeof(short);
                    rot.c2 = BitConverter.ToInt16(message.messageData, index); index += sizeof(short);
                    rot.c3 = BitConverter.ToInt16(message.messageData, index); index += sizeof(short);
                    rot.loss = message.messageData[index]; index += 1;

                    byte aiState = message.messageData[index];

                    puppet.ApplyState(pos, rot.ExpandQuat(), aiState);
                }
            }

            // If we are the host, bounce this message to everyone else
            if (Server.instance != null)
            {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, msgBytes, sender);
            }
        }
    }

    public class PuppetSyncMessageData : NetworkMessageData
    {
        public ushort objectId;
        public Vector3 position;
        public SimplifiedQuaternion rotation;
        public byte aiState;
    }
}