using System;

using HarmonyLib;

using StressLevelZero.Props.Weapons;

using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    public static class GunRackSync
    {
        public static bool applying;

        public static void OnRackComplete(Gun gun) {
            if (applying || !SteamIntegration.hasLobby || Node.activeNode == null || !gun)
                return;

            TransformSyncable gunSync = TransformSyncable.cache.GetOrAdd(gun.gameObject);
            if (!gunSync || gunSync.objectId == 0)
                return;

            // Owner racked their own gun: it's already chambered where it will be fired.
            if (gunSync.IsOwner() || gunSync.staleOwner == 0)
                return;

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.GunRack,
                new GunRackMessageData { gunId = gunSync.objectId });
            if (message != null)
                Node.activeNode.SendMessage(gunSync.staleOwner, NetworkChannel.Reliable, message.GetBytes());
        }

        public static void Apply(ushort gunId) {
            if (!ObjectSync.TryGetSyncable(gunId, out Syncable syncable))
                return;

            TransformSyncable gunSync = syncable.TryCast<TransformSyncable>();
            if (!gunSync || !gunSync.IsOwner() || !gunSync._CachedGun)
                return;

            applying = true;
            try {
                // The same two steps the slide triggers at each end of its travel.
                gunSync._CachedGun.CompleteSlidePull();
                gunSync._CachedGun.CompleteSlideReturn();
            }
            catch (Exception e) {
                EntangleLogger.Warn("[GunRack] Could not rack gun " + gunId + ": " + e.Message);
            }
            finally {
                applying = false;
            }
        }
    }

    // Private_Void_0 in the decomp: private, no arguments, not virtual.
    [HarmonyPatch(typeof(Gun), "CompleteSlideReturn")]
    public static class GunRackPatch
    {
        public static void Postfix(Gun __instance) {
            try {
                GunRackSync.OnRackComplete(__instance);
                GunSlideSync.OnReturned(__instance);
            }
            catch (Exception e) { PatchGuard.Report("GunRackPatches.CompleteSlideReturn", e); }
        }
    }
}
