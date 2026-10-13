using System;

using Entanglement.Extensions;
using Entanglement.Objects;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class NpcHealthMessageHandler : NetworkMessageHandler<NpcHealthMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.NpcHealth;

        public override NetworkMessage CreateMessage(NpcHealthMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[sizeof(ushort) + sizeof(float)];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.spawnId), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.value), ref index);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < sizeof(ushort) + sizeof(float))
                return;

            ushort spawnId = BitConverter.ToUInt16(message.messageData, 0);
            float value = BitConverter.ToSingle(message.messageData, sizeof(ushort));

            bool held = NpcPending.HoldHealth(spawnId, value);

            PooleeSyncable poolee;
            if (!held && PooleeSyncable._PooleeLookup.TryGetValue(spawnId, out poolee) && ObjectSync.Live(poolee))
                poolee.ApplyHealth(value);

            if (Server.instance != null)
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, message.GetBytes(), sender);
        }
    }

    // The health of an NPC, from the machine that runs it.
    public class NpcHealthMessageData : NetworkMessageData {
        public ushort spawnId;
        public float value;
    }
}
