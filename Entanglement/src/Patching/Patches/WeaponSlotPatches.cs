using UnityEngine;

using StressLevelZero.Pool;
using StressLevelZero.Props.Weapons;
using StressLevelZero.Interaction;
using StressLevelZero.Data;

using HarmonyLib;

using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Objects;

using System.Collections;

using MelonLoader;

namespace Entanglement.Patching
{
    [HarmonyPatch(typeof(HandWeaponSlotReciever), "MakeStatic")]
    public static class WeaponInsertPatch {
        public static void Prefix(this HandWeaponSlotReciever __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            TransformSyncable tran;
            if (tran = TransformSyncable.cache.Get(__instance.m_WeaponHost.rb.gameObject)) {
                TransformCollisionMessageData data = new TransformCollisionMessageData() { objectId = tran.objectId, enabled = false };

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCollision, data);
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

                TransformSyncMessageData syncData = new TransformSyncMessageData()
                {
                    objectId = tran.objectId,
                    simplifiedTransform = new SimplifiedTransform(tran.transform, tran.rb)
                };

                NetworkMessage syncMessage = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSync, syncData);
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, syncMessage.GetBytes());
            }
        }
    }

    [HarmonyPatch(typeof(HandWeaponSlotReciever), "MakeDynamic")]
    public static class WeaponExitPatch {
        public static void Prefix(this HandWeaponSlotReciever __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            TransformSyncable tran;
            if (tran = TransformSyncable.cache.Get(__instance.m_WeaponHost.rb.gameObject)) {
                TransformCollisionMessageData data = new TransformCollisionMessageData() { objectId = tran.objectId, enabled = true };

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCollision, data);
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

                TransformSyncMessageData syncData = new TransformSyncMessageData()
                {
                    objectId = tran.objectId,
                    simplifiedTransform = new SimplifiedTransform(tran.transform, tran.rb)
                };

                NetworkMessage syncMessage = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSync, syncData);
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, syncMessage.GetBytes());
            }
        }
    }
}
