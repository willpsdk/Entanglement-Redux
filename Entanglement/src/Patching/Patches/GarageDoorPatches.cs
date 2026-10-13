using System;
using HarmonyLib;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    public static class GarageDoorSync
    {
        public static bool isRemote;

        public static void Send(SceneEventType type, Transform transform, ushort arg = 0) {
            if (isRemote || SceneEventSync.applyingRemote || !SteamIntegration.hasLobby || !transform)
                return;

            int id = transform.GetInstanceID();
            if (!SceneEventSync.MarkEvent(SceneEventSync.EventKey(id, type)))
                return;

            // Every machine opens or closes its own doors as the level starts.
            if (SceneEventSync.Settling)
                return;

            SceneEventSync.SendEvent(type, transform, arg);
        }

        public static ushort PackDirection(GarageDoorControl.Direction dir) {
            int v = (int)dir + 1;
            if (v < 0) v = 0;
            if (v > 2) v = 2;
            return (ushort)v;
        }

        public static GarageDoorControl.Direction UnpackDirection(ushort arg) {
            int v = arg;
            if (v > 2) v = 1;
            return (GarageDoorControl.Direction)(v - 1);
        }

        public static bool Apply(SceneEventType type, Transform target = null) {
            switch (type) {
                case SceneEventType.GarageDoorToggle: {
                    GarageDoor_Phys_Powerable door = target ? target.GetComponent<GarageDoor_Phys_Powerable>() : null;
                    if (door) {
                        isRemote = true;
                        try { door.ToggleVoltage(); }
                        catch (Exception e) { EntangleLogger.Debug("[GarageDoor] Toggle failed: " + e.Message); }
                        finally { isRemote = false; }
                    }
                    return true;
                }
                case SceneEventType.GarageDoorUp:
                case SceneEventType.GarageDoorDown:
                case SceneEventType.GarageDoorStop:
                case SceneEventType.GarageDoorUse:
                    return true;
                default:
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(GarageDoorPowerable), "UpGargeDoorBttn")]
    public static class GarageDoorUpPatch
    {
        public static void Postfix(GarageDoorPowerable __instance) {
            try {
                GarageDoorSync.Send(SceneEventType.GarageDoorUp, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("GarageDoorPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(GarageDoorPowerable), "DownGarageDoorBttn")]
    public static class GarageDoorDownPatch
    {
        public static void Postfix(GarageDoorPowerable __instance) {
            try {
                GarageDoorSync.Send(SceneEventType.GarageDoorDown, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("GarageDoorPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(GarageDoorPowerable), "StopGarageDoorBttn")]
    public static class GarageDoorStopPatch
    {
        public static void Postfix(GarageDoorPowerable __instance) {
            try {
                GarageDoorSync.Send(SceneEventType.GarageDoorStop, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("GarageDoorPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(GarageDoorControl), "UseGarageDoor")]
    public static class GarageDoorUsePatch
    {
        public static void Postfix(GarageDoorControl __instance, GarageDoorControl.Direction activateDir) {
            try {
                GarageDoorSync.Send(SceneEventType.GarageDoorUse, __instance.transform, GarageDoorSync.PackDirection(activateDir));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("GarageDoorPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(GarageDoor_Phys_Powerable), "ToggleVoltage")]
    public static class GarageDoorTogglePatch
    {
        public static bool Prefix(GarageDoor_Phys_Powerable __instance) => RemoteToggleGuard.Allow(__instance);

        public static void Postfix(GarageDoor_Phys_Powerable __instance) {
            try {
                GarageDoorSync.Send(SceneEventType.GarageDoorToggle, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("GarageDoorPatches.Postfix", e); }
        }
    }
}
