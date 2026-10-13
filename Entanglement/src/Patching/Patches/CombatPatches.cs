using System;

using HarmonyLib;

using StressLevelZero.Combat;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Network.Combat;
using Entanglement.Representation;
using Entanglement.Gamemodes;

namespace Entanglement.Patching
{
    /// <summary>
    /// Unified combat: gun (and other Attack) hits on a PlayerRep go through PlayerAttack,
    /// same as melee — GunShot stays FX-only on remotes.
    /// </summary>
    [HarmonyPatch(typeof(ImpactProperties), "ReceiveAttack")]
    public static class RepCombatAttackPatch
    {
        static float nextSend;
        static long lastVictim;

        public static void Prefix(ImpactProperties __instance, Attack attack) {
            if (!SteamIntegration.hasLobby || Node.activeNode == null)
                return;
            // Cosmetic remote projectiles must not generate a second combat message.
            if (RemoteShot.active)
                return;

            try {
                Transform root = __instance.transform.root;
                string name = root ? root.name : null;
                if (string.IsNullOrEmpty(name) || !name.Contains("PlayerRep"))
                    return;

                string[] parts = name.Split('.');
                if (parts.Length < 2)
                    return;
                long victimId;
                if (!long.TryParse(parts[1], out victimId) || victimId == 0)
                    return;
                if (victimId == SteamIntegration.currentUserId)
                    return;
                if (!GamemodeManager.CanAttack(SteamIntegration.currentUserId, victimId))
                    return;

                float now = Time.realtimeSinceStartup;
                if (victimId == lastVictim && now < nextSend)
                    return;
                lastVictim = victimId;
                nextSend = now + 0.05f;

                float damage = 10f;
                AttackType type = AttackType.Piercing;
                float impulse = 8f;
                try {
                    damage = Mathf.Clamp(attack.damage, 0.5f, 120f);
                    type = attack.attackType;
                    if (type == AttackType.None)
                        type = AttackType.Piercing;
                    impulse = Mathf.Clamp(attack.force.magnitude, 0f, 80f);
                }
                catch { }

                byte bodyPart = 2;
                try {
                    string n = (__instance.name + " " + __instance.transform.name).ToLowerInvariant();
                    if (n.Contains("head") || n.Contains("skull"))
                        bodyPart = 1;
                    else if (n.Contains("pelvis") || n.Contains("hip"))
                        bodyPart = 4;
                }
                catch { }

                NetworkCombat.SendPlayerAttack(victimId, type, damage, bodyPart, impulse);
            }
            catch (Exception e) {
                PatchGuard.Report("RepCombatAttackPatch", e);
            }
        }
    }
}
