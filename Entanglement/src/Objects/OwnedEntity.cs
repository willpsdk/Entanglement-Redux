using System;
using System.Collections.Generic;

using PuppetMasta;

namespace Entanglement.Objects
{
    /// <summary>
    /// Fusion NetworkEntity for Boneworks NPCs. Ownership, PuppetMaster, and run-locality
    /// go through here — PooleeSyncable is the sync engine underneath.
    /// </summary>
    public sealed class OwnedEntity
    {
        static readonly Dictionary<ushort, OwnedEntity> byId = new Dictionary<ushort, OwnedEntity>();

        public ushort EntityId { get; private set; }
        public PooleeSyncable Poolee { get; private set; }
        public PuppetMaster PuppetMaster { get; private set; }

        public long OwnerId => Poolee != null ? Poolee.CurrentOwner() : 0L;

        public bool IsOwner {
            get {
                long owner = OwnerId;
                return owner != 0L && owner == Network.SteamIntegration.currentUserId;
            }
        }

        public bool IsRegistered => Poolee != null && EntityId != 0;

        /// <summary>Owner (or solo) simulates AI + PuppetMaster; others follow pose.</summary>
        public bool RunsLocally => Poolee != null && Poolee.RunsHere;

        public static IEnumerable<OwnedEntity> All => byId.Values;

        public static int Count => byId.Count;

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

        /// <summary>Lookup by any transform under the NPC (combat / grips).</summary>
        public static OwnedEntity OfTransform(UnityEngine.Transform t) {
            if (!t)
                return null;
            PooleeSyncable poolee = PooleeSyncable.SyncedNpcOf(t);
            return poolee != null ? Of(poolee) : null;
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

        public void SetOwner(long newOwner) {
            if (Poolee == null)
                return;
            Poolee.SetOwner(newOwner);
            RefreshPuppet();
        }

        public void ClaimFight() {
            Poolee?.ClaimFight();
        }

        public void TakeOwnership(long newOwner, bool fighting = false) {
            if (Poolee == null)
                return;
            try {
                if (Network.Node.isServer) {
                    SetOwner(newOwner);
                    if (fighting)
                        ClaimFight();
                }
                else if (fighting)
                    ClaimFight();
            }
            catch (Exception e) {
                EntangleLogger.Warn("[OwnedEntity] TakeOwnership: " + e.Message);
            }
        }

        /// <summary>PuppetMaster state for debug / gamemodes.</summary>
        public PuppetMaster.State PuppetState {
            get {
                try {
                    return PuppetMaster != null ? PuppetMaster.state : PuppetMaster.State.Dead;
                }
                catch {
                    return PuppetMaster.State.Dead;
                }
            }
        }

        public bool IsAlive {
            get {
                try {
                    return PuppetMaster != null && PuppetMaster.state == PuppetMaster.State.Alive
                        && Poolee != null && !Poolee.DeclaredDead;
                }
                catch {
                    return false;
                }
            }
        }
    }
}
