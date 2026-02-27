using System;
using System.Collections.Generic;

using MelonLoader;

using StressLevelZero.Interaction;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Data;

namespace Entanglement.Managers
{
    /// <summary>
    /// Manages Story Mode synchronization including NPCs, doors, and destructibles.
    /// Tracks state changes and broadcasts them to other players.
    /// </summary>
    public static class StoryModeSync
    {
        private static Dictionary<int, StoryNPCSyncData> syncedNPCs = new Dictionary<int, StoryNPCSyncData>();
        private static Dictionary<int, StoryDoorSyncData> syncedDoors = new Dictionary<int, StoryDoorSyncData>();
        private static Dictionary<int, StoryDestructibleSyncData> syncedDestructibles = new Dictionary<int, StoryDestructibleSyncData>();

        /// <summary>
        /// Registers an NPC for synchronization across the network.
        /// Call this when an NPC spawns.
        /// </summary>
        public static void RegisterNPC(int npcInstanceId, Transform npcTransform, bool isAlive = true, float health = 100f)
        {
            if (!SteamIntegration.hasLobby)
                return;

            if (!syncedNPCs.ContainsKey(npcInstanceId))
            {
                StoryNPCSyncData data = new StoryNPCSyncData
                {
                    npcInstanceId = npcInstanceId,
                    isAlive = isAlive,
                    isActive = true,
                    health = health,
                    position = npcTransform.position,
                    rotation = npcTransform.rotation
                };

                syncedNPCs[npcInstanceId] = data;

                // Send to network if host
                if (SteamIntegration.isHost)
                {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryNPCSync, data);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }
            }
        }

        /// <summary>
        /// Updates NPC state (position, health, alive status).
        /// Call this regularly for active NPCs.
        /// </summary>
        public static void UpdateNPC(int npcInstanceId, Transform npcTransform, bool isAlive, float health)
        {
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                return;

            if (syncedNPCs.ContainsKey(npcInstanceId))
            {
                StoryNPCSyncData data = syncedNPCs[npcInstanceId];
                
                // Only send update if significant change
                if (Vector3.Distance(data.position, npcTransform.position) > 0.1f ||
                    data.isAlive != isAlive ||
                    Mathf.Abs(data.health - health) > 5f)
                {
                    data.position = npcTransform.position;
                    data.rotation = npcTransform.rotation;
                    data.isAlive = isAlive;
                    data.health = health;

                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryNPCSync, data);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
                }
            }
        }

        /// <summary>
        /// Registers a door/interactable for synchronization.
        /// Call this when a door or interactive object is first encountered.
        /// </summary>
        public static void RegisterDoor(int doorInstanceId, Vector3 position, bool isOpen = false, bool isLocked = false)
        {
            if (!SteamIntegration.hasLobby)
                return;

            if (!syncedDoors.ContainsKey(doorInstanceId))
            {
                StoryDoorSyncData data = new StoryDoorSyncData
                {
                    doorInstanceId = doorInstanceId,
                    isOpen = isOpen,
                    isLocked = isLocked,
                    state = isOpen ? 1f : 0f,
                    position = position
                };

                syncedDoors[doorInstanceId] = data;

                // Send to network if host
                if (SteamIntegration.isHost)
                {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryDoorSync, data);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }
            }
        }

        /// <summary>
        /// Updates door state (open/closed).
        /// Call this when a door state changes.
        /// </summary>
        public static void UpdateDoor(int doorInstanceId, bool isOpen, bool isLocked = false, float animState = 0f)
        {
            if (!SteamIntegration.hasLobby)
                return;

            if (syncedDoors.ContainsKey(doorInstanceId))
            {
                StoryDoorSyncData data = syncedDoors[doorInstanceId];

                if (data.isOpen != isOpen || data.isLocked != isLocked)
                {
                    data.isOpen = isOpen;
                    data.isLocked = isLocked;
                    data.state = animState;

                    // FIX: Both host and clients should broadcast door changes immediately
                    // to prevent desync when multiple players interact with doors
                    if (Node.activeNode != null)
                    {
                        NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryDoorSync, data);
                        if (SteamIntegration.isHost)
                        {
                            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                        }
                        else
                        {
                            // Clients send to host
                            Node.activeNode.SendMessage(SteamIntegration.hostUser.m_SteamID, NetworkChannel.Reliable, message.GetBytes());
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Registers a destructible object for synchronization.
        /// Call this when a destructible is encountered.
        /// </summary>
        public static void RegisterDestructible(int destructibleId, Vector3 position, float health = 100f)
        {
            if (!SteamIntegration.hasLobby)
                return;

            if (!syncedDestructibles.ContainsKey(destructibleId))
            {
                StoryDestructibleSyncData data = new StoryDestructibleSyncData
                {
                    destructibleInstanceId = destructibleId,
                    isDestroyed = false,
                    health = health,
                    damageAmount = 0f,
                    damagePosition = position
                };

                syncedDestructibles[destructibleId] = data;

                // Send to network if host
                if (SteamIntegration.isHost)
                {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryDestructibleSync, data);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }
            }
        }

        /// <summary>
        /// Updates destructible state (health, damage, destruction).
        /// Call this when a destructible takes damage or is destroyed.
        /// </summary>
        public static void UpdateDestructible(int destructibleId, bool isDestroyed, float health, Vector3 damagePos, float damageAmount = 0f)
        {
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                return;

            if (syncedDestructibles.ContainsKey(destructibleId))
            {
                StoryDestructibleSyncData data = syncedDestructibles[destructibleId];
                
                if (data.isDestroyed != isDestroyed || Mathf.Abs(data.health - health) > 1f)
                {
                    data.isDestroyed = isDestroyed;
                    data.health = health;
                    data.damageAmount = damageAmount;
                    data.damagePosition = damagePos;

                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryDestructibleSync, data);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }
            }
        }

        /// <summary>
        /// Clears all registered story mode objects on scene change.
        /// </summary>
        public static void ClearAll()
        {
            syncedNPCs.Clear();
            syncedDoors.Clear();
            syncedDestructibles.Clear();
        }

        /// <summary>
        /// Gets the synchronized state of an NPC.
        /// Used by clients to apply received state to their game world.
        /// </summary>
        public static bool TryGetNPCState(int npcInstanceId, out StoryNPCSyncData data)
        {
            return syncedNPCs.TryGetValue(npcInstanceId, out data);
        }

        /// <summary>
        /// Gets the synchronized state of a door.
        /// </summary>
        public static bool TryGetDoorState(int doorInstanceId, out StoryDoorSyncData data)
        {
            return syncedDoors.TryGetValue(doorInstanceId, out data);
        }

        /// <summary>
        /// Gets the synchronized state of a destructible.
        /// </summary>
        public static bool TryGetDestructibleState(int destructibleId, out StoryDestructibleSyncData data)
        {
            return syncedDestructibles.TryGetValue(destructibleId, out data);
        }
    }
}
