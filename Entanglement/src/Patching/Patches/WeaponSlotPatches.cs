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

            try {
                Rigidbody rb = __instance.m_WeaponHost ? __instance.m_WeaponHost.rb : null;
                TransformSyncable tran = rb ? TransformSyncable.cache.Get(rb.gameObject) : null;
                if (tran && tran.objectId != 0 && !tran.inSlotUi)
                    HolsterSync.SendSlotted(tran, true);
            }
            catch (System.Exception e) { Entanglement.Patching.PatchGuard.Report("WeaponInsertPatch", e); }
        }
    }

    [HarmonyPatch(typeof(HandWeaponSlotReciever), "MakeDynamic")]
    public static class WeaponExitPatch {
        public static void Prefix(this HandWeaponSlotReciever __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            try {
                Rigidbody rb = __instance.m_WeaponHost ? __instance.m_WeaponHost.rb : null;
                TransformSyncable tran = rb ? TransformSyncable.cache.Get(rb.gameObject) : null;
                if (tran && tran.objectId != 0 && !tran.inSlotUi)
                    HolsterSync.SendSlotted(tran, false);
            }
            catch (System.Exception e) { Entanglement.Patching.PatchGuard.Report("WeaponExitPatch", e); }
        }
    }

    // The radial menu's inventory takes your slots, items and all, out in front of you and back.
    [HarmonyPatch(typeof(HandWeaponSlotReciever), "SendToUI")]
    public static class WeaponSlotToUiPatch {
        public static void Prefix(HandWeaponSlotReciever __instance) {
            try { HolsterSync.OnSendToUI(__instance); }
            catch (System.Exception e) { Entanglement.Patching.PatchGuard.Report("WeaponSlotToUiPatch", e); }
        }
    }

    [HarmonyPatch(typeof(HandWeaponSlotReciever), "ReturnFromUI")]
    public static class WeaponSlotFromUiPatch {
        public static void Prefix(HandWeaponSlotReciever __instance) {
            try { HolsterSync.OnReturnFromUI(__instance); }
            catch (System.Exception e) { Entanglement.Patching.PatchGuard.Report("WeaponSlotFromUiPatch", e); }
        }
    }
}
