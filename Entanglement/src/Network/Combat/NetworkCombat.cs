using StressLevelZero.Combat;

using Entanglement.Gamemodes;
using Entanglement.Managers;

using UnityEngine;

namespace Entanglement.Network.Combat
{
    /// <summary>
    /// Fusion NetworkCombatManager ideology: every player-to-player hit (melee, gun, shove)
    /// goes through one victim-apply path — <see cref="PlayerAttackMessageHandler"/>.
    /// GunShot messages stay visual (projectile / muzzle / SFX); damage is not applied from FX.
    /// </summary>
    public static class NetworkCombat
    {
        public static bool CanAttack(long attacker, long victim) {
            return GamemodeManager.CanAttack(attacker, victim);
        }

        /// <summary>Attacker → victim. Returns true if a message was sent.</summary>
        public static bool SendPlayerAttack(
            long victimId,
            AttackType attackType,
            float damage,
            byte bodyPart = 0,
            float impulse = 0f,
            bool fromNpc = false)
        {
            if (!SteamIntegration.hasLobby || Node.activeNode == null)
                return false;
            if (victimId == 0 || victimId == SteamIntegration.currentUserId)
                return false;
            if (!fromNpc && !CanAttack(SteamIntegration.currentUserId, victimId))
                return false;

            NetworkMessage message = NetworkMessage.CreateMessage(
                (byte)BuiltInMessageType.PlayerAttack,
                new PlayerAttackMessageData {
                    attackType = attackType,
                    attackDamage = Mathf.Clamp(damage, 0.01f, 200f),
                    bodyPart = bodyPart,
                    impulse = Mathf.Clamp(impulse, 0f, 120f),
                    fromNpc = fromNpc,
                });
            if (message == null)
                return false;

            Node.activeNode.SendMessage(victimId, NetworkChannel.Attack, message.GetBytes());
            return true;
        }

        /// <summary>Gun hit on a remote player rep — same combat channel as punches.</summary>
        public static bool SendGunAttack(long victimId, float damage, byte bodyPart, float impulse) {
            return SendPlayerAttack(victimId, AttackType.Piercing, damage, bodyPart, impulse, false);
        }

        public static void NoteLocalHitByPlayer() {
            PlayerDeathManager.NoteHitByPlayer();
        }
    }
}
