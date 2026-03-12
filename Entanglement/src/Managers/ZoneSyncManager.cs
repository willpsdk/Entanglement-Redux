using System;
using System.Collections.Generic;
using System.Linq;

using Entanglement.Network;

namespace Entanglement.Managers
{
    public static class ZoneSyncManager
    {
        public enum ZoneStateId : byte
        {
            NotActivated = 0,
            Active = 1,
            Culled = 2,
        }

        private const string GlobalZoneKey = "GLOBAL";

        private static readonly Dictionary<string, ZoneStateId> zoneStates = new Dictionary<string, ZoneStateId>();
        private static readonly Dictionary<string, HashSet<string>> destroyedMapObjectsByZone = new Dictionary<string, HashSet<string>>();
        private static readonly Dictionary<string, Dictionary<int, StoryDoorSyncData>> doorStatesByZone = new Dictionary<string, Dictionary<int, StoryDoorSyncData>>();
        private static readonly Dictionary<string, Dictionary<int, StoryDestructibleSyncData>> destructibleStatesByZone = new Dictionary<string, Dictionary<int, StoryDestructibleSyncData>>();

        public static void Tick()
        {
            // Intentionally lightweight. Zone state is event-driven.
        }

        public static void ClearAll()
        {
            zoneStates.Clear();
            destroyedMapObjectsByZone.Clear();
            doorStatesByZone.Clear();
            destructibleStatesByZone.Clear();
        }

        public static void RegisterZoneEnter(string zonePath, bool isPlayerTrigger)
        {
            string zoneKey = BuildZoneKey(zonePath, isPlayerTrigger);
            zoneStates[zoneKey] = ZoneStateId.Active;

            if (SteamIntegration.hasLobby && SteamIntegration.isHost)
                BroadcastZoneState(zonePath, isPlayerTrigger, true, ZoneStateId.Active);
        }

        public static void SyncAndCullZone(string zonePath, bool isPlayerTrigger)
        {
            string zoneKey = BuildZoneKey(zonePath, isPlayerTrigger);
            zoneStates[zoneKey] = ZoneStateId.Culled;

            if (SteamIntegration.hasLobby && SteamIntegration.isHost)
            {
                BroadcastTrackedStateForZone(zoneKey);
                BroadcastZoneState(zonePath, isPlayerTrigger, false, ZoneStateId.Culled);
            }
        }

        public static void ApplyRemoteZoneState(string zonePath, bool isPlayerTrigger, byte zoneStateId)
        {
            string zoneKey = BuildZoneKey(zonePath, isPlayerTrigger);
            zoneStates[zoneKey] = (ZoneStateId)MathfClamp(zoneStateId, 0, 2);
        }

        public static void RecordMapObjectDestroyed(string objectPath)
        {
            if (string.IsNullOrEmpty(objectPath))
                return;

            string zoneKey = ResolveZoneForObjectPath(objectPath);
            if (!destroyedMapObjectsByZone.TryGetValue(zoneKey, out HashSet<string> paths))
            {
                paths = new HashSet<string>();
                destroyedMapObjectsByZone[zoneKey] = paths;
            }

            paths.Add(objectPath);
        }

        public static void RecordDoorState(StoryDoorSyncData data)
        {
            if (data == null)
                return;

            string zoneKey = ResolveAnyActiveZoneOrGlobal();
            if (!doorStatesByZone.TryGetValue(zoneKey, out Dictionary<int, StoryDoorSyncData> states))
            {
                states = new Dictionary<int, StoryDoorSyncData>();
                doorStatesByZone[zoneKey] = states;
            }

            states[data.doorInstanceId] = data;
        }

        public static void ApplyRemoteDoorState(StoryDoorSyncData data)
        {
            RecordDoorState(data);
        }

        public static void RecordDestructibleState(StoryDestructibleSyncData data)
        {
            if (data == null)
                return;

            string zoneKey = ResolveAnyActiveZoneOrGlobal();
            if (!destructibleStatesByZone.TryGetValue(zoneKey, out Dictionary<int, StoryDestructibleSyncData> states))
            {
                states = new Dictionary<int, StoryDestructibleSyncData>();
                destructibleStatesByZone[zoneKey] = states;
            }

            states[data.destructibleInstanceId] = data;
        }

        public static void ApplyRemoteDestructibleState(StoryDestructibleSyncData data)
        {
            RecordDestructibleState(data);
        }

        public static void SendFullStateTo(ulong userId)
        {
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost || Node.activeNode == null)
                return;

