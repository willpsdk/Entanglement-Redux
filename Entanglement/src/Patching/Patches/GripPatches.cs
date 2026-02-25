using System;
using HarmonyLib;
using UnhollowerBaseLib;
using StressLevelZero.SFX;
using StressLevelZero.Rig;
using StressLevelZero;
using StressLevelZero.Interaction;
using StressLevelZero.Combat;
using UnityEngine;
using Entanglement.Representation;
using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Objects;
using Entanglement.Extensions;
using MelonLoader;
using System.Collections;

namespace Entanglement.Patching
{
    // --- EXISTING FORCE PULL PATCHES ---
    [HarmonyPatch(typeof(ForcePullGrip), "OnFarHandHoverUpdate")]
    public class ForcePullPatch
    {
        public static void Prefix(ForcePullGrip __instance, ref bool __state, Hand hand)
        {
            __state = __instance.pullCoroutine != null;
        }

        public static void Postfix(ForcePullGrip __instance, ref bool __state, Hand hand)
        {
            if (!(__instance.pullCoroutine != null && !__state))
                return;

            ObjectSync.OnGripAttached(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(ForcePullGrip), "CancelPull")]
    public class ForceCancelPatch
    {
        public static void Postfix(ForcePullGrip __instance, Hand hand)
        {
            ObjectSync.OnForcePullCancelled(__instance.gameObject);
        }
    }

    // --- ADDED: MULTI-GRAB / SHARED AUTHORITY PATCHES ---
    [HarmonyPatch(typeof(Grip), "OnAttachedToHand")]
    public class GripAttachedPatch
    {
        public static void Postfix(Grip __instance, Hand hand)
        {
            if (!SteamIntegration.hasLobby) return;

            TransformSyncable syncObj = TransformSyncable.cache.Get(__instance.gameObject);
            if (syncObj != null && !syncObj.IsOwner())
            {
                // The player grabbed an object they don't own. 
                // We ask the host/current owner for authority instead of stealing it violently.
                byte[] requestBytes = BitConverter.GetBytes(syncObj.objectId);
                NetworkMessage msg = NetworkMessage.CreateMessage(BuiltInMessageType.AuthorityRequest, requestBytes);
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msg.GetBytes());
            }

            ObjectSync.OnGripAttached(__instance.gameObject);
        }
    }
}