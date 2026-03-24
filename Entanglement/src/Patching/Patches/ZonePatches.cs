using HarmonyLib;
using Entanglement.Network;
using StressLevelZero.Zones;
using UnityEngine;
using System.Collections.Generic;
using MelonLoader;
using Entanglement.Data;

namespace Entanglement.Patching
{
    public static class ZoneTrackingUtilities
    {
        public static Dictionary<SceneZone, int> zoneCount = new Dictionary<SceneZone, int>();
        public static Dictionary<PlayerTrigger, int> triggerCount = new Dictionary<PlayerTrigger, int>();
        public static bool networkIgnore = false; 

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

        // FIX: The Network Override. Call this when receiving a ZoneTriggerMessage from the Host!
        public static void ForceZoneTriggerFromNetwork(string zoneName)
        {
            GameObject zoneObj = GameObject.Find(zoneName);
            if (zoneObj != null)
            {
                SceneZone zone = zoneObj.GetComponent<SceneZone>();
                if (zone != null && PlayerScripts.playerRig != null)
                {
                    EntangleLogger.Log($"[ZoneSync] Forcing local Zone Trigger for: {zoneName}", System.ConsoleColor.Yellow);
                    networkIgnore = true; // Prevent infinite network loops
                    
                    // Grab the player's collider to fool the zone into triggering locally
                    Collider playerCollider = PlayerScripts.playerRig.GetComponentInChildren<Collider>();
                    if (playerCollider != null)
                    {
                        zone.OnTriggerEnter(playerCollider);
                    }
                    
                    networkIgnore = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(SceneZone), "OnTriggerEnter")]
    public static class ZoneEnterPatch
    {
        public static bool Prefix(SceneZone __instance, Collider other)
        {
            if (__instance == null || other == null) return true;
            if (ZoneTrackingUtilities.networkIgnore) return true; // Ignore artificial network triggers

            try
            {
                if (!other.CompareTag("Player")) return true;

                bool canEnter = ZoneTrackingUtilities.CanEnter(__instance);

                if (canEnter && SteamIntegration.isHost && SteamIntegration.hasLobby && Node.activeNode != null)
                {
                    try
                    {
                        if (__instance.gameObject != null)
                        {
                            // Send the exact zone name to the clients so they can spawn the same enemies
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
                        EntangleLogger.Verbose($"[ZoneEnterPatch] Broadcast failed: {ex.Message}");
                    }
                }

                return canEnter;
            }
            catch
            {
                return true;
            }
        }
    }
}