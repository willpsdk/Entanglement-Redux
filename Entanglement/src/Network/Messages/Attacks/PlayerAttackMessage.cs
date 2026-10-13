using System;

using Entanglement.Data;
using Entanglement.Representation;
using Entanglement.Extensions;

using StressLevelZero.Combat;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class PlayerAttackMessageHandler : NetworkMessageHandler<PlayerAttackMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PlayerAttack;

        public static event Action<long, float> OnDamageReceived;

        const byte FlagFromNpc = 1;

        // Damage travels as an unsigned short: x1000 reaches 65, the old x10000 stopped at 6.55 so a hard
        // hit was cut down to that. The same on both ends (a mixed pair of builds would read it wrong).
        const float DamageScale = 1000f;

        public override NetworkMessage CreateMessage(PlayerAttackMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(byte) + sizeof(ushort) + sizeof(byte) + (data.fromNpc ? sizeof(byte) : 0)];

            int index = 0;
            message.messageData[index++] = (byte)data.attackType;

            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes((ushort)Math.Min(data.attackDamage * DamageScale, ushort.MaxValue)), ref index);
            message.messageData[index++] = data.fromNpc ? FlagFromNpc : (byte)0;
            if (data.fromNpc)
                message.messageData[index++] = data.bodyPart;

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

            if ((flags & FlagFromNpc) != 0) {
                // An NPC that another machine runs hit our body there (NpcMeleeHook). The sender
                // only passes it on: not a player's hit, so the death-save still counts.
                byte part = index < message.messageData.Length ? message.messageData[index] : (byte)0;
                Entanglement.Patching.NpcMeleeHook.Receive(attackType, attackDamage, part);
                return;
            }

            // Before the damage: a hit that starts the death-save is finished, not saved.
            Entanglement.Managers.PlayerDeathManager.NoteHitByPlayer();
            float before = PlayerScripts.playerHealth.curr_Health;
            PlayerScripts.playerHealth.TAKEDAMAGE(attackDamage);
            if (EntangleLogger.verbose)
                EntangleLogger.Debug("[PvP] " + attackType + " hit from " + sender + " for " + attackDamage.ToString("F2") + ": health "
                    + before.ToString("F2") + " -> " + PlayerScripts.playerHealth.curr_Health.ToString("F2") + " of " + PlayerScripts.playerHealth.max_Health.ToString("F2")
                    + (PlayerScripts.playerHealth.alive ? "" : ", dead"));
            // A hit that took the last of it is final: no waiting for the next frame's check.
            Entanglement.Managers.PlayerDeathManager.CheckLethality();
            OnDamageReceived?.Invoke(sender, attackDamage);

            // Play Sound
            if (PlayerRepresentation.representations.ContainsKey(sender))
            {
                PlayerRepresentation rep = PlayerRepresentation.representations[sender];
                if (attackType == AttackType.Stabbing && rep.repStabSFX)
                    rep.repStabSFX.GunShot();
            }
        }
    }

    public class PlayerAttackMessageData : NetworkMessageData {
        public AttackType attackType = AttackType.None;
        public float attackDamage;
        /// <summary>Dealt by an NPC the sender runs, not by the sender.</summary>
        public bool fromNpc;
        /// <summary>Where that NPC's swing landed (PlayerDamageReceiver.BodyPart), 0 when unknown.</summary>
        public byte bodyPart;
    }
}
