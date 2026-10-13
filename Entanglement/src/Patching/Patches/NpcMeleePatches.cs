using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using UnityEngine;

using UnhollowerBaseLib;

using PuppetMasta;

using StressLevelZero.Combat;

using Entanglement.Data;
using Entanglement.Network;
using Entanglement.Objects;
using Entanglement.Representation;

namespace Entanglement.Patching
{
    /// <summary>
    /// BehaviourBaseNav.DealMeleeDamage(MuscleCollision m, float impulse): an NPC's swing landed on
    /// something. The game's code (read from it) does nothing below an impulse of 1 or when the
    /// struck collider's layer is not in the NPC's melee mask; otherwise it hands a fixed blunt
    /// attack of 1.5 damage to the struck object's IAttackReceiver. Another player's body here is
    /// their rep's hit body, which has no receiver, so their game never heard of the hit. When a
    /// synced NPC this machine runs lands one on a rep, the same attack goes to that player, and
    /// their own body takes it the way it takes any NPC's swing (Receive).
    ///
    /// MuscleCollision holds a Collision reference, so this is a native hook (see NativeHook). An
    /// NPC copy that follows its owner never gets here: its contacts are dropped (NpcCollisionHook).
    /// </summary>
    public static class NpcMeleeHook
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void MeleeFn(IntPtr nav, IntPtr m, float impulse, IntPtr method);

        static readonly NativeHook hook = new NativeHook("BehaviourBaseNav.DealMeleeDamage");

        // What DealMeleeDamage builds, as its code has it.
        const float MinImpulse = 1f;
        public const float SwingDamage = 1.5f;

        static int collisionField = -1;

        /// <summary>Swings sent on to the player they landed on, since the level loaded.</summary>
        public static int relayed;

        public static void Attach() => hook.Attach(typeof(BehaviourBaseNav), "DealMeleeDamage", new MeleeFn(DealMeleeDamage));

        public static void OnSceneCleanup() {
            relayed = 0;
            receivers = null;
            receiversOf = null;
        }

        static unsafe void DealMeleeDamage(IntPtr nav, IntPtr m, float impulse, IntPtr method) {
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = m;
            args[1] = (IntPtr)(&impulse);
            hook.Invoke(nav, args);

            try {
                if (m != IntPtr.Zero && nav != IntPtr.Zero && impulse >= MinImpulse)
                    Relay(nav, m);
            }
            catch (Exception e) { PatchGuard.Report("NpcMeleeHook", e); }
        }

        static unsafe void Relay(IntPtr navPtr, IntPtr m) {
            // Only the machine running a synced NPC sends its swings. An unsynced one runs on every
            // machine at once, and each would send the same hit.
            PooleeSyncable npc = PooleeSyncable.OfBehaviour(navPtr);
            if (npc == null || !npc.RunsHere)
                return;

            if (collisionField == -1) {
                collisionField = GameCalls.FieldOffset(Il2CppClassPointerStore<MuscleCollision>.NativeClassPtr, "collision");
                if (collisionField < 0)
                    collisionField = int.MaxValue;
            }
            if (collisionField == int.MaxValue)
                return;
            IntPtr contact = *(IntPtr*)((byte*)m + collisionField);
            if (contact == IntPtr.Zero)
                return;

            Collider struck = new Collision(contact).collider;
            if (!struck)
                return;

            long victimId;
            PlayerDamageReceiver.BodyPart part;
            PlayerRepresentation victim = RepOf(struck, out victimId, out part);
            if (victim == null)
                return;

            // The game's own test for whether a contact is a hit.
            BehaviourBaseNav nav = new BehaviourBaseNav(navPtr);
            if ((nav.meleeAttackMask.value & (1 << struck.gameObject.layer)) == 0)
                return;

            relayed++;
            string who = NpcTrace.Name(npc.gameObject);
            EntangleLogger.Debug("[Attack] " + who + " hit " + victim.playerName + " (" + part + ")");

            if (victimId == PlayerRepresentation.TesterRepId) {
                Entanglement.UI.GameNotify.Show(who + " hit the dummy (" + part + "); a player would take that hit.", 2f);
                return;
            }
            if (!SteamIntegration.hasLobby || Node.activeNode == null)
                return;

            NetworkMessage message = NetworkMessage.CreateMessage((byte)BuiltInMessageType.PlayerAttack, new PlayerAttackMessageData() {
                attackType = AttackType.Blunt,
                attackDamage = SwingDamage,
                fromNpc = true,
                bodyPart = (byte)part,
            });
            Node.activeNode.SendMessage(victimId, NetworkChannel.Attack, message.GetBytes());
        }

