using System;

using Entanglement.Extensions;
using Entanglement.Objects;

namespace Entanglement.Network
{
    /// <summary>
    /// Moves an NPC to another player's machine. See PooleeSyncable.ConsiderHandoff.
    /// </summary>
    [Net.HandleOnLoaded]
    public class NpcOwnerMessageHandler : NetworkMessageHandler<NpcOwnerMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.NpcOwner;

        const int Size = sizeof(ushort) + sizeof(long) + sizeof(byte) + sizeof(ushort) + sizeof(byte);

        public override NetworkMessage CreateMessage(NpcOwnerMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[Size];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.spawnId), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.owner), ref index);
            message.messageData[index++] = (byte)(data.fighting ? 1 : 0);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.seq), ref index);
            message.messageData[index++] = (byte)(data.proposal ? 1 : 0);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < Size)
                return;

            int index = 0;
            ushort spawnId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);
            long owner = BitConverter.ToInt64(message.messageData, index);
            index += sizeof(long);
            bool fighting = message.messageData[index++] != 0;
            ushort seq = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);
            bool proposal = message.messageData[index++] != 0;

            PooleeSyncable.ApplyOwnerMessage(spawnId, owner, sender, fighting, seq, proposal);
        }
    }

    public class NpcOwnerMessageData : NetworkMessageData {
        public ushort spawnId;
        public long owner;
        public bool fighting; // the NPC is fighting its new owner
        public ushort seq;
        public bool proposal; // a player asking the host, rather than the host deciding
    }
}
