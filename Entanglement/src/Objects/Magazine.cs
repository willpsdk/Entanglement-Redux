using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace Entanglement.Objects
{
    public static class MagazineCache
    {
        // By explicitly stating "StressLevelZero.Props.Weapons.Magazine", C# won't use any local stubs
        private static Dictionary<GameObject, StressLevelZero.Props.Weapons.Magazine> cache = new Dictionary<GameObject, StressLevelZero.Props.Weapons.Magazine>();

        public static StressLevelZero.Props.Weapons.Magazine Get(GameObject go)
        {
            // 1. IL2CPP Null Check - Very important for multiplayer mods
            if (go == null || go.Pointer == IntPtr.Zero)
            {
                return null;
            }

            if (cache.TryGetValue(go, out StressLevelZero.Props.Weapons.Magazine cachedMag))
            {
                if (cachedMag != null && cachedMag.Pointer != IntPtr.Zero)
                {
                    return cachedMag;
                }
                else
                {
                    cache.Remove(go);
                }
            }

            // Force the GetComponent to look for the ACTUAL game's Magazine component
            StressLevelZero.Props.Weapons.Magazine newMag = go.GetComponent<StressLevelZero.Props.Weapons.Magazine>();

            if (newMag != null && newMag.Pointer != IntPtr.Zero)
            {
                cache.Add(go, newMag);
                return newMag;
            }

            return null;
        }

        public static void ClearCache()
        {
            cache.Clear();
        }
    }

    // This helper uses Harmony to sneakily grab the private bullet count from the game's code!
    public static class MagazineReflectionHelper
    {
        public static object GetCartridgeStates(StressLevelZero.Props.Weapons.Magazine magazine)
        {
            return AccessTools.Field(typeof(StressLevelZero.Props.Weapons.Magazine), "cartridgeStates")?.GetValue(magazine);
        }

        public static void SetCartridgeStates(StressLevelZero.Props.Weapons.Magazine magazine, int ammoCount)
        {
            AccessTools.Field(typeof(StressLevelZero.Props.Weapons.Magazine), "cartridgeStates")?.SetValue(magazine, ammoCount);
        }
    }
}