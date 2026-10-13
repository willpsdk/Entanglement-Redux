using System;

using HarmonyLib;

using UnhollowerBaseLib;

using StressLevelZero.SFX;
using StressLevelZero.Rig;
using StressLevelZero;
using StressLevelZero.Interaction;
using StressLevelZero.Combat;

using UnityEngine;

using Entanglement.Representation;
using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Objects;
using Entanglement.Extensions;

using MelonLoader;

namespace Entanglement.Patching
{
    [HarmonyPatch(typeof(Player_Health), "TAKEDAMAGE")]
    public static class PlayerDamagePatch
    {
        // Ignore damage that resets the death groan if currently dying.
        public static bool Prefix(Player_Health __instance, float damage, bool crit)
        {
            if (!__instance.alive)
                return false;
            // Another player's bullet reaching us. Noted here: OnReceivedDamage takes an Attack (see
            // NativeHook), and the game's damage receiver runs its own inlined copy of it anyway.
            if (SteamIntegration.hasLobby && RemoteShot.active) {
                Entanglement.Managers.PlayerDeathManager.NoteHitByPlayer();
                if (EntangleLogger.verbose)
                    EntangleLogger.Debug("[PvP] another player's bullet reached you for " + damage.ToString("F2") + ": health "
                        + __instance.curr_Health.ToString("F2") + " of " + __instance.max_Health.ToString("F2"));
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Hand), "AttachObject")]
    public static class GripAttachPatch
    {
        public static void Prefix(Hand __instance, GameObject objectToAttach) {
            ObjectSync.OnGripAttached(objectToAttach);
        }
    }

    [HarmonyPatch(typeof(Hand), "DetachObject")]
    public static class GripDetachPatch
    {
        public static void Prefix(Hand __instance, GameObject objectToDetach, bool restoreOriginalParent = true) {
            try { ObjectSync.OnGripDetached(__instance);
            }
            catch {
#if DEBUG
                EntangleLogger.Warn("Caught exception while detaching grip!");  
#endif   
            }
        }
    }

    [HarmonyPatch(typeof(HandSFX), "PunchAttack")]
    public static class PunchPatch
    {
        // The punch's attack reaches the NPC from inside this (see NpcHitHook).
        public static void Prefix(HandSFX __instance) => HitSource.Begin(__instance);

        public static void Postfix(HandSFX __instance, Collision c, float impulse, float relVelSqr, bool __result)
        {
            HitSource.End();
            try {
                if (!SteamIntegration.hasLobby)
                    return;

                Transform root = c.gameObject.transform.root;
                string objName = root.name;

                if (objName.Contains("PlayerRep")) {
                    string[] playerName = objName.Split('.');
                    if (playerName.Length < 2)
                        throw new IndexOutOfRangeException();
                    long id = long.Parse(playerName[1]);
                    // Stronger, less flat mapping — physical contact now carries shove; damage should match.
                    float damage = Mathf.Clamp(impulse / 2.5f, 2f, 40f);
                    NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.PlayerAttack, new PlayerAttackMessageData() {
                        attackType = AttackType.Blunt,
                        attackDamage = damage,
                    });

                    byte[] msgBytes = message.GetBytes();

                    Node.activeNode.SendMessage(id, NetworkChannel.Attack, msgBytes);
                    return;
                }

                // The fist's contact itself plays out on the owner's machine against our body there,
                // and the punch's attack goes there through NpcHitHook.
                if (!__result && PooleeSyncable.IsNpcBody(c.transform))
                    return;

                ObjectSync.OnBodyPunched(c.gameObject);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PlayerPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(SkeletonHand), "SetHandPose")]
    public static class PosePatch
    {
        public static int prevLeftPose = 0;
        public static int prevRightPose = 0;

        public static void Postfix(SkeletonHand __instance, string handPoseName)
        {
            try {
                if (!__instance.GetCharacterAnimationManager()) return;

                int poseIndex = PlayerScripts.playerHandPoses.IndexOf(handPoseName);
                if (poseIndex <= -1)
                    return;

                Handedness hand = __instance.handedness;

                // Skip same pose
                bool validMessage = true;
                if (hand == Handedness.LEFT) {
                    validMessage = prevLeftPose != poseIndex;
                    prevLeftPose = poseIndex;
                }
                else {
                    validMessage = prevRightPose != poseIndex;
                    prevRightPose = poseIndex;
                }

                if (!validMessage) return;

                HandPoseChangeMessageData poseData = new HandPoseChangeMessageData();

                poseData.userId = SteamIntegration.currentUserId;
                poseData.hand = hand;
                poseData.poseIndex = (ushort)poseIndex;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.HandPose, poseData);

                byte[] msgBytes = message.GetBytes();

                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msgBytes);

    #if DEBUG
                if (PlayerRepresentation.debugRepresentation != null) PlayerRepresentation.debugRepresentation.UpdatePose(poseData.hand, poseData.poseIndex);
    #endif
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PlayerPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(SkeletonHand), "SetCylinderRadius")]
    public static class GripRadiusPatch
    {
        public static int prevLeftRadius = -1;
        public static int prevRightRadius = -1;

        static bool Unchanged(int sent, int now) {
            if (sent < 0)
                return false;
            if (now == sent)
                return true;
            // Flickering across one step is still the wobble. Fully open or closed always goes.
            return now != 0 && now != 255 && Math.Abs(now - sent) < 2;
        }

        public static void Postfix(SkeletonHand __instance, float radius)
        {
            try {
                if (!__instance.GetCharacterAnimationManager()) return;

                Handedness hand = __instance.handedness;
                int quantized = Mathf.Clamp((int)(radius * 255f), 0, 255);

                int slot = hand == Handedness.LEFT ? 0 : hand == Handedness.RIGHT ? 1 : -1;
                if (slot >= 0) {
                    if (Unchanged(slot == 0 ? prevLeftRadius : prevRightRadius, quantized)) {
                        pending[slot] = -1;
                        return;
                    }
                    // The hand closing animates the radius every frame (up to ~200 reliable messages a
                    // second on Oct 8). At most 15 a second per hand go out; the latest value waits for
                    // its turn (Flush), and fully open or closed goes at once.
                    bool endpoint = quantized == 0 || quantized == 255;
                    if (!endpoint && Time.realtimeSinceStartup < nextSend[slot]) {
                        pending[slot] = quantized;
                        return;
                    }
                }

                Send(hand, slot, quantized, radius);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("PlayerPatches.Postfix", e); }
        }

        const float MinSendInterval = 1f / 15f;
        static readonly float[] nextSend = new float[2];
        static readonly int[] pending = { -1, -1 };

        /// <summary>Each frame: a held-back radius goes out once its hand's turn comes.</summary>
        public static void Flush() {
            if (Node.activeNode == null)
                return;
            float now = Time.realtimeSinceStartup;
            for (int slot = 0; slot < 2; slot++) {
                int value = pending[slot];
                if (value < 0 || now < nextSend[slot])
                    continue;
                Send(slot == 0 ? Handedness.LEFT : Handedness.RIGHT, slot, value, value / 255f);
            }
        }

        static void Send(Handedness hand, int slot, int quantized, float radius) {
            {
                if (slot == 0)
                    prevLeftRadius = quantized;
                else if (slot == 1)
                    prevRightRadius = quantized;
                if (slot >= 0) {
                    pending[slot] = -1;
                    nextSend[slot] = Time.realtimeSinceStartup + MinSendInterval;
                }

                GripRadiusMessageData radiusData = new GripRadiusMessageData();

                radiusData.userId = SteamIntegration.currentUserId;
                radiusData.hand = hand;
                radiusData.radius = radius;

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.GripRadius, radiusData);

                byte[] msgBytes = message.GetBytes();

                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msgBytes);

    #if DEBUG
                if (PlayerRepresentation.debugRepresentation != null) PlayerRepresentation.debugRepresentation.UpdatePoseRadius(radiusData.hand, radiusData.radius);
    #endif
            }
        }
    }
}
