using System;
using System.Collections.Generic;

using Entanglement.Data;

using UnityEngine;

namespace Entanglement.Network
{
    /// <summary>
    /// Syncs destructible objects state (windows, walls, obstacles).
    /// When something breaks or is destroyed, this broadcasts the destruction state.
    /// </summary>
    public class StoryDestructibleSyncMessageHandler : NetworkMessageHandler<StoryDestructibleSyncData>
    {
        public override byte? MessageIndex => BuiltInMessageType.StoryDestructibleSync;

        public override NetworkMessage CreateMessage(StoryDestructibleSyncData data)
        {
            NetworkMessage message = new NetworkMessage();
            List<byte> rawBytes = new List<byte>();

            // Serialize destructible data
            rawBytes.AddRange(System.BitConverter.GetBytes(data.destructibleInstanceId));
            rawBytes.Add(data.isDestroyed ? (byte)1 : (byte)0);
            rawBytes.AddRange(System.BitConverter.GetBytes(data.health));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.damageAmount));
            
            // Position of destruction
            rawBytes.AddRange(System.BitConverter.GetBytes(data.damagePosition.x));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.damagePosition.y));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.damagePosition.z));

            message.messageData = rawBytes.ToArray();
            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            try
            {
                if (message.messageData == null || message.messageData.Length <= 0)
                    return;

                int index = 0;
                int destructibleId = System.BitConverter.ToInt32(message.messageData, index);
                index += sizeof(int);

                bool isDestroyed = Convert.ToBoolean(message.messageData[index++]);
                float health = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                float damageAmount = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);

                Vector3 damagePos = new Vector3();
                damagePos.x = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                damagePos.y = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                damagePos.z = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);

                // Find destructible by instance ID and update state
                EntangleLogger.Verbose($"Received destructible sync: ID={destructibleId}, Destroyed={isDestroyed}, Health={health}");

                // Server relays to other clients
                if (Server.instance != null)
                {
                    byte[] msgBytes = message.GetBytes();
                    Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error handling StoryDestructibleSyncMessage: {ex.Message}");
            }
        }
    }

    public class StoryDestructibleSyncData : NetworkMessageData
    {
        public int destructibleInstanceId;
        public bool isDestroyed = false;
        public float health = 100f;
        public float damageAmount = 0f;
        public Vector3 damagePosition = Vector3.zero;
    }
}
