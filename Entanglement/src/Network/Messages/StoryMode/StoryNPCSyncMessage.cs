using System;
using System.Collections.Generic;

using Entanglement.Data;
using Entanglement.Objects;
using Entanglement.Representation;

using MelonLoader;

using StressLevelZero.Interaction;

using UnityEngine;

namespace Entanglement.Network
{
    /// <summary>
    /// Syncs NPC/Character spawning and state across the network.
    /// When an NPC spawns, dies, or changes state, this message broadcasts the change.
    /// </summary>
    public class StoryNPCSyncMessageHandler : NetworkMessageHandler<StoryNPCSyncData>
    {
        public override byte? MessageIndex => BuiltInMessageType.StoryNPCSync;

        public override NetworkMessage CreateMessage(StoryNPCSyncData data)
        {
            NetworkMessage message = new NetworkMessage();
            List<byte> rawBytes = new List<byte>();

            // Serialize NPC data
            rawBytes.AddRange(System.BitConverter.GetBytes(data.npcInstanceId));
            rawBytes.Add(data.isAlive ? (byte)1 : (byte)0);
            rawBytes.Add(data.isActive ? (byte)1 : (byte)0);
            rawBytes.AddRange(System.BitConverter.GetBytes(data.health));
            
            // Position and rotation
            rawBytes.AddRange(System.BitConverter.GetBytes(data.position.x));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.position.y));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.position.z));

            rawBytes.AddRange(System.BitConverter.GetBytes(data.rotation.x));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.rotation.y));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.rotation.z));
            rawBytes.AddRange(System.BitConverter.GetBytes(data.rotation.w));

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
                int npcInstanceId = System.BitConverter.ToInt32(message.messageData, index);
                index += sizeof(int);

                bool isAlive = Convert.ToBoolean(message.messageData[index++]);
                bool isActive = Convert.ToBoolean(message.messageData[index++]);

                float health = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);

                Vector3 position = new Vector3();
                position.x = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                position.y = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                position.z = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);

                Quaternion rotation = new Quaternion();
                rotation.x = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                rotation.y = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                rotation.z = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);
                rotation.w = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);

                // Find NPC by instance ID and update state
                // This would require keeping a registry of NPCs - implementation depends on Boneworks API
                EntangleLogger.Verbose($"Received NPC sync: ID={npcInstanceId}, Alive={isAlive}, Health={health}");

                // Server relays to other clients
                if (Server.instance != null)
                {
                    byte[] msgBytes = message.GetBytes();
                    Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error handling StoryNPCSyncMessage: {ex.Message}");
            }
        }
    }

    public class StoryNPCSyncData : NetworkMessageData
    {
        public int npcInstanceId;
        public bool isAlive = true;
        public bool isActive = true;
        public float health = 100f;
        public Vector3 position = Vector3.zero;
        public Quaternion rotation = Quaternion.identity;
    }
}
