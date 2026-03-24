using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using StressLevelZero.Combat;
using StressLevelZero.Props.Weapons;
using StressLevelZero.Pool;

using Entanglement.Data;
using Entanglement.Network;
using Entanglement.Objects; // Needed for cache check

using HarmonyLib;

namespace Entanglement.Patching
{
    [HarmonyPatch(typeof(Gun), "OnFire")]
    public class GunShotPatch
    {
        public static void Prefix(Gun __instance) {
            BulletObject bulletObject = __instance.chamberedCartridge;
            Transform firePoint = __instance.firePointTransform;

            if (!firePoint || !bulletObject) return;

            GunShotMessageData shotData = new GunShotMessageData()
            {
                userId = SteamIntegration.currentUser.m_SteamID,
                bulletObject = bulletObject,
                bulletTransform = new SimplifiedTransform(firePoint)
            };

            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.GunShot, shotData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Attack, message.GetBytes());
        }
    }
    
    // FIX: Catch Dry Fires to sync the tactical "Click"
    [HarmonyPatch(typeof(Gun), "EmptyFire")]
    public class GunEmptyFirePatch
    {
        public static void Prefix(Gun __instance) {
            // Ensure we only broadcast if we are holding the gun
            TransformSyncable gunSync = TransformSyncable.cache.Get(__instance.gameObject);
            if (!gunSync || !gunSync.IsOwner()) return;
        }
    }

    [HarmonyPatch(typeof(BalloonGun), "OnFire")]
    public class BalloonShotPatch
    {
        public static void Prefix(BalloonGun __instance) {
            Transform firePoint = __instance.firePointTransform;

            if (!firePoint) return;

            BalloonShotMessageData balloonData = new BalloonShotMessageData()
            {
                userId = SteamIntegration.currentUser.m_SteamID,
                balloonColor = __instance.currentColor,
                balloonTransform = new SimplifiedTransform(firePoint),
            };

            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.BalloonShot, balloonData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Attack, message.GetBytes());
        }
    }
}