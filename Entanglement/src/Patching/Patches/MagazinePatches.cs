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

        /// <summary>A plug that is going into, sitting in or coming out of a socket (not a gun's magazine).</summary>
        public static bool InSocket(this StressLevelZero.Interaction.AlignPlug plug) {
            try {
                if (!plug)
                    return false;
                if (plug._isEnterTransition || plug._isExitTransition)
                    return true;
                Socket last = plug._lastSocket;
                return last && last.LockedPlug == plug;
            }
            catch { return false; }
        }

        /// <summary>One of the local player's hands is holding this (or something on it).</summary>
        public static bool HeldHere(Transform t) {
            if (!t)
                return false;
            return HeldBy(Entanglement.Data.PlayerScripts.playerLeftHand, t)
                || HeldBy(Entanglement.Data.PlayerScripts.playerRightHand, t);
        }

        static bool HeldBy(StressLevelZero.Interaction.Hand hand, Transform t) {
            try {
                if (!hand || !hand.m_CurrentAttachedObject)
                    return false;
                Transform held = hand.m_CurrentAttachedObject.transform;
                return held == t || held.IsChildOf(t);
            }
            catch { return false; }
        }

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
        // Anything thrown here went on up into the game's own eject.
        public static void Postfix(MagazinePlug __instance) {
            try { Send(__instance); }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PlugExitPatch", e); }
        }

        static void Send(MagazinePlug __instance)
        {
            // Replaying someone else's eject: they already told everyone.
            if (MagazinePlugMessageHandler.applying)
                return;

            TransformSyncable magSync = TransformSyncable.cache.GetOrAdd(__instance.magazine.gameObject);
            if (!magSync)
                return;
            if (MagazinePlugMessageHandler.Replayed(magSync.objectId))
                return;

            MagazineSocket magSocket = __instance._lastSocket.Cast<MagazineSocket>();
            Gun gun = magSocket.GetComponentInParent<Gun>();
            if (!gun) {
                EntangleLogger.Debug("[Mag] Eject of " + __instance.magazine.name + " not sent: its socket isn't on a gun");
                return;
            }

            TransformSyncable gunSync = TransformSyncable.cache.GetOrAdd(gun.gameObject);
            if (!gunSync || gunSync.objectId == 0) {
                EntangleLogger.Debug("[Mag] Eject of #" + magSync.objectId + " not sent: the gun isn't synced");
                return;
            }

            if (!magSync.IsOwner() && !gunSync.IsOwner() && !Magazine_Settings.HeldHere(__instance.magazine.transform)) {
                EntangleLogger.Debug("[Mag] Eject of #" + magSync.objectId + " not sent: not our mag, not in our hand, and not our gun");
                return;
            }

            MagazinePlugMessageData plugData = new MagazinePlugMessageData()
            {
                magId = magSync.objectId,
                gunId = gunSync.objectId,
                isInsert = false,
            };
            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.MagazinePlug, plugData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            EntangleLogger.Debug("[Mag] Sent eject of #" + plugData.magId + " from gun #" + plugData.gunId);
        }
    }

    [HarmonyPatch(typeof(MagazinePlug), "OnPlugInsertComplete")]
    public static class PlugEnterPatch {
        // Anything thrown here went on up into the game's own insert.
        public static void Postfix(MagazinePlug __instance) {
            try { Send(__instance); }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PlugEnterPatch", e); }
        }

        static void Send(MagazinePlug __instance) {
            if (MagazinePlugMessageHandler.applying)
                return;

            TransformSyncable magSync = TransformSyncable.cache.GetOrAdd(__instance.magazine.gameObject);
            if (!magSync || (!magSync.IsOwner() && !Magazine_Settings.HeldHere(__instance.magazine.transform))) {
                EntangleLogger.Debug("[Mag] Insert of " + __instance.magazine.name + " not sent: the mag isn't synced, ours, or in our hand");
                return;
            }
            if (MagazinePlugMessageHandler.Replayed(magSync.objectId))
                return;

            MagazineSocket magSocket = __instance._lastSocket.Cast<MagazineSocket>();
            Gun gun = magSocket.GetComponentInParent<Gun>();
            if (!gun) {
                EntangleLogger.Debug("[Mag] Insert of #" + magSync.objectId + " not sent: its socket isn't on a gun");
                return; 
            }

            TransformSyncable gunSync = TransformSyncable.cache.GetOrAdd(gun.gameObject);
            if (!gunSync || gunSync.objectId == 0) {
                EntangleLogger.Debug("[Mag] Insert of #" + magSync.objectId + " not sent: the gun isn't synced");
                return;
            }

            MagazinePlugMessageData plugData = new MagazinePlugMessageData() {
                magId = magSync.objectId,
                gunId = gunSync.objectId,
                isInsert = true
            };

            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.MagazinePlug, plugData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            if (gunSync.staleOwner != 0 && !gunSync.IsOwner())
                magSync.ForceOwner(gunSync.staleOwner, false);

            EntangleLogger.Debug("[Mag] Sent insert of #" + plugData.magId + " into gun #" + plugData.gunId);
        }
    }
}
