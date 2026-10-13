using System;
using System.Collections.Generic;

using UnityEngine;

using PuppetMasta;

namespace Entanglement.Objects
{
    /// <summary>
    /// Fusion NetworkEntity ideology for Boneworks NPCs: one owned entity per synced Poolee,
    /// with explicit owner id and PuppetMaster handle. PooleeSyncable remains the sync engine;
    /// this is the ownership/puppet façade gameplay and combat talk to.
    /// </summary>
    public sealed class OwnedEntity
    {
        static readonly Dictionary<ushort, OwnedEntity> byId = new Dictionary<ushort, OwnedEntity>();

        public ushort EntityId { get; private set; }
        public PooleeSyncable Poolee { get; private set; }
        public PuppetMaster PuppetMaster { get; private set; }
        public long OwnerId => Poolee != null ? Poolee.CurrentOwner() : 0L;
        public bool IsOwner => OwnerId != 0L && OwnerId == Network.SteamIntegration.currentUserId;
        public bool IsRegistered => Poolee != null && EntityId != 0;

        public static IEnumerable<OwnedEntity> All => byId.Values;

        public static void Clear() => byId.Clear();

        public static OwnedEntity Register(PooleeSyncable poolee) {
            if (poolee == null || poolee.id == 0)
                return null;

            OwnedEntity existing;
            if (byId.TryGetValue(poolee.id, out existing) && existing != null) {
                existing.Bind(poolee);
                return existing;
            }

            var entity = new OwnedEntity();
            entity.Bind(poolee);
            byId[poolee.id] = entity;
            return entity;
        }

        public static void Unregister(PooleeSyncable poolee) {
            if (poolee == null || poolee.id == 0)
                return;
            byId.Remove(poolee.id);
        }

        public static bool TryGet(ushort id, out OwnedEntity entity) => byId.TryGetValue(id, out entity);

        public static bool TryGet(PooleeSyncable poolee, out OwnedEntity entity) {
            if (poolee != null && poolee.id != 0)
                return byId.TryGetValue(poolee.id, out entity);
            entity = null;
            return false;
        }

        public static OwnedEntity Of(PooleeSyncable poolee) {
            OwnedEntity entity;
            return TryGet(poolee, out entity) ? entity : Register(poolee);
        }

        void Bind(PooleeSyncable poolee) {
            Poolee = poolee;
            EntityId = poolee.id;
            RefreshPuppet();
        }

        public void RefreshPuppet() {
            PuppetMaster = null;
            if (Poolee == null)
                return;
            try {
                PuppetMaster = Poolee.GetComponentInChildren<PuppetMaster>(true);
            }
            catch { }
        }

        /// <summary>Owner machine simulates AI + PuppetMaster muscles; others follow pose.</summary>
        public bool RunsLocally => Poolee != null && Poolee.RunsHere;

        public void TakeOwnership(long newOwner, bool fighting = false) {
            if (Poolee == null)
                return;
            try {
                if (Network.Node.isServer) {
                    Poolee.SetOwner(newOwner);
                    // Host rebroadcasts via existing NpcOwner path when fighting/claiming.
                    if (fighting)
                        Poolee.ClaimFight();
                }
                else if (fighting)
                    Poolee.ClaimFight();
            }
            catch (Exception e) {
                EntangleLogger.Warn("[OwnedEntity] TakeOwnership: " + e.Message);
            }
        }
    }
}
