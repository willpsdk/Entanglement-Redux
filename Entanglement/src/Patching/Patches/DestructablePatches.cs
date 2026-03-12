using StressLevelZero.Props;
using Entanglement.Objects;
using Entanglement.Network;
using Entanglement.Managers;
using HarmonyLib;
using UnityEngine;

namespace Entanglement.Patching
{
    public static class DestructablePathHelper
    {
        public static string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = obj.name + "/" + path;
            }
            return path;
        }
    }

    [HarmonyPatch(typeof(Prop_Health), "DESTROYED")]
    public static class PropHealthPatch
    {
        public static bool Prefix(Prop_Health __instance)
        {
            if (!__instance.impactSFX)
                return false;
            return true;
        }

        public static void Postfix(Prop_Health __instance)
        {
            TransformSyncable syncTransform = TransformSyncable.DestructCache.Get(__instance.gameObject);
            if (syncTransform)
            {
                if (!SteamIntegration.hasLobby) return;
                if (!syncTransform.IsOwner()) return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ObjectDestroy, new ObjectDestroyMessageData() { objectId = syncTransform.objectId });
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
            else
            {
                string objectPath = DestructablePathHelper.GetGameObjectPath(__instance.gameObject);
                ZoneSyncManager.RecordMapObjectDestroyed(objectPath);

                if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                    return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MapObjectDestroy, new MapObjectDestroyMessageData() { objectPath = objectPath });
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDestructable), "TakeDamage")]
    public static class DestructablePatch
    {
        public static void Postfix(ObjectDestructable __instance)
        {
            if (!__instance._isDead) return;

            TransformSyncable syncTransform = TransformSyncable.DestructCache.Get(__instance.gameObject);
            if (syncTransform)
            {
                if (!SteamIntegration.hasLobby) return;
                if (!syncTransform.IsOwner()) return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ObjectDestroy, new ObjectDestroyMessageData() { objectId = syncTransform.objectId });
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
            else
            {
                string objectPath = DestructablePathHelper.GetGameObjectPath(__instance.gameObject);
                ZoneSyncManager.RecordMapObjectDestroyed(objectPath);

                if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                    return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MapObjectDestroy, new MapObjectDestroyMessageData() { objectPath = objectPath });
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
        }
    }
}