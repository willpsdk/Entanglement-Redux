using System;
using System.Collections.Generic;

using PuppetMasta;

using Entanglement.Objects;

namespace Entanglement.Entities
{
    /// <summary>
    /// Fusion NetworkEntity extender for Boneworks NPCs — ownership + PuppetMaster.
    /// Replaces OwnedEntity; PooleeSyncable remains the pose/sync engine.
    /// </summary>
    public sealed class NpcEntity : IEntityExtender
    {
        static readonly Dictionary<ushort, NpcEntity> bySpawnId = new Dictionary<ushort, NpcEntity>();

        public NetworkEntity NetworkEntity { get; private set; }
        public PooleeSyncable Poolee { get; private set; }
        public PuppetMaster PuppetMaster { get; private set; }

        public ushort SpawnId => Poolee != null ? Poolee.id : (ushort)0;
        public ushort EntityId => NetworkEntity != null ? NetworkEntity.Id : SpawnId;
        public long OwnerId => NetworkEntity != null ? NetworkEntity.OwnerId : 0;
        public bool IsOwner => NetworkEntity != null && NetworkEntity.IsOwner;
        public bool RunsLocally => NetworkEntity != null && NetworkEntity.RunsLocally;
        public bool IsRegistered => NetworkEntity != null && NetworkEntity.IsRegistered;

        public static IEnumerable<NpcEntity> All => bySpawnId.Values;
        public static int Count => bySpawnId.Count;

        public static NpcEntity Register(PooleeSyncable poolee) {
            if (poolee == null || poolee.id == 0)
                return null;

            NpcEntity existing;
            if (bySpawnId.TryGetValue(poolee.id, out existing) && existing != null) {
                existing.Bind(poolee);
                return existing;
            }

            var npc = new NpcEntity();
            npc.Bind(poolee);
            bySpawnId[poolee.id] = npc;
            return npc;
        }

        public static void Unregister(PooleeSyncable poolee) {
            if (poolee == null || poolee.id == 0)
                return;
            NpcEntity npc;
            if (!bySpawnId.TryGetValue(poolee.id, out npc))
                return;
            bySpawnId.Remove(poolee.id);
            if (npc.NetworkEntity != null) {
                try { npc.NetworkEntity.OnOwnershipTransfer -= npc.OnOwnerChanged; }
                catch { }
                npc.NetworkEntity.DisconnectExtender(npc);
                NetworkEntityManager.Unregister(npc.NetworkEntity);
                npc.NetworkEntity = null;
            }
            npc.Poolee = null;
            npc.PuppetMaster = null;
        }

        public static bool TryGet(ushort spawnId, out NpcEntity npc) => bySpawnId.TryGetValue(spawnId, out npc);

        public static bool TryGet(PooleeSyncable poolee, out NpcEntity npc) {
            if (poolee != null && poolee.id != 0)
                return bySpawnId.TryGetValue(poolee.id, out npc);
            npc = null;
            return false;
        }

        public static NpcEntity Of(PooleeSyncable poolee) {
            NpcEntity npc;
            return TryGet(poolee, out npc) ? npc : Register(poolee);
        }

        public static NpcEntity OfTransform(UnityEngine.Transform t) {
            if (!t)
                return null;
            PooleeSyncable poolee = PooleeSyncable.SyncedNpcOf(t);
            return poolee != null ? Of(poolee) : null;
        }

        public static void Clear() {
            List<ushort> ids = new List<ushort>(bySpawnId.Keys);
            for (int i = 0; i < ids.Count; i++) {
                NpcEntity npc;
                if (!bySpawnId.TryGetValue(ids[i], out npc) || npc == null || npc.Poolee == null)
                    continue;
                Unregister(npc.Poolee);
            }
            bySpawnId.Clear();
        }

        void Bind(PooleeSyncable poolee) {
            Poolee = poolee;
            if (NetworkEntity == null || NetworkEntity.Id != poolee.id) {
                if (NetworkEntity != null) {
                    try { NetworkEntity.OnOwnershipTransfer -= OnOwnerChanged; }
                    catch { }
                    NetworkEntity.DisconnectExtender(this);
                }
                NetworkEntity = NetworkEntityManager.Register(poolee.id);
                NetworkEntity.ConnectExtender(this);
                NetworkEntity.OnOwnershipTransfer += OnOwnerChanged;
            }
            SyncOwnerFromPoolee();
            RefreshPuppet();
        }

        void OnOwnerChanged(NetworkEntity entity, long ownerId) {
            if (Poolee != null && Poolee.CurrentOwner() != ownerId)
                Poolee.SetOwner(ownerId);
            RefreshPuppet();
        }

        public void SyncOwnerFromPoolee() {
            if (NetworkEntity == null || Poolee == null)
                return;
            long owner = Poolee.CurrentOwner();
            if (NetworkEntity.OwnerId != owner)
                NetworkEntity.SetOwner(owner);
        }

        public void RefreshPuppet() {
            PuppetMaster = null;
            if (Poolee == null)
                return;
            try { PuppetMaster = Poolee.GetComponentInChildren<PuppetMaster>(true); }
            catch { }
        }

        public void SetOwner(long ownerId) {
            NetworkEntity?.SetOwner(ownerId);
            Poolee?.SetOwner(ownerId);
            RefreshPuppet();
        }

        public void ClaimFight() => Poolee?.ClaimFight();

        public void TakeOwnership(long newOwner, bool fighting = false) {
            if (Poolee == null)
                return;
            try {
                if (Network.Node.isServer) {
                    SetOwner(newOwner);
                    if (fighting)
                        ClaimFight();
                }
                else if (fighting) {
                    ClaimFight();
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn("[NpcEntity] TakeOwnership: " + e.Message);
            }
        }

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
                    return PuppetMaster != null
                        && PuppetMaster.state == PuppetMaster.State.Alive
                        && Poolee != null
                        && !Poolee.DeclaredDead;
                }
                catch {
                    return false;
                }
            }
        }
    }
}
