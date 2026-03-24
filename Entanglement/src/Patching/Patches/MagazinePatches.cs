using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MelonLoader;

using StressLevelZero.Interaction;
using StressLevelZero.Props.Weapons;

using HarmonyLib;

using Entanglement.Objects;
using Entanglement.Network;

using UnityEngine;

namespace Entanglement.Patching {
    public static class Magazine_Settings {
        public static bool InGun(this MagazinePlug plug) {
            if (!plug) return false;

            Socket lastSocket = plug._lastSocket;
            if (lastSocket && lastSocket.LockedPlug == plug) return true;
            return false;
        }

        public static bool EnteringOrInside(this MagazinePlug plug) => plug._isEnterTransition || plug.InGun();

        public static void ForceEject(this MagazinePlug plug) {
            try
            {
                plug.EjectPlug();
                plug.ClearFromSocket();
            } catch { }
            plug.magazine.gameObject.SetActive(true);
            plug.magazine.transform.parent = null;
            plug._isEnterTransition = false;
            plug._isExitTransition = false;
            plug._isExitComplete = true;
        }
    }

    [HarmonyPatch(typeof(MagazinePlug), "OnPlugExitComplete")]
    public static class PlugExitPatch {
        public static void Postfix(MagazinePlug __instance)
        {
            TransformSyncable magSync = TransformSyncable.cache.GetOrAdd(__instance.magazine.gameObject);
            if (!magSync || !magSync.IsOwner()) {
                return;
            }

            MagazineSocket magSocket = __instance._lastSocket.Cast<MagazineSocket>();
            Gun gun = magSocket.GetComponentInParent<Gun>();
            if (!gun) {
                return;
            }

            TransformSyncable gunSync = TransformSyncable.cache.GetOrAdd(gun.gameObject);
            if (!gunSync || !gunSync.IsOwner()) {
                return;
            }

            var states = Entanglement.Objects.MagazineReflectionHelper.GetCartridgeStates(__instance.magazine);
            int ammoCount = (states is int i) ? i : (states != null ? System.Convert.ToInt32(states) : 0); // FIX: Send exact remaining bullets on eject
            MagazinePlugMessageData plugData = new MagazinePlugMessageData()
            {
                magId = magSync.objectId,
                gunId = gunSync.objectId,
                isInsert = false,
                ammoCount = ammoCount
            };
            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.MagazinePlug, plugData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }
    }

    [HarmonyPatch(typeof(MagazinePlug), "OnPlugInsertComplete")]
    public static class PlugEnterPatch {
        public static void Postfix(MagazinePlug __instance) {
            TransformSyncable magSync = TransformSyncable.cache.GetOrAdd(__instance.magazine.gameObject);
            if (!magSync || !magSync.IsOwner()) {
                return;
            }

            MagazineSocket magSocket = __instance._lastSocket.Cast<MagazineSocket>();
            Gun gun = magSocket.GetComponentInParent<Gun>();
            if (!gun) {
                return; 
            }

            TransformSyncable gunSync = TransformSyncable.cache.GetOrAdd(gun.gameObject);
            if (!gunSync || !gunSync.IsOwner()) {
                return;
            }

            var states = Entanglement.Objects.MagazineReflectionHelper.GetCartridgeStates(__instance.magazine);
            int ammoCount = (states is int i) ? i : (states != null ? System.Convert.ToInt32(states) : 0); // FIX: Send exact remaining bullets on insert
            MagazinePlugMessageData plugData = new MagazinePlugMessageData() {
                magId = magSync.objectId,
                gunId = gunSync.objectId,
                isInsert = true,
                ammoCount = ammoCount
            };

            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.MagazinePlug, plugData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }
    }
}