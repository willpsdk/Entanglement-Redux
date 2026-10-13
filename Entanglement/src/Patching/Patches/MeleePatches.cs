using System;

using UnityEngine;

using StressLevelZero.Combat;
using StabPoint = StressLevelZero.Combat.StabSlash.StabPoint;

using Entanglement.Network;
using Entanglement.Objects;
using Entanglement.Gamemodes;

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

                // A blade in an NPC someone else runs: ask for it, so the stab counts and it fights us.
                PooleeSyncable npc = PooleeSyncable.SyncedNpcOf(surfaceProperties.transform);
                if (npc) {
                    npc.ClaimFight();
                    return;
                }

                Transform root = surfaceProperties.transform.root;
                string objName = root.name;
                if (!objName.Contains("PlayerRep"))
                    return;
                string[] playerName = objName.Split('.');
                if (playerName.Length < 2)
                    throw new IndexOutOfRangeException();
                long id = long.Parse(playerName[1]);
                if (!GamemodeManager.CanAttack(SteamIntegration.currentUserId, id))
                    return;

                NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.PlayerAttack, new PlayerAttackMessageData()
                {
                    attackType = AttackType.Stabbing,
                    attackDamage = __instance.damage * surfaceProperties.FireResistance,
                    bodyPart = 2, // chest default for stab; surface may not expose BodyPart
                    impulse = Mathf.Clamp(stabForce, 0f, 80f),
                });

                byte[] msgBytes = message.GetBytes();

                Node.activeNode.SendMessage(id, NetworkChannel.Attack, msgBytes);
            }
            catch { }
        }
    }
}
