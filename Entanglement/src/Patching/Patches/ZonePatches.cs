using HarmonyLib;
using Entanglement.Network;
using StressLevelZero.Zones;
using UnityEngine;
using System.Collections.Generic;

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
            if (other.CompareTag("Player"))
            {
                if (ZoneTrackingUtilities.networkIgnore) return true;

                bool canEnter = ZoneTrackingUtilities.CanEnter(__instance);

                // If the host triggers a zone, tell all clients to also trigger it
                if (canEnter && SteamIntegration.isHost && SteamIntegration.hasLobby)
                {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ZoneTrigger, new ZoneTriggerMessageData() { zonePath = __instance.gameObject.name });
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }

                return canEnter;
            }
            return true;
        }
    }
}