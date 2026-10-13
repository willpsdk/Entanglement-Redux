using System;

using UnityEngine;

using Entanglement.Extensions;
using Entanglement.Objects;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class NpcHitMessageHandler : NetworkMessageHandler<NpcHitMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.NpcHit;

        const int Size = sizeof(ushort) + sizeof(long) + sizeof(byte) + sizeof(short) + sizeof(float) + sizeof(byte) + 3 * 3 * sizeof(float);

        public override NetworkMessage CreateMessage(NpcHitMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[Size];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.spawnId), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.attacker), ref index);
            message.messageData[index++] = (byte)data.kind;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.muscle), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.damage), ref index);
            message.messageData[index++] = data.attackType;
            index = Put(message.messageData, index, data.origin);
            index = Put(message.messageData, index, data.normal);
            Put(message.messageData, index, data.force);
            return message;
        }

        static int Put(byte[] bytes, int index, Vector3 v) {
            bytes = bytes.AddBytes(BitConverter.GetBytes(v.x), ref index);
            bytes = bytes.AddBytes(BitConverter.GetBytes(v.y), ref index);
            bytes = bytes.AddBytes(BitConverter.GetBytes(v.z), ref index);
            return index;
        }

        static Vector3 Get(byte[] bytes, ref int index) {
            float x = BitConverter.ToSingle(bytes, index);
            float y = BitConverter.ToSingle(bytes, index + sizeof(float));
            float z = BitConverter.ToSingle(bytes, index + 2 * sizeof(float));
            index += 3 * sizeof(float);
            return new Vector3(x, y, z);
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < Size)
                return;

            byte[] bytes = message.messageData;
            int index = 0;
            NpcHitMessageData data = new NpcHitMessageData();
            data.spawnId = BitConverter.ToUInt16(bytes, index);
            index += sizeof(ushort);
            data.attacker = BitConverter.ToInt64(bytes, index);
            index += sizeof(long);
            data.kind = (NpcHitKind)bytes[index++];
            data.muscle = BitConverter.ToInt16(bytes, index);
            index += sizeof(short);
            data.damage = BitConverter.ToSingle(bytes, index);
            index += sizeof(float);
            data.attackType = bytes[index++];
            data.origin = Get(bytes, ref index);
            data.normal = Get(bytes, ref index);
            data.force = Get(bytes, ref index);

            // A client can only speak for itself.
            if (Server.instance != null && data.attacker != sender)
                return;

            PooleeSyncable poolee;
            if (PooleeSyncable._PooleeLookup.TryGetValue(data.spawnId, out poolee) && ObjectSync.Live(poolee))
                poolee.ApplyHit(data);

            // Only whoever runs it applies it, and that may be another client.
            if (Server.instance != null)
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, message.GetBytes(), sender);
        }
    }

    public enum NpcHitKind : byte {
        Attack, // the game's own attack (a bullet, fist, blade, blunt prop, explosion): replayed as one
        Impact, // a hard contact from something the attacker holds or threw: damage is its impulse
    }

    public class NpcHitMessageData : NetworkMessageData {
        public ushort spawnId;
        public long attacker;
        public NpcHitKind kind;
        public short muscle;      // index in the NPC's PuppetMaster.muscles, -1 if not found
        public float damage;
        public byte attackType;   // StressLevelZero.Combat.AttackType
        public Vector3 origin;    // where it landed
        public Vector3 normal;    // the surface there
        public Vector3 force;     // the push, as an impulse
    }
}
