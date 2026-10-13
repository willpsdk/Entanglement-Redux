using System;

using Entanglement.Patching;

namespace Entanglement.Network
{
    /// <summary>Someone racked a gun you're holding. See GunRackSync.</summary>
    [Net.SkipHandleOnLoading]
    public class GunRackMessageHandler : NetworkMessageHandler<GunRackMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.GunRack;

        public override NetworkMessage CreateMessage(GunRackMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = BitConverter.GetBytes(data.gunId);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < sizeof(ushort))
                return;

            GunRackSync.Apply(BitConverter.ToUInt16(message.messageData, 0));
        }
    }

    public class GunRackMessageData : NetworkMessageData {
        public ushort gunId;
    }
}
