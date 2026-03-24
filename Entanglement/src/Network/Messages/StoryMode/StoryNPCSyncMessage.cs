using System;
using System.Collections.Generic;
using System.Linq;
using Entanglement.Data;
using Entanglement.Objects;
using Entanglement.Representation;
using MelonLoader;
using StressLevelZero.Interaction;
using StressLevelZero.AI; // Needed for AIBrain
using UnityEngine;

namespace Entanglement.Network
{
    // FIX: The custom smoothing component that runs on the enemy's body
    [RegisterTypeInIl2Cpp]
    public class SmoothNPCSync : MonoBehaviour
    {
        public SmoothNPCSync(IntPtr intPtr) : base(intPtr) { }

        public Vector3 targetPosition;
        public Quaternion targetRotation;
        public float health;
        public bool isAlive;

        void Awake()
        {
            targetPosition = transform.position;
            targetRotation = transform.rotation;
        }

        void Update()
        {
            // Buttery smooth 10x Lerp toward the Host's reported position
            if (Vector3.Distance(transform.position, targetPosition) > 0.05f)
            {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * 12f);
                transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * 12f);
            }
        }
    }

    public class StoryNPCSyncMessageHandler : NetworkMessageHandler<StoryNPCSyncData>
    {
        public override byte? MessageIndex => BuiltInMessageType.StoryNPCSync;

        // Fast cache to prevent heavy GameObject.Find searches every frame
        private static Dictionary<int, SmoothNPCSync> npcCache = new Dictionary<int, SmoothNPCSync>();

        public override NetworkMessage CreateMessage(StoryNPCSyncData data)
        {
            NetworkMessage message = new NetworkMessage();
            List<byte> rawBytes = new List<byte>();

            rawBytes.AddRange(System.BitConverter.GetBytes(data.npcInstanceId));
            rawBytes.Add(data.isAlive ? (byte)1 : (byte)0);
            rawBytes.Add(data.isActive ? (byte)1 : (byte)0);
            rawBytes.AddRange(System.BitConverter.GetBytes(data.health));
            
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
                if (message.messageData == null || message.messageData.Length < 34) return;

                int index = 0;
                int npcInstanceId = System.BitConverter.ToInt32(message.messageData, index);
                index += sizeof(int);

                bool isAlive = message.messageData[index++] == 1;
                bool isActive = message.messageData[index++] == 1;

                float health = System.BitConverter.ToSingle(message.messageData, index);
                index += sizeof(float);

                Vector3 position = Vector3.zero;
                position.x = System.BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                position.y = System.BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                position.z = System.BitConverter.ToSingle(message.messageData, index); index += sizeof(float);

                Quaternion rotation = Quaternion.identity;
                rotation.x = System.BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                rotation.y = System.BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                rotation.z = System.BitConverter.ToSingle(message.messageData, index); index += sizeof(float);
                rotation.w = System.BitConverter.ToSingle(message.messageData, index); index += sizeof(float);

                // FIX: Apply smoothing dynamically to the enemy
                if (!npcCache.ContainsKey(npcInstanceId) || npcCache[npcInstanceId] == null)
                {
                    // Find the AIBrain matching this ID (Client side)
                    AIBrain[] brains = GameObject.FindObjectsOfType<AIBrain>();
                    AIBrain target = brains.FirstOrDefault(b => b.gameObject.GetInstanceID() == npcInstanceId);

                    if (target != null)
                    {
                        SmoothNPCSync smoothSync = target.gameObject.AddComponent<SmoothNPCSync>();
                        npcCache[npcInstanceId] = smoothSync;
                    }
                }

                // If we found and cached it, update the targets so the Update() loop can slide them smoothly
                if (npcCache.ContainsKey(npcInstanceId) && npcCache[npcInstanceId] != null)
                {
                    npcCache[npcInstanceId].targetPosition = position;
                    npcCache[npcInstanceId].targetRotation = rotation;
                    npcCache[npcInstanceId].health = health;
                    npcCache[npcInstanceId].isAlive = isAlive;

                    // Override local navigation completely
                    AIBrain brain = npcCache[npcInstanceId].GetComponent<AIBrain>();
                    // Use reflection to get navMeshAgent
                    var navMeshAgentProp = brain.GetType().GetProperty("navMeshAgent");
                    var navMeshAgent = navMeshAgentProp != null ? navMeshAgentProp.GetValue(brain) : null;
                    if (brain != null && navMeshAgent != null)
                    {
                        UnityEngine.AI.NavMeshAgent agent = brain.GetComponent<UnityEngine.AI.NavMeshAgent>();
                        if (agent != null) agent.enabled = false;
                    }
                }

                if (Server.instance != null)
                {
                    byte[] msgBytes = message.GetBytes();
                    Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, msgBytes, sender);
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