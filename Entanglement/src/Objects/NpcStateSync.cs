using System;
using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.AI;
using PuppetMasta;

using Entanglement.Network;

namespace Entanglement.Objects
{
    public static class NpcStateSync
    {
        // Guards against the replayed values re-broadcasting or tripping damage patches.
        public static bool isApplying;

        /// <summary>
        /// Dead by its brain or its puppet. A puppet has three states (PuppetMaster.State): Alive,
        /// Dead and Frozen. Resting NPCs are spawned Frozen (BaseEnemyConfig.puppetState,
        /// freezeWhileResting) and stand there alive; only Dead counts here. Treating "not alive" as
        /// dead made the corpse scan report every resting NPC dead a second after it spawned, so
        /// every client's copy was killed on arrival (the "spawned ragdolled" NPCs).
        /// </summary>
        public static bool IsDead(AIBrain brain) {
            try {
                if (!brain)
                    return false;
                if (brain.isDead)
                    return true;
                PuppetMaster puppet = brain.puppetMaster;
                return puppet && puppet.isDead;
            }
            catch { return false; }
        }

        /// <summary>Alive but frozen: a resting NPC the game has switched off until something comes near.</summary>
        public static bool IsFrozen(AIBrain brain) {
            try {
                if (!brain || brain.isDead)
                    return false;
                PuppetMaster puppet = brain.puppetMaster;
                return puppet && puppet.isFrozen;
            }
            catch { return false; }
        }

        /// <summary>Host side: one state message per living synced NPC.</summary>
        public static int ReplayTo(long userId) {
            if (!SteamIntegration.isHost || Node.activeNode == null)
                return 0;

            int sent = 0;

            foreach (PooleeSyncable poolee in new List<PooleeSyncable>(PooleeSyncable._PooleeLookup.Values)) {
                if (poolee == null || !poolee.gameObject || !poolee.gameObject.activeInHierarchy)
                    continue;

                AIBrain brain;
                try { brain = poolee.GetComponentInChildren<AIBrain>(true); }
                catch { continue; }

                if (brain == null)
                    continue;

                bool dead = IsDead(brain);
                if (dead) {
                    if (SendDeath(userId, poolee.id))
                        sent++;
                    continue;
                }

                float hp;
                byte mental, loco;
                if (!TryReadState(brain, out hp, out mental, out loco))
                    continue;

                NpcStateMessageData data = new NpcStateMessageData {
                    spawnId = poolee.id,
                    health = hp,
                    mentalState = mental,
                    locoState = loco,
                };

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.NpcState, data);
                if (message == null)
                    continue;

                Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
                sent++;
            }

            return sent;
        }

        /// <summary>Host: this NPC is already dead (a scene corpse). Tell everyone, so clients do not stand it back up.</summary>
        public static void PublishDeath(GameObject go, ushort id) {
            if (!SteamIntegration.isHost || go == null || id == 0 || !SteamIntegration.hasLobby)
                return;
            AIBrain brain;
            try { brain = go.GetComponentInChildren<AIBrain>(true); }
            catch { return; }
            if (!IsDead(brain))
                return;

            PooleeSyncable poolee = PooleeSyncable._Cache.Get(go);
            if (poolee != null)
                poolee.NoteDeclaredDeath();

            SceneEventSync.SendEvent(SceneEventType.NpcDeath, go.transform, id);
            NpcTrace.Log("placed or spawned dead, told everyone: " + go.name + " #" + id);
        }

        internal static bool SendDeath(long userId, ushort spawnId) {
            if (spawnId == 0)
                return false;

            SceneEventMessageData data = new SceneEventMessageData {
                eventType = SceneEventType.NpcDeath,
                arg = spawnId,
                objectPath = "#" + spawnId,
                replay = true,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, data);
            if (message == null)
                return false;

            Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
            return true;
        }

        static bool TryReadState(AIBrain brain, out float hp, out byte mental, out byte loco) {
            hp = 0f;
            mental = 0;
            loco = 0;

            try {
                BehaviourBaseNav behaviour = brain.behaviour;
                if (behaviour == null)
                    return false;

                SubBehaviourHealth health = behaviour.health;
                if (health == null)
                    return false;

                hp = health.cur_hp;
                mental = (byte)behaviour.mentalState;
                loco = (byte)behaviour.locoState;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Client side: apply a replayed NPC's condition.</summary>
        public static void ApplyRemote(ushort spawnId, float health, byte mentalState, byte locoState) {
            // The replay usually lands before the joiner has built the NPC; it waits for it.
            if (NpcPending.HoldState(spawnId, health, mentalState, locoState))
                return;

            PooleeSyncable poolee;
            if (!PooleeSyncable._PooleeLookup.TryGetValue(spawnId, out poolee) || poolee == null)
                return;

            AIBrain brain;
            try { brain = poolee.GetComponentInChildren<AIBrain>(true); }
            catch { return; }

            if (brain == null)
                return;

            isApplying = true;
            try {
                BehaviourBaseNav behaviour = brain.behaviour;
                if (behaviour == null)
                    return;

                // Health is plain data - assign it directly.
                try {
                    SubBehaviourHealth sub = behaviour.health;
                    if (sub != null)
                        sub.cur_hp = health;
                }
                catch { }

                try { behaviour.SwitchMentalState((BehaviourBaseNav.MentalState)mentalState); }
                catch (Exception e) { EntangleLogger.Warn("[NpcState] SwitchMentalState: " + e.Message); }

                try {
                    BehaviourBaseNav.LocoState loco = (BehaviourBaseNav.LocoState)locoState;
                    // A just-built copy that is still frozen or has not seen the owner's pose must
                    // not be told it has fallen: that is how late-join NPCs arrived as ragdolls.
                    bool skipFallen = (loco == BehaviourBaseNav.LocoState.Fallen || loco == BehaviourBaseNav.LocoState.GetUp)
                        && (NpcStateSync.IsFrozen(brain) || !poolee.PoseArrived());
                    if (!skipFallen)
                        behaviour.SwitchLocoState(loco, 0f, true);
                }
                catch (Exception e) { EntangleLogger.Warn("[NpcState] SwitchLocoState: " + e.Message); }
            }
            catch (Exception e) {
                EntangleLogger.Warn("[NpcState] Apply failed for " + spawnId + ": " + e.Message);
            }
            finally { isApplying = false; }
        }
    }
}
