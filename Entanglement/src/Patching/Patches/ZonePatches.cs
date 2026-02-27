using HarmonyLib;
using Entanglement.Network;
using StressLevelZero.Zones;
using UnityEngine;
using System.Collections.Generic;
using MelonLoader;

namespace Entanglement.Patching
{
    public static class ZoneTrackingUtilities
    {
        public static Dictionary<SceneZone, int> zoneCount = new Dictionary<SceneZone, int>();
        public static Dictionary<PlayerTrigger, int> triggerCount = new Dictionary<PlayerTrigger, int>();
        public static bool networkIgnore = false; // Prevents infinite loops when triggered by network

        public static bool CanEnter(SceneZone zone)
        {
            if (!zoneCount.ContainsKey(zone)) zoneCount.Add(zone, 0);
            zoneCount[zone]++;
            return zoneCount[zone] <= 1;
        }

        public static bool CanExit(SceneZone zone)
        {
            if (!zoneCount.ContainsKey(zone)) zoneCount.Add(zone, 0);
            zoneCount[zone]--;
            return zoneCount[zone] <= 0;
        }
    }

    [HarmonyPatch(typeof(SceneZone), "OnTriggerEnter")]
    public static class ZoneEnterPatch
    {
        public static bool Prefix(SceneZone __instance, Collider other)
        {
            // CRITICAL FIX: Return immediately if any critical parameter is null
            if (__instance == null || other == null)
                return true;

            try
            {
                // Quick early returns for non-Player objects
                if (!other.CompareTag("Player"))
                    return true;

                if (ZoneTrackingUtilities.networkIgnore)
                    return true;

                bool canEnter = ZoneTrackingUtilities.CanEnter(__instance);

                // Only broadcast if we're the host with a lobby and have an active node
                if (canEnter && SteamIntegration.isHost && SteamIntegration.hasLobby && Node.activeNode != null)
                {
                    try
                    {
                        if (__instance.gameObject != null)
                        {
                            ZoneTriggerMessageData triggerData = new ZoneTriggerMessageData
                            {
                                zonePath = __instance.gameObject.name
                            };

                            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ZoneTrigger, triggerData);
                            if (message != null && message.messageData != null)
                            {
                                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        // Silently fail on broadcast - don't crash the zone
                        EntangleLogger.Verbose($"[ZoneEnterPatch] Broadcast failed: {ex.Message}");
                    }
                }

                return canEnter;
            }
            catch
            {
                // If anything goes wrong, just let the original method run
                return true;
            }
        }
    }
}