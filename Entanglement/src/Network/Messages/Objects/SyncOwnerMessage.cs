using System;

using Entanglement.Extensions;
using Entanglement.Objects;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class SyncOwnerMessageHandler : NetworkMessageHandler<SyncOwnerMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.SyncOwner;

        public override NetworkMessage CreateMessage(SyncOwnerMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[sizeof(ushort) + sizeof(long)];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.objectId), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.owner), ref index);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < sizeof(ushort) + sizeof(long))
                return;

            ushort objectId = BitConverter.ToUInt16(message.messageData, 0);
            long owner = BitConverter.ToInt64(message.messageData, sizeof(ushort));

            Syncable syncable;
            if (!ObjectSync.TryGetSyncable(objectId, out syncable) || syncable == null)
                return;

            if (Server.instance != null) {
                // Only for yourself, and never out of someone's hands.
                if (owner != sender || syncable.ownerQueue.Count > 0 || syncable.staleOwner == owner)
                    return;
                syncable.ForceOwner(owner);
                Server.instance.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
            else {
                if (sender != SteamIntegration.lobbyOwnerId)
                    return;
                syncable.ForceOwner(owner);
            }
        }
    }

    public class SyncOwnerMessageData : NetworkMessageData {
        public ushort objectId;
        public long owner;
    }
}
