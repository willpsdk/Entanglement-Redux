using System.Collections.Generic;

using UnityEngine;

using Entanglement.Network;

namespace Entanglement.Objects
{
    public static class NpcPending
    {
        const float MaxBuildSeconds = 15f;

        class Build {
            public int count;
            public float since;
        }

        class Held {
            public bool hasOwner;
            public long owner;
            public long ownerSender;
            public bool fighting;
            public ushort ownerSeq;
            public bool ownerProposal;

            public bool hasState;
            public float stateHealth;
            public byte mental;
            public byte loco;

            public bool hasHealth;
            public float health;

            public bool dead;
            public bool despawned;
        }

        static readonly Dictionary<ushort, Build> building = new Dictionary<ushort, Build>();
        static readonly Dictionary<ushort, Held> held = new Dictionary<ushort, Held>();

        static readonly HashSet<ushort> creating = new HashSet<ushort>();

        static int heldCount;
        static int appliedCount;
        static int droppedCount;

        public static void OnSceneCleanup() {
            building.Clear();
            held.Clear();
            creating.Clear();
        }

        /// <summary>A spawn or bind for this id has arrived. Call when the message is handled, before any frame passes.</summary>
        public static void BeginBuild(ushort id) {
            if (id == 0 || Node.isServer)
                return;

            // Anything held was about the object's previous life.
            held.Remove(id);

            Build build;
            if (!building.TryGetValue(id, out build)) {
                build = new Build();
                building[id] = build;
            }
            build.count++;
            build.since = Time.realtimeSinceStartup;
        }

        public static void EndBuild(ushort id) {
            Build build;
            if (!building.TryGetValue(id, out build))
                return;

            build.count--;
            if (build.count > 0)
                return;

            building.Remove(id);
            Flush(id);
        }

        /// <summary>False when another copy of this id is already being made.</summary>
        public static bool TryBeginCreate(ushort id) => creating.Add(id);

        public static void EndCreate(ushort id) => creating.Remove(id);

        /// <summary>True while messages about this id should wait for its build.</summary>
        public static bool Waiting(ushort id) {
            if (id == 0 || Node.isServer)
                return false;

            Build build;
            if (!building.TryGetValue(id, out build))
                return false;

            if (Time.realtimeSinceStartup - build.since > MaxBuildSeconds) {
                // The build never reported back. Stop holding and use what we have.
                building.Remove(id);
                Flush(id);
                return false;
            }
            return true;
        }

        static Held Get(ushort id) {
            Held entry;
            if (!held.TryGetValue(id, out entry)) {
                entry = new Held();
                held[id] = entry;
            }
            heldCount++;
            return entry;
        }

        public static bool HoldOwner(ushort id, long owner, long sender, bool fighting, ushort seq, bool proposal) {
            if (!Waiting(id))
                return false;
            Held entry = Get(id);
            // Only the newest grant matters.
            if (entry.hasOwner && !entry.ownerProposal && !proposal && (short)(seq - entry.ownerSeq) < 0)
                return true;
            entry.hasOwner = true;
            entry.owner = owner;
            entry.ownerSender = sender;
            entry.fighting = fighting;
            entry.ownerSeq = seq;
            entry.ownerProposal = proposal;
            return true;
        }

        public static bool HoldState(ushort id, float health, byte mental, byte loco) {
            if (!Waiting(id))
                return false;
            Held entry = Get(id);
            entry.hasState = true;
            entry.stateHealth = health;
            entry.mental = mental;
            entry.loco = loco;
            return true;
        }

        public static bool HoldHealth(ushort id, float health) {
            if (!Waiting(id))
                return false;
            Held entry = Get(id);
            entry.hasHealth = true;
            entry.health = health;
            return true;
        }

        public static bool HoldDeath(ushort id) {
            if (!Waiting(id))
                return false;
            Get(id).dead = true;
            return true;
        }

        public static bool HoldDespawn(ushort id) {
            if (!Waiting(id))
                return false;
            Get(id).despawned = true;
            return true;
        }

        static void Flush(ushort id) {
            Held entry;
            if (!held.TryGetValue(id, out entry))
                return;
            held.Remove(id);

            PooleeSyncable poolee;
            if (!PooleeSyncable._PooleeLookup.TryGetValue(id, out poolee) || !ObjectSync.Live(poolee)) {
                droppedCount++;
                NpcTrace.Log("held messages for #" + id + " dropped, it was never built here");
                return;
            }

            if (entry.dead) {
                try { SceneEventSync.ApplyNpcEvent(SceneEventType.NpcDeath, id, null); }
                catch (System.Exception e) { EntangleLogger.Debug("[NpcPending] death for #" + id + ": " + e.Message); }
            }
            else {
                if (entry.hasState) {
                    try { NpcStateSync.ApplyRemote(id, entry.stateHealth, entry.mental, entry.loco); }
                    catch (System.Exception e) { EntangleLogger.Debug("[NpcPending] state for #" + id + ": " + e.Message); }
                }
                if (entry.hasHealth) {
                    try { poolee.ApplyHealth(entry.health); }
                    catch (System.Exception e) { EntangleLogger.Debug("[NpcPending] health for #" + id + ": " + e.Message); }
                }
            }

            if (entry.hasOwner) {
                try { PooleeSyncable.ApplyOwnerMessage(id, entry.owner, entry.ownerSender, entry.fighting, entry.ownerSeq, entry.ownerProposal); }
                catch (System.Exception e) { EntangleLogger.Debug("[NpcPending] owner for #" + id + ": " + e.Message); }
            }

            if (entry.despawned) {
                try { SceneEventSync.ApplyNpcEvent(SceneEventType.NpcDespawn, id, null); }
                catch (System.Exception e) { EntangleLogger.Debug("[NpcPending] despawn for #" + id + ": " + e.Message); }
            }

            appliedCount++;
            NpcTrace.Log("applied held messages to #" + id + ":"
                + (entry.dead ? " death" : "")
                + (entry.hasState ? " state" : "")
                + (entry.hasHealth ? " health" : "")
                + (entry.hasOwner ? " owner=" + entry.owner : "")
                + (entry.despawned ? " despawn" : ""));
        }

        /// <summary>For the state log: messages held, NPCs they were applied to, and ones dropped.</summary>
        public static string Summary() => "held " + heldCount + ", applied to " + appliedCount + " NPC(s), dropped " + droppedCount;
    }
}
