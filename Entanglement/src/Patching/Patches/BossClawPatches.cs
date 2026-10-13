using System;

using HarmonyLib;

using StressLevelZero.AI;

using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    [HarmonyPatch(typeof(BossClawAi), "TriggerStateChange")]
    public static class BossClawTriggerStateChangePatch
    {
        public static bool Prefix(TriggerRefProxy trp, bool enter) {
            try { return BossClawSync.AllowTriggerStateChange(trp, enter); }
            catch (Exception e) {
                // Never let a sync decision stop the stock claw from working.
                EntangleLogger.Warn("BossClaw trigger gate: " + e.Message);
                return true;
            }
        }
    }

    // The machine running the claw tells the others when it pounces and when its scoop opens or
    // closes. Their copies don't run its AI, so without this their scoop never moved and the
    // pounce sounds and effects never played (BossClawSync.ApplyEvent).
    [HarmonyPatch(typeof(BossClawAi), "SwitchPounceState")]
    public static class BossClawPouncePatch
    {
        public static void Prefix() => BossClawSync.pounceDepth++;

        public static void Postfix(BossClawAi __instance, BossClawAi.PounceState pState) {
            BossClawSync.pounceDepth--;
            try { BossClawSync.SendState(__instance, SceneEventType.ClawPounce, (ushort)pState); }
            catch (Exception e) { PatchGuard.Report("BossClawPouncePatch", e); }
        }
    }

    [HarmonyPatch(typeof(BossClawAi), "ToggleScoop")]
    public static class BossClawScoopPatch
    {
        public static void Postfix(BossClawAi __instance, bool toggleOn) {
            // A pounce switches the scoop itself; its own event covers that.
            if (BossClawSync.pounceDepth > 0)
                return;
            try { BossClawSync.SendState(__instance, SceneEventType.ClawScoop, (ushort)(toggleOn ? 1 : 0)); }
            catch (Exception e) { PatchGuard.Report("BossClawScoopPatch", e); }
        }
    }

    [HarmonyPatch(typeof(BossClawAi), "SwitchMentalState")]
    public static class BossClawMentalPatch
    {
        public static void Postfix(BossClawAi __instance, BossClawAi.MentalState mState) {
            try { BossClawSync.SendState(__instance, SceneEventType.ClawMental, (ushort)mState); }
            catch (Exception e) { PatchGuard.Report("BossClawMentalPatch", e); }
        }
    }
}
