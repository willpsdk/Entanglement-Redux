using System;

using Entanglement.Data;
using Entanglement.Gamemodes;
using Entanglement.Representation;
using Entanglement.Extensions;

using StressLevelZero.Combat;

using UnityEngine;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class PlayerAttackMessageHandler : NetworkMessageHandler<PlayerAttackMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PlayerAttack;

        public static event Action<long, float> OnDamageReceived;

        const byte FlagFromNpc = 1;
        const byte FlagBodyPart = 2;
        const byte FlagImpulse = 4;

        // Damage travels as an unsigned short: x1000 reaches 65, the old x10000 stopped at 6.55 so a hard
        // hit was cut down to that. The same on both ends (a mixed pair of builds would read it wrong).
        const float DamageScale = 1000f;
        const float ImpulseScale = 10f;

        public override NetworkMessage CreateMessage(PlayerAttackMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte flags = 0;
            if (data.fromNpc)
                flags |= FlagFromNpc;
            if (data.bodyPart != 0)
                flags |= FlagBodyPart;
            if (data.impulse > 0f)
                flags |= FlagImpulse;

            int size = sizeof(byte) + sizeof(ushort) + sizeof(byte);
            if ((flags & FlagBodyPart) != 0)
                size += sizeof(byte);
            if ((flags & FlagImpulse) != 0)
                size += sizeof(ushort);

            message.messageData = new byte[size];

            int index = 0;
            message.messageData[index++] = (byte)data.attackType;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes((ushort)Math.Min(data.attackDamage * DamageScale, ushort.MaxValue)), ref index);
            message.messageData[index++] = flags;
            if ((flags & FlagBodyPart) != 0)
                message.messageData[index++] = data.bodyPart;
            if ((flags & FlagImpulse) != 0)
                message.messageData = message.messageData.AddBytes(BitConverter.GetBytes((ushort)Math.Min(data.impulse * ImpulseScale, ushort.MaxValue)), ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            AttackType attackType = (AttackType)message.messageData[index++];

            float attackDamage = BitConverter.ToUInt16(message.messageData, index) / DamageScale;
            index += sizeof(ushort);
            byte flags = index < message.messageData.Length ? message.messageData[index++] : (byte)0;

            byte bodyPart = 0;
            if ((flags & FlagBodyPart) != 0 && index < message.messageData.Length)
                bodyPart = message.messageData[index++];

            float impulse = 0f;
            if ((flags & FlagImpulse) != 0 && index + 1 < message.messageData.Length) {
                impulse = BitConverter.ToUInt16(message.messageData, index) / ImpulseScale;
                index += sizeof(ushort);
            }

            if ((flags & FlagFromNpc) != 0) {
                // An NPC that another machine runs hit our body there (NpcMeleeHook).
                Entanglement.Patching.NpcMeleeHook.Receive(attackType, attackDamage, bodyPart);
                return;
            }

            // Victim-apply (Fusion ideology): we trust the payload on our health, not local ghost hits.
            if (!GamemodeManager.CanAttack(sender, SteamIntegration.currentUserId))
                return;

            Entanglement.Managers.PlayerDeathManager.NoteHitByPlayer();
            float before = PlayerScripts.playerHealth.curr_Health;
            float finalDamage = ScaleByPart(attackDamage, bodyPart);
            PlayerScripts.playerHealth.TAKEDAMAGE(finalDamage);

            // Physical shove from networked impulse when present.
            if (impulse > 0.5f && PlayerScripts.playerPhysBody) {
                try {
                    Vector3 dir = Vector3.forward;
                    Vector3 self = PlayerRepresentation.syncedRoot
                        ? PlayerRepresentation.syncedRoot.position
                        : PlayerScripts.playerPhysBody.transform.position;
                    if (PlayerRepresentation.representations.TryGetValue(sender, out PlayerRepresentation rep) && rep != null && rep.repTransforms[0]) {
                        dir = self - rep.repTransforms[0].position;
                        dir.y *= 0.25f;
                        if (dir.sqrMagnitude < 0.01f)
                            dir = Vector3.forward;
                        else
                            dir.Normalize();
                    }
                    PlayerScripts.playerPhysBody.AddImpulseForce(dir * Mathf.Clamp(impulse * 0.35f, 1f, 25f));
                }
                catch { }
            }

            if (EntangleLogger.verbose)
                EntangleLogger.Debug("[PvP] " + attackType + " part=" + bodyPart + " from " + sender + " for " + finalDamage.ToString("F2")
                    + " (raw " + attackDamage.ToString("F2") + ", impulse " + impulse.ToString("F1") + "): health "
                    + before.ToString("F2") + " -> " + PlayerScripts.playerHealth.curr_Health.ToString("F2")
                    + (PlayerScripts.playerHealth.alive ? "" : ", dead"));

            Entanglement.Managers.PlayerDeathManager.CheckLethality();
            OnDamageReceived?.Invoke(sender, finalDamage);

            if (PlayerRepresentation.nxt.ContainsKey(sender)) {
                PlayerRepresentation rep = PlayerRepresentation.representations[sender];
                if (attackType == AttackType.Stabbing && rep.repStabSFX)
                    rep.repStabSFX.GunShot();
            }
        }

        static float ScaleByPart(float damage, byte part) {
            // PlayerDamageReceiver.BodyPart flags
            if (part == 0)
                return damage;
            if ((part & 1) != 0) // Head
                return damage * 1.35f;
            if ((part & 2) != 0) // Chest
                return damage * 1.1f;
            if ((part & 4) != 0) // Pelvis
                return damage;
            return damage * 0.9f; // limbs
        }
    }

    public class PlayerAttackMessageData : NetworkMessageData {
        public AttackType attackType = AttackType.None;
        public float attackDamage;
        /// <summary>Dealt by an NPC the sender runs, not by the sender.</summary>
        public bool fromNpc;
        /// <summary>Where the hit landed (PlayerDamageReceiver.BodyPart flags), 0 when unknown.</summary>
        public byte bodyPart;
        /// <summary>Optional physical impulse magnitude for victim shove.</summary>
        public float impulse;
    }
}
