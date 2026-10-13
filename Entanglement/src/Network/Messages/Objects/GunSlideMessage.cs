using System;

using Entanglement.Patching;

namespace Entanglement.Network
{
    /// <summary>The owner is working their gun's slide by hand. See GunSlideSync.</summary>
    [Net.SkipHandleOnLoading]
    public class GunSlideMessageHandler : NetworkMessageHandler<GunSlideMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.GunSlide;

        public override NetworkMessage CreateMessage(GunSlideMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[sizeof(ushort) + 2];
            message.messageData[0] = (byte)(data.gunId & 0xFF);
            message.messageData[1] = (byte)(data.gunId >> 8);
            message.messageData[2] = (byte)data.step;
            message.messageData[3] = data.value;
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < 4)
                return;

            ushort gunId = (ushort)(message.messageData[0] | (message.messageData[1] << 8));
            GunSlideSync.Apply(gunId, (GunSlideSync.Step)message.messageData[2], message.messageData[3]);

            if (Server.instance != null)
                Server.instance.BroadcastMessageExcept(NetworkChannel.Object, message.GetBytes(), sender);
        }
    }

    public class GunSlideMessageData : NetworkMessageData {
        public ushort gunId;
        public GunSlideSync.Step step;
        public byte value;       // how far back for Percent, the slide's state for LetGo
    }
}
