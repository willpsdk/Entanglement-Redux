using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using StressLevelZero.Interaction;
using StressLevelZero.Pool;
using StressLevelZero.Props.Weapons;
using StressLevelZero.Data;
using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    [HarmonyPatch(typeof(HandWeaponSlotReciever), "MakeStatic")]
    public static class WeaponInsertPatch
    {
        public static void Prefix(this HandWeaponSlotReciever __instance)
        {
            if (!SteamIntegration.hasLobby)
                return;

            TransformSyncable tran;
            if (tran = TransformSyncable.cache.Get(__instance.m_WeaponHost.rb.gameObject))
            {
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
    public static class WeaponExitPatch
    {
        public static void Prefix(this HandWeaponSlotReciever __instance)
        {
            if (!SteamIntegration.hasLobby)
                return;

            TransformSyncable tran;
            if (tran = TransformSyncable.cache.Get(__instance.m_WeaponHost.rb.gameObject))
            {
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

    [HarmonyPatch]
    public static class GenericHolsterSlotPatches
    {
        private static readonly List<MethodBase> ResolvedTargetMethods = new List<MethodBase>();

        private static readonly string[] SlotTypeNames = new string[]
        {
            "StressLevelZero.Interaction.InventorySlotReceiver",
            "StressLevelZero.Interaction.InventorySlotReciever",
            "StressLevelZero.Interaction.WeaponSlotReceiver",
            "StressLevelZero.Interaction.WeaponSlotReciever",
            "StressLevelZero.Interaction.BodySlotReceiver",
            "StressLevelZero.Interaction.BodySlotReciever",
        };

        [HarmonyPrepare]
        public static bool Prepare()
        {
            ResolvedTargetMethods.Clear();
            ResolvedTargetMethods.AddRange(FindTargetMethods());
            return ResolvedTargetMethods.Count > 0;
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            if (ResolvedTargetMethods.Count > 0)
                return ResolvedTargetMethods;

            return FindTargetMethods();
        }

        private static IEnumerable<MethodBase> FindTargetMethods()
        {
            HashSet<MethodBase> seen = new HashSet<MethodBase>();

            for (int i = 0; i < SlotTypeNames.Length; i++)
            {
                System.Type slotType = AccessTools.TypeByName(SlotTypeNames[i]);
                if (slotType == null)
                    continue;

                MethodInfo makeStatic = AccessTools.Method(slotType, "MakeStatic");
                if (makeStatic != null && seen.Add(makeStatic))
                    yield return makeStatic;

                MethodInfo makeDynamic = AccessTools.Method(slotType, "MakeDynamic");
                if (makeDynamic != null && seen.Add(makeDynamic))
                    yield return makeDynamic;
            }
        }

        public static void Postfix(object __instance, MethodBase __originalMethod)
        {
            if (!SteamIntegration.hasLobby || __instance == null || __originalMethod == null)
                return;

            bool enableCollision = __originalMethod.Name == "MakeDynamic";
            Rigidbody rb = TryGetSlotRigidbody(__instance);
            if (rb == null)
                return;

            TransformSyncable tran = TransformSyncable.cache.Get(rb.gameObject);
            if (!tran)
                return;

            TransformCollisionMessageData data = new TransformCollisionMessageData() { objectId = tran.objectId, enabled = enableCollision };
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

        private static Rigidbody TryGetSlotRigidbody(object slotInstance)
        {
            object host = AccessTools.Field(slotInstance.GetType(), "m_WeaponHost")?.GetValue(slotInstance)
                ?? AccessTools.Field(slotInstance.GetType(), "_weaponHost")?.GetValue(slotInstance)
                ?? AccessTools.Field(slotInstance.GetType(), "weaponHost")?.GetValue(slotInstance)
                ?? AccessTools.Field(slotInstance.GetType(), "m_SlotHost")?.GetValue(slotInstance)
                ?? AccessTools.Field(slotInstance.GetType(), "_slotHost")?.GetValue(slotInstance)
                ?? AccessTools.Field(slotInstance.GetType(), "slotHost")?.GetValue(slotInstance)
                ?? AccessTools.Field(slotInstance.GetType(), "m_Host")?.GetValue(slotInstance)
                ?? AccessTools.Field(slotInstance.GetType(), "_host")?.GetValue(slotInstance)
                ?? AccessTools.Field(slotInstance.GetType(), "host")?.GetValue(slotInstance)
                ?? slotInstance;

            if (host is Rigidbody rb)
                return rb;

            if (host is Component comp)
            {
                Rigidbody direct = comp.GetComponent<Rigidbody>();
                if (direct != null)
                    return direct;

                object rbField = AccessTools.Field(comp.GetType(), "rb")?.GetValue(comp)
                    ?? AccessTools.Field(comp.GetType(), "m_Rb")?.GetValue(comp)
                    ?? AccessTools.Property(comp.GetType(), "rb")?.GetValue(comp, null);

                if (rbField is Rigidbody fieldRb)
                    return fieldRb;
            }

            return null;
        }
    }
}
