using System;
using HarmonyLib;
using StressLevelZero.Interaction;
using StressLevelZero.Props;
using StressLevelZero.Props.Weapons;
using StressLevelZero.AI; // Needed for enemies
using Entanglement.Network;
using Entanglement.Managers;
using Entanglement.Data;
using UnityEngine;

namespace Entanglement.Patching
{
    public static class StoryModeSyncPatches
    {
        public static void Initialize()
        {
            EntangleLogger.Verbose("Story Mode Sync initialized");
        }
    }

    // FIX: Grab enemy positional data from the Host and broadcast it
    [HarmonyPatch(typeof(AIBrain), "Update")]
    public static class AIBrainSyncPatch
    {
        private static float syncTimer = 0f;

        public static void Postfix(AIBrain __instance)
        {
            // Only the Host controls enemy AI pathing
            if (!SteamIntegration.isHost || !SteamIntegration.hasLobby || Node.activeNode == null) return;
            if (__instance == null || __instance.gameObject == null) return;

            // Throttle to roughly 20Hz (Every 0.05 seconds) to save bandwidth. 
            // The SmoothNPCSync on the client will make up the missing frames!
            syncTimer += Time.deltaTime;
            if (syncTimer < 0.05f) return;
            syncTimer = 0f;

            StoryNPCSyncData npcData = new StoryNPCSyncData()
            {
                npcInstanceId = __instance.gameObject.GetInstanceID(),
                isAlive = __instance.behaviour.isAlive,
                isActive = __instance.gameObject.activeInHierarchy,
                health = __instance.behaviour.health,
                position = __instance.transform.position,
                rotation = __instance.transform.rotation
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryNPCSync, npcData);
            if (message != null)
            {
                Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
            }
        }
    }
}