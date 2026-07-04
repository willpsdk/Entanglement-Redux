using System;
using System.Text;

using Entanglement.Extensions;
using Entanglement.Objects;

using UnityEngine;

namespace Entanglement.Network
{
    public enum SceneEventType : byte {
        ButtonPress    = 0,
        ButtonDepress  = 1,
        KeyLock        = 2,
        KeyUnlock      = 3,
        MonoMatInsert  = 4,
        PullDevicePull = 5,
        NpcDeath       = 6,
        NpcDespawn     = 7,
    }

    /// <summary>
    /// Syncs story mode interactions that live on static scene objects (buttons, key receivers).
    /// The interaction inputs are synced and the scene logic they trigger runs locally on every client.
    /// </summary>
    [Net.SkipHandleOnLoading]
    public class SceneEventMessageHandler : NetworkMessageHandler<SceneEventMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.SceneEvent;

        public override NetworkMessage CreateMessage(SceneEventMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] utf8 = Encoding.UTF8.GetBytes(data.objectPath);

            message.messageData = new byte[sizeof(byte) + sizeof(ushort) + utf8.Length];

            int index = 0;
            message.messageData[index++] = (byte)data.eventType;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.arg), ref index);
            message.messageData = message.messageData.AddBytes(utf8, ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            SceneEventType eventType = (SceneEventType)message.messageData[index++];

            ushort arg = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            byte[] pathBytes = new byte[message.messageData.Length - index];
            for (int i = 0; i < pathBytes.Length; i++)
                pathBytes[i] = message.messageData[index++];

            string objectPath = Encoding.UTF8.GetString(pathBytes);

            SceneEventSync.ApplyRemoteEvent(eventType, arg, objectPath);

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
            }
        }
    }

    public class SceneEventMessageData : NetworkMessageData {
        public SceneEventType eventType;
        public ushort arg; // Event specific payload, e.g. the synced object id of an inserted magazine
        public string objectPath;
    }
}
