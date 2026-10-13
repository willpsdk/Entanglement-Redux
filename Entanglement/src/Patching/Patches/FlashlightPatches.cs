using System;

using HarmonyLib;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    public static class FlashlightSync
    {
        // Set while a remote toggle is being applied, so replaying it does not echo back out.
        public static bool isApplying;
    }

    [HarmonyPatch(typeof(PropFlashlight), "SwitchLight")]
    public static class FlashlightSwitchPatch
    {
        // Postfix: lightOn only holds the new value after the game has flipped it.
        public static void Postfix(PropFlashlight __instance) {
            if (FlashlightSync.isApplying || !SteamIntegration.hasLobby || __instance == null)
                return;

            try { Send(__instance); }
            catch (Exception e) { EntangleLogger.Warn("[Flashlight] Send failed: " + e.Message); }
        }

        static void Send(PropFlashlight light) {
            TransformSyncable sync = TransformSyncable.cache.Get(light.gameObject);
            if (!sync) {
                Rigidbody body = light.GetComponentInParent<Rigidbody>();
                if (body)
                    sync = TransformSyncable.cache.Get(body.gameObject);
            }

            if (!sync || !sync.isValid || sync.objectId == 0) {
                if (Entanglement.Objects.SceneEventSync.traceEnabled)
                    EntangleLogger.Warn("[SyncTrace] FLASHLIGHT toggle NOT sent - no synced id on "
                        + light.gameObject.name + " (has anyone grabbed it?)");
                return;
            }

            FlashlightMessageData data = new FlashlightMessageData {
                objectId = sync.objectId,
                on = light.lightOn,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.FlashlightState, data);
            if (message != null)
                Node.activeNode?.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            if (Entanglement.Objects.SceneEventSync.traceEnabled)
                EntangleLogger.Log("[SyncTrace] FLASHLIGHT sent id=" + sync.objectId + " on=" + light.lightOn
                    + " as=" + (SteamIntegration.isHost ? "HOST" : "CLIENT"));
        }
    }
}
