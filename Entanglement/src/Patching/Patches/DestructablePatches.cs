using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StressLevelZero.Props;
using StressLevelZero.Combat;

using Entanglement.Objects;
using Entanglement.Network;

using MelonLoader;

using HarmonyLib;

using UnityEngine;

namespace Entanglement.Patching
{
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
                if (!syncTransform.IsOwner())
                    return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ObjectDestroy, new ObjectDestroyMessageData()
                {
                    objectId = syncTransform.objectId
                });

                byte[] msgBytes = message.GetBytes();
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msgBytes);
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDestructable), "TakeDamage")]
    public static class DestructablePatch
    {
        // FIX: Removed the unused arguments (normal, damage, crit, attackType) 
        // to prevent Harmony from throwing an ArgumentOutOfRangeException during injection
        public static void Postfix(ObjectDestructable __instance)
        {
            if (!SteamIntegration.hasLobby) return;

            if (!__instance._isDead) return;

            TransformSyncable syncTransform = TransformSyncable.DestructCache.Get(__instance.gameObject);
            if (syncTransform)
            {
                if (!syncTransform.IsOwner())
                    return;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ObjectDestroy, new ObjectDestroyMessageData()
                {
                    objectId = syncTransform.objectId
                });

                byte[] msgBytes = message.GetBytes();
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msgBytes);
            }
        }
    }
}