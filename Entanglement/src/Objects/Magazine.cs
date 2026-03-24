using System;
using UnityEngine;
using StressLevelZero.Props.Weapons;
using StressLevelZero.Data;

namespace Entanglement.Objects
{
    // Extended stub for Magazine to fix missing field errors
    public class Magazine : MonoBehaviour
    {
        // Simulate the Cache property (replace with actual implementation if available)
        public static MagazineCache Cache = new MagazineCache();

        // Simulate magazineData (replace with actual type if available)
        public MagazineData magazineData;

        // Simulate cartridgeStates as int for now (replace with actual type if needed)
        public int cartridgeStates;
    }

    // Dummy cache class for Magazine
    public class MagazineCache
    {
        public Magazine Get(GameObject go) => go.GetComponent<Magazine>();
    }

    // Dummy magazine data class
    public class MagazineData
    {
        public SpawnableObject spawnableObject;
    }

    public static class MagazineReflectionHelper
    {
        public static object GetCartridgeStates(object magazine)
        {
            var type = magazine.GetType();
            var field = type.GetField("cartridgeStates");
            if (field != null) return field.GetValue(magazine);
            var prop = type.GetProperty("cartridgeStates");
            if (prop != null) return prop.GetValue(magazine);
            return null;
        }
        public static void SetCartridgeStates(object magazine, object value)
        {
            var type = magazine.GetType();
            var field = type.GetField("cartridgeStates");
            if (field != null) { field.SetValue(magazine, value); return; }
            var prop = type.GetProperty("cartridgeStates");
            if (prop != null && prop.CanWrite) { prop.SetValue(magazine, value); }
        }
    }
}
