using System;

using UnityEngine;

using StressLevelZero.Combat;
using StabPoint = StressLevelZero.Combat.StabSlash.StabPoint;

using Entanglement.Network;
using Entanglement.Objects;

using HarmonyLib;

namespace Entanglement.Patching
{
    [HarmonyPatch(typeof(StabSlash.StabPoint), "SpawnStab")]
    public class StabPatch
    {
        public static void Postfix(StabPoint __instance, Transform tran, Collision c, float stabForce, ImpactProperties surfaceProperties) {
            if (!SteamIntegration.hasLobby)
                return;

            try
            {
                if (__instance.rb)
                {
                    TransformSyncable cachedTransform = TransformSyncable.cache.Get(__instance.rb.gameObject);
                    if (cachedTransform && !cachedTransform.IsOwner()) return;
                }

                Transform root = surfaceProperties.transform.root;
                string objName = root.name;
                if (!objName.Contains("PlayerRep"))
                    return;
                string[] playerName = objName.Split('.');
                if (playerName.Length < 2)
                    throw new IndexOutOfRangeException();
                long id = long.Parse(playerName[1]);

                NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.PlayerAttack, new PlayerAttackMessageData()
                {
                    attackType = AttackType.Stabbing,
                    attackDamage = __instance.damage * surfaceProperties.FireResistance // Since fire is unused its used as a damage multiplier for body parts
                });

                byte[] msgBytes = message.GetBytes();

                Node.activeNode.SendMessage(id, NetworkChannel.Attack, msgBytes);
            }
            catch { }
        }
    }

    /// <summary>
    /// Bats/knives apply blunt damage through ImpactProperties.ReceiveAttack.
    /// Stabbing is already networked via StabPoint; blunt hits against a PlayerRep were local-only.
    /// PlayerRep exists only for remote players, so a ReceiveAttack on one means the local attacker
    /// scored the hit on their machine.
    /// </summary>
    [HarmonyPatch(typeof(ImpactProperties), "ReceiveAttack")]
    public static class BluntMeleePatch
    {
        static float lastSendTime;
        static long lastSendTarget;

        public static void Postfix(ImpactProperties __instance, Attack attack) {
            if (!SteamIntegration.hasLobby || attack == null)
                return;

            try {
                // Stabs are already sent by StabPatch; avoid double-damage
                if (attack.attackType == AttackType.Stabbing)
                    return;

                if (attack.damage <= 0.01f)
                    return;

                Transform root = __instance.transform.root;
                string objName = root.name;
                if (!objName.Contains("PlayerRep"))
                    return;

                string[] playerName = objName.Split('.');
                if (playerName.Length < 2)
                    return;
                long id = long.Parse(playerName[1]);

                // Collision callbacks can spam for one swing
                if (id == lastSendTarget && Time.time - lastSendTime < 0.08f)
                    return;
                lastSendTarget = id;
                lastSendTime = Time.time;

                float multiplier = 1f;
                try { multiplier = __instance.FireResistance; } catch { }

                NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.PlayerAttack, new PlayerAttackMessageData()
                {
                    attackType = AttackType.Blunt,
                    attackDamage = attack.damage * multiplier
                });

                Node.activeNode.SendMessage(id, NetworkChannel.Attack, message.GetBytes());
            }
            catch { }
        }
    }
}
