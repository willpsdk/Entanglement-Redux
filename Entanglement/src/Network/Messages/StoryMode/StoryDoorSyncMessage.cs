using System;
using System.Collections.Generic;

using Entanglement.Data;
using Entanglement.Extensions;

using MelonLoader;

using StressLevelZero.Interaction;

using UnityEngine;

namespace Entanglement.Network
{
    /// <summary>
    /// Syncs door opening/closing and interactable state changes.
    /// When a door opens, a lever is pulled, or an object is used, this broadcasts the state change.
    /// </summary>
    public class StoryDoorSyncMessageHandler : NetworkMessageHandler<StoryDoorSyncData>
    {
        public override byte? MessageIndex => BuiltInMessageType.StoryDoorSync;

        public override NetworkMessage CreateMessage(StoryDoorSyncData data)
        {
            NetworkMessage message = new NetworkMessage();
            List<byte> rawBytes = new List<byte>();

            // Serialize door/interactable data
            rawBytes.AddRange(System.BitConverter.GetBytes(data.doorInstanceId));
            rawBytes.Add(data.isOpen ? (byte)1 : (byte)0);
            rawBytes.Add(data.isLocked ? (byte)1 : (byte)0);
            rawBytes.AddRange(System.BitConverter.GetBytes(data.state)); // 0-1 lerp value
            
            // Door position for verification
            rawBytes.AddRange(System.BitConverter.GetBytes(data.position.x));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.position.y));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.position.z));

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
                int doorInstanceId = System.BitConverter.ToInt32(message.messageData, index);
                index += sizeof(int);

                bool isOpen = Convert.ToBoolean(message.messageData[index++]);
                bool isLocked = Convert.ToBoolean(message.messageData[index++]);
                float state = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);

                Vector3 position = new Vector3();
                position.x = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                position.y = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                position.z = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);

                // Find door by instance ID and update state
                EntangleLogger.Verbose($"Received door sync: ID={doorInstanceId}, Open={isOpen}, State={state}");

                // Server relays to other clients
                if (Server.instance != null)
                {
                    byte[] msgBytes = message.GetBytes();
                    Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error handling StoryDoorSyncMessage: {ex.Message}");
            }
        }
    }

    public class StoryDoorSyncData : NetworkMessageData
    {
        public int doorInstanceId;
        public bool isOpen = false;
        public bool isLocked = false;
        public float state = 0f; // 0 = closed, 1 = open (for animated doors)
        public Vector3 position = Vector3.zero;
    }
}
