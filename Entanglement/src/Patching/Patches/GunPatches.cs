using Entanglement.Objects;
using HarmonyLib;
using StressLevelZero.Props.Weapons;

namespace Entanglement.Patching
{
    // Make sure we are patching BONEWORKS' "EmptyFire" method, not "OnDryFire"
    [HarmonyPatch(typeof(Gun), "EmptyFire")]
    public class GunEmptyFirePatch
    {
        public static void Prefix(Gun __instance)
        {
            // CRITICAL IL2CPP/Unity Null Check
            if (__instance == null || __instance.Pointer == System.IntPtr.Zero) return;

            // Ensure we only broadcast if we are holding the gun and own it network-wise
            TransformSyncable gunSync = TransformSyncable.cache.Get(__instance.gameObject);
            if (!gunSync || !gunSync.IsOwner()) return;

        }
    }

}