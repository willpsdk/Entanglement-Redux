using System;

using UnityEngine;

using Entanglement.Objects;

namespace Entanglement.Network
{
    public class FlashlightMessageHandler : NetworkMessageHandler<FlashlightMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.FlashlightState;

        public override NetworkMessage CreateMessage(FlashlightMessageData data) {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(ushort) + sizeof(byte)];

            int index = 0;
            message.messageData[index++] = (byte)(data.objectId & 0xFF);
            message.messageData[index++] = (byte)(data.objectId >> 8);
            message.messageData[index++] = (byte)(data.on ? 1 : 0);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < 3)
                return;

            int index = 0;
            ushort objectId = (ushort)(message.messageData[index] | (message.messageData[index + 1] << 8));
            index += sizeof(ushort);
            bool on = message.messageData[index] != 0;

            Syncable syncable;
            if (ObjectSync.TryGetSyncable(objectId, out syncable) && syncable != null)
                Apply(syncable, on);

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
            }
        }

        static void Apply(Syncable syncable, bool on) {
            Entanglement.Patching.FlashlightSync.isApplying = true;
            try {
                PropFlashlight light = syncable.GetComponent<PropFlashlight>();
                if (light == null)
                    light = syncable.GetComponentInChildren<PropFlashlight>(true);
                if (light == null)
                    return;

                if (light.lightOn == on)
                    return;

                light.SwitchLight();
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Flashlight] Apply failed: " + e.Message);
            }
            finally { Entanglement.Patching.FlashlightSync.isApplying = false; }
        }
    }

    public class FlashlightMessageData : NetworkMessageData {
        public ushort objectId;
        public bool on;
    }
}