            foreach (var pair in zoneStates)
            {
                if (pair.Value == ZoneStateId.NotActivated)
                    continue;

                SplitZoneKey(pair.Key, out bool isPlayerTrigger, out string zonePath);
                bool isEnter = pair.Value == ZoneStateId.Active;

                ZoneTriggerMessageData triggerData = new ZoneTriggerMessageData
                {
                    zonePath = zonePath,
                    isPlayerTrigger = isPlayerTrigger,
                    isEnter = isEnter,
                    zoneStateId = (byte)pair.Value,
                };

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ZoneTrigger, triggerData);
                if (message != null)
                    Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
            }

            ReplayTrackedStateTo(userId, null);
        }

        private static void BroadcastTrackedStateForZone(string zoneKey)
        {
            ReplayTrackedStateTo(0, zoneKey);
        }

        private static void ReplayTrackedStateTo(ulong userId, string targetZoneKey)
        {
            bool targeted = !string.IsNullOrEmpty(targetZoneKey);

            foreach (var pair in destroyedMapObjectsByZone)
            {
                if (targeted && pair.Key != targetZoneKey)
                    continue;

                if (!ShouldSyncZone(pair.Key))
                    continue;

                foreach (string objectPath in pair.Value)
                {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MapObjectDestroy, new MapObjectDestroyMessageData { objectPath = objectPath });
                    if (message == null)
                        continue;

                    if (targeted)
                        Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                    else
                        Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
                }
            }

            foreach (var pair in doorStatesByZone)
            {
                if (targeted && pair.Key != targetZoneKey)
                    continue;

                if (!ShouldSyncZone(pair.Key))
                    continue;

                foreach (StoryDoorSyncData door in pair.Value.Values)
                {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryDoorSync, door);
                    if (message == null)
                        continue;

                    if (targeted)
                        Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                    else
                        Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
                }
            }

            foreach (var pair in destructibleStatesByZone)
            {
                if (targeted && pair.Key != targetZoneKey)
                    continue;

                if (!ShouldSyncZone(pair.Key))
                    continue;

                foreach (StoryDestructibleSyncData destructible in pair.Value.Values)
                {
                    NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.StoryDestructibleSync, destructible);
                    if (message == null)
                        continue;

                    if (targeted)
                        Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
                    else
                        Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
                }
            }
        }

        private static void BroadcastZoneState(string zonePath, bool isPlayerTrigger, bool isEnter, ZoneStateId stateId)
        {
            if (Node.activeNode == null)
                return;

            ZoneTriggerMessageData triggerData = new ZoneTriggerMessageData
            {
                zonePath = zonePath,
                isPlayerTrigger = isPlayerTrigger,
                isEnter = isEnter,
                zoneStateId = (byte)stateId,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ZoneTrigger, triggerData);
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        private static bool ShouldSyncZone(string zoneKey)
        {
            if (zoneKey == GlobalZoneKey)
                return true;

            if (!zoneStates.TryGetValue(zoneKey, out ZoneStateId state))
                return false;

            return state != ZoneStateId.NotActivated;
        }

        private static string ResolveZoneForObjectPath(string objectPath)
        {
            string bestKey = GlobalZoneKey;
            int bestLength = -1;

            foreach (var pair in zoneStates)
            {
                if (pair.Value == ZoneStateId.NotActivated)
                    continue;

                SplitZoneKey(pair.Key, out _, out string zonePath);
                if (string.IsNullOrEmpty(zonePath))
                    continue;

                if (!objectPath.StartsWith(zonePath, StringComparison.Ordinal))
                    continue;

                if (zonePath.Length > bestLength)
                {
                    bestLength = zonePath.Length;
                    bestKey = pair.Key;
                }
            }

            return bestKey;
        }

        private static string ResolveAnyActiveZoneOrGlobal()
        {
            foreach (var pair in zoneStates)
            {
                if (pair.Value == ZoneStateId.Active)
                    return pair.Key;
            }

            return GlobalZoneKey;
        }

        private static string BuildZoneKey(string zonePath, bool isPlayerTrigger)
        {
            return $"{(isPlayerTrigger ? "P" : "S")}|{zonePath}";
        }

        private static void SplitZoneKey(string zoneKey, out bool isPlayerTrigger, out string zonePath)
        {
            isPlayerTrigger = false;
            zonePath = zoneKey;

            if (zoneKey == GlobalZoneKey)
                return;

            int split = zoneKey.IndexOf('|');
            if (split <= 0 || split >= zoneKey.Length - 1)
                return;

            isPlayerTrigger = zoneKey[0] == 'P';
            zonePath = zoneKey.Substring(split + 1);
        }

        private static int MathfClamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
