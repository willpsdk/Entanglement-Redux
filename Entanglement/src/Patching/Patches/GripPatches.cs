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
        public static void Prefix(ForcePullGrip __instance, ref bool __state, Hand hand) {
            __state = __instance.pullCoroutine != null;
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

        public static Exception Finalizer(Exception __exception, ForcePullGrip __instance) {
            if (__exception == null)
                return null;
            PatchGuard.Report("ForcePullGrip.CancelPull (the game's own)", __exception);
            try {
                if (__instance)
                    ObjectSync.OnForcePullCancelled(__instance.gameObject);
            }
            catch (Exception e) { PatchGuard.Report("ForceCancelPatch.Finalizer", e); }
            return null;
        }
    }
}