        // The player whose rep hit body this is, and which part: its sphere is the head, its capsule the chest.
        static PlayerRepresentation RepOf(Collider col, out long id, out PlayerDamageReceiver.BodyPart part) {
            id = 0L;
            part = PlayerDamageReceiver.BodyPart.Chest;
            IntPtr ptr = col.Pointer;
            foreach (KeyValuePair<long, PlayerRepresentation> pair in PlayerRepresentation.Remotes()) {
                PlayerRepresentation rep = pair.Value;
                if (rep == null)
                    continue;
                Collider repHead = rep.EnemyHead;
                Collider repTorso = rep.EnemyTorso;
                if (repHead != null && repHead.Pointer == ptr)
                    part = PlayerDamageReceiver.BodyPart.Head;
                else if (repTorso == null || repTorso.Pointer != ptr)
                    continue;
                id = pair.Key;
                return rep;
            }
            return null;
        }

        // ---- the player who was hit ------------------------------------------------------------------

        static PlayerDamageReceiver[] receivers;
        static StressLevelZero.Rig.RigManager receiversOf;
        static bool warnedNoReceiver;

        /// <summary>
        /// An NPC that another machine runs hit our body there. Our body takes the same attack the
        /// way it takes any NPC's swing: PlayerDamageReceiver applies our head and crit multipliers
        /// and the damage, as it would here.
        /// </summary>
        public static void Receive(AttackType type, float damage, byte bodyPart) {
            PlayerDamageReceiver.BodyPart part = bodyPart != 0 ? (PlayerDamageReceiver.BodyPart)bodyPart : PlayerDamageReceiver.BodyPart.Chest;
            PlayerDamageReceiver receiver = ReceiverFor(part);
            if (receiver == null) {
                if (!warnedNoReceiver) {
                    warnedNoReceiver = true;
                    EntangleLogger.Warn("[Attack] Your body has no damage receiver to take an NPC's hit; taking it as plain damage.");
                }
                PlayerScripts.playerHealth.TAKEDAMAGE(damage);
                return;
            }

            Attack attack = new Attack();
            attack.damage = damage;
            attack.attackType = type;
            Transform at = receiver.transform;
            attack.origin = at.position;
            attack.normal = -at.forward;
            try { attack.collider = receiver.GetComponent<Collider>(); }
            catch { }
            receiver.ReceiveAttack(attack);
        }

        static PlayerDamageReceiver ReceiverFor(PlayerDamageReceiver.BodyPart part) {
            StressLevelZero.Rig.RigManager rig = PlayerScripts.playerRig;
            if (!rig)
                return null;
            if (receivers == null || receiversOf != rig) {
                try { receivers = rig.GetComponentsInChildren<PlayerDamageReceiver>(true); }
                catch { receivers = null; }
                receiversOf = rig;
            }
            PlayerDamageReceiver chest = null;
            for (int i = 0; receivers != null && i < receivers.Length; i++) {
                PlayerDamageReceiver receiver = receivers[i];
                if (!receiver)
                    continue;
                if (receiver.bodyPart == part)
                    return receiver;
                if (chest == null && receiver.bodyPart == PlayerDamageReceiver.BodyPart.Chest)
                    chest = receiver;
            }
            return chest;
        }
    }
}
