using StressLevelZero.Props;
using Entanglement.Objects;
using Entanglement.Network;
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
            if (!SteamIntegration.hasLobby) return;

            TransformSyncable syncTransform = TransformSyncable.DestructCache.Get(__instance.gameObject);
            if (syncTransform)
            {
                if (!syncTransform.IsOwner()) return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ObjectDestroy, new ObjectDestroyMessageData() { objectId = syncTransform.objectId });
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
            else
            {
                // Map object (lock/plank)
                if (!SteamIntegration.isHost) return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MapObjectDestroy, new MapObjectDestroyMessageData() { objectPath = DestructablePathHelper.GetGameObjectPath(__instance.gameObject) });
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDestructable), "TakeDamage")]
    public static class DestructablePatch
    {
        public static void Postfix(ObjectDestructable __instance)
        {
            if (!SteamIntegration.hasLobby) return;
            if (!__instance._isDead) return;

            TransformSyncable syncTransform = TransformSyncable.DestructCache.Get(__instance.gameObject);
            if (syncTransform)
            {
                if (!syncTransform.IsOwner()) return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ObjectDestroy, new ObjectDestroyMessageData() { objectId = syncTransform.objectId });
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
            else
            {
                if (!SteamIntegration.isHost) return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MapObjectDestroy, new MapObjectDestroyMessageData() { objectPath = DestructablePathHelper.GetGameObjectPath(__instance.gameObject) });
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
        }
    }
}