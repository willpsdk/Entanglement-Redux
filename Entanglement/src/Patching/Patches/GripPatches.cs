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
    [HarmonyPatch(typeof(ForcePullGrip), "OnFarHandHoverUpdate")]
    public class ForcePullPatch
    {
        public static bool Prefix(ForcePullGrip __instance, ref bool __state, Hand hand) {
            __state = __instance.pullCoroutine != null;

            if (!SteamIntegration.hasLobby)
                return true;

            // Check the full jointed rigidbody chain \u2014 if any body is owned by someone else, block the force grab.
            Rigidbody[] bodies = __instance.gameObject.transform.GetJointedBodies();
            ulong localId = SteamIntegration.currentUser.m_SteamID;

            for (int i = 0; i < bodies.Length; i++)
            {
                TransformSyncable syncObj = TransformSyncable.cache.Get(bodies[i].gameObject);
                if (syncObj != null && syncObj.ownerQueue != null && syncObj.ownerQueue.Count > 0)
                {
                    ulong activeOwner = syncObj.ownerQueue[0];
                    if (activeOwner != localId)
                    {
                        if (__instance.pullCoroutine != null)
                            __instance.CancelPull(hand);
                        return false;
                    }
                }
            }

            return true;
        }

        public static void Postfix(ForcePullGrip __instance, ref bool __state, Hand hand) {
            if (!(__instance.pullCoroutine != null && !__state))
                return;

            ObjectSync.OnGripAttached(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(ForcePullGrip), "CancelPull")]
    public class ForceCancelPatch
    {
        public static void Postfix(ForcePullGrip __instance, Hand hand) {
            ObjectSync.OnForcePullCancelled(__instance.gameObject);
        }
    }
}
