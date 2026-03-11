using HarmonyLib;

using System;

using Entanglement.Network;
using Entanglement.Extensions;

using StressLevelZero.Zones;

using UnityEngine;

using System.Collections.Generic;

namespace Entanglement.Patching
{
    public static class ZoneTrackingUtilities
    {
        public static Dictionary<SceneZone, int> zoneCount = new Dictionary<SceneZone, int>(new UnityComparer());
        public static Dictionary<PlayerTrigger, int> triggerCount = new Dictionary<PlayerTrigger, int>(new UnityComparer());
        public static bool networkReplay = false;

        public static void Increment(SceneZone zone)
        {
            if (!zoneCount.ContainsKey(zone))
                zoneCount.Add(zone, 0);

            zoneCount[zone]++;
        }

        public static void Decrement(SceneZone zone)
        {
            if (!zoneCount.ContainsKey(zone))
                zoneCount.Add(zone, 0);

            zoneCount[zone]--;
            zoneCount[zone] = Mathf.Clamp(zoneCount[zone], 0, int.MaxValue);
        }

        public static bool CanEnter(SceneZone zone)
        {
            if (!zoneCount.ContainsKey(zone))
                return false;

            return zoneCount[zone] <= 1;
        }

        public static bool CanExit(SceneZone zone)
        {
            if (!zoneCount.ContainsKey(zone))
                return false;

            return zoneCount[zone] <= 0;
        }

        public static void Increment(PlayerTrigger trigger)
        {
            if (!triggerCount.ContainsKey(trigger))
                triggerCount.Add(trigger, 0);

            triggerCount[trigger]++;
        }

        public static void Decrement(PlayerTrigger trigger)
        {
            if (!triggerCount.ContainsKey(trigger))
                triggerCount.Add(trigger, 0);

            triggerCount[trigger]--;
            triggerCount[trigger] = Mathf.Clamp(triggerCount[trigger], 0, int.MaxValue);
        }

        public static bool CanEnter(PlayerTrigger trigger)
        {
            if (!triggerCount.ContainsKey(trigger))
                return false;

            return triggerCount[trigger] <= 1;
        }

        public static bool CanExit(PlayerTrigger trigger)
        {
            if (!triggerCount.ContainsKey(trigger))
                return false;

            return triggerCount[trigger] <= 0;
        }

        public static bool IsNetworkPlayerRepCollider(Collider other)
        {
            if (other == null || other.transform == null)
                return false;

            Transform root = other.transform.root;
            if (root == null)
                return false;

            return root.name.StartsWith("PlayerRep.", StringComparison.Ordinal);
        }
    }

    [HarmonyPatch(typeof(SceneZone), "OnTriggerEnter")]
    public static class ZoneEnterPatch
    {
        public static bool Prefix(SceneZone __instance, Collider other)
        {
            if (__instance == null || other == null)
                return true;

            // Prevent scene zones from culling/despawning networked remote player representations.
            if (ZoneTrackingUtilities.IsNetworkPlayerRepCollider(other))
                return false;

            if (ZoneTrackingUtilities.networkReplay)
                return true;

            if (other.CompareTag("Player"))
            {
                ZoneTrackingUtilities.Increment(__instance);
                bool canEnter = ZoneTrackingUtilities.CanEnter(__instance);

                if (false && canEnter && SteamIntegration.isHost && SteamIntegration.hasLobby && Node.activeNode != null)
                {
                    ZoneTriggerMessageData triggerData = new ZoneTriggerMessageData
                    {
                        zonePath = __instance.transform.GetFullPath(),
                        isPlayerTrigger = false,
                        isEnter = true,
                    };

                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ZoneTrigger, triggerData);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }
#if DEBUG
                EntangleLogger.Log($"Entering SceneZone {__instance.name} with number {ZoneTrackingUtilities.zoneCount[__instance]} and result {canEnter}");
#endif

                return canEnter;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(SceneZone), "OnTriggerExit")]
    public static class ZoneExitPatch
    {
        public static bool Prefix(SceneZone __instance, Collider other)
        {
            if (__instance == null || other == null)
                return true;

            // Prevent scene zones from culling/despawning networked remote player representations.
            if (ZoneTrackingUtilities.IsNetworkPlayerRepCollider(other))
                return false;

            if (ZoneTrackingUtilities.networkReplay)
                return true;

            if (other.CompareTag("Player"))
            {
                ZoneTrackingUtilities.Decrement(__instance);
                bool canExit = ZoneTrackingUtilities.CanExit(__instance);

                if (false && canExit && SteamIntegration.isHost && SteamIntegration.hasLobby && Node.activeNode != null)
                {
                    ZoneTriggerMessageData triggerData = new ZoneTriggerMessageData
                    {
                        zonePath = __instance.transform.GetFullPath(),
                        isPlayerTrigger = false,
                        isEnter = false,
                    };

                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ZoneTrigger, triggerData);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }

#if DEBUG
                EntangleLogger.Log($"Exiting SceneZone {__instance.name} with number {ZoneTrackingUtilities.zoneCount[__instance]} and result {canExit}");
#endif

                return canExit;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerTrigger), "OnTriggerEnter")]
    public static class PlayerTriggerEnterPatch
    {
        public static bool Prefix(PlayerTrigger __instance, Collider other)
        {
            if (__instance == null || other == null)
                return true;

            // Prevent player triggers from processing networked remote player representations.
            if (ZoneTrackingUtilities.IsNetworkPlayerRepCollider(other))
                return false;

            if (ZoneTrackingUtilities.networkReplay)
                return true;

            if (other.CompareTag("Player"))
            {
                ZoneTrackingUtilities.Increment(__instance);
                bool canEnter = ZoneTrackingUtilities.CanEnter(__instance);

                if (false && canEnter && SteamIntegration.isHost && SteamIntegration.hasLobby && Node.activeNode != null)
                {
                    ZoneTriggerMessageData triggerData = new ZoneTriggerMessageData
                    {
                        zonePath = __instance.transform.GetFullPath(),
                        isPlayerTrigger = true,
                        isEnter = true,
                    };

                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ZoneTrigger, triggerData);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }
#if DEBUG
                EntangleLogger.Log($"Entering PlayerTrigger {__instance.name} with number {ZoneTrackingUtilities.triggerCount[__instance]} and result {canEnter}");
#endif

                return canEnter;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerTrigger), "OnTriggerExit")]
    public static class PlayerTriggerExitPatch
    {
        public static bool Prefix(PlayerTrigger __instance, Collider other)
        {
            if (__instance == null || other == null)
                return true;

            // Prevent player triggers from processing networked remote player representations.
            if (ZoneTrackingUtilities.IsNetworkPlayerRepCollider(other))
                return false;

            if (ZoneTrackingUtilities.networkReplay)
                return true;

            if (other.CompareTag("Player"))
            {
                ZoneTrackingUtilities.Decrement(__instance);
                bool canExit = ZoneTrackingUtilities.CanExit(__instance);

                if (false && canExit && SteamIntegration.isHost && SteamIntegration.hasLobby && Node.activeNode != null)
                {
                    ZoneTriggerMessageData triggerData = new ZoneTriggerMessageData
                    {
                        zonePath = __instance.transform.GetFullPath(),
                        isPlayerTrigger = true,
                        isEnter = false,
                    };

                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ZoneTrigger, triggerData);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                }

#if DEBUG
                EntangleLogger.Log($"Exiting PlayerTrigger {__instance.name} with number {ZoneTrackingUtilities.triggerCount[__instance]} and result {canExit}");
#endif

                return canExit;
            }

            return true;
        }
    }
}