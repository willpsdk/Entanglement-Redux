using System;
using System.Collections.Generic;

using Entanglement.Objects;

using UnityEngine;

namespace Entanglement.Entities
{
    /// <summary>
    /// Fusion NetworkProp for Boneworks: NetworkEntity extender over a <see cref="TransformSyncable"/>.
    /// Ownership and run-locality go through the entity; pose sync stays on TransformSyncable.
    /// </summary>
    public sealed class NetworkProp : IEntityExtender
    {
        static readonly Dictionary<ushort, NetworkProp> byObjectId = new Dictionary<ushort, NetworkProp>();

        public NetworkEntity NetworkEntity { get; private set; }
        public TransformSyncable Syncable { get; private set; }

        public ushort ObjectId => Syncable != null ? Syncable.objectId : (ushort)0;
        public long OwnerId => NetworkEntity != null ? NetworkEntity.OwnerId : 0;
        public bool IsOwner => NetworkEntity != null && NetworkEntity.IsOwner;
        public bool RunsLocally => NetworkEntity != null && NetworkEntity.RunsLocally;

        public static IEnumerable<NetworkProp> All => byObjectId.Values;
        public static int Count => byObjectId.Count;

        public static NetworkProp Register(TransformSyncable sync) {
            if (sync == null || sync.objectId == 0)
                return null;

            // NPC bones are owned by NpcEntity — don't double-register.
            if (sync.npcRoot != null || sync.aiPart)
                return null;

            NetworkProp existing;
            if (byObjectId.TryGetValue(sync.objectId, out existing) && existing != null) {
                existing.Bind(sync);
                return existing;
            }

            var prop = new NetworkProp();
            prop.Bind(sync);
            byObjectId[sync.objectId] = prop;
            DebugLog.Entity("NetworkProp #" + sync.objectId + " owner=" + prop.OwnerId);
            return prop;
        }

        public static void Unregister(TransformSyncable sync) {
            if (sync == null || sync.objectId == 0)
                return;
            NetworkProp prop;
            if (!byObjectId.TryGetValue(sync.objectId, out prop))
                return;
            byObjectId.Remove(sync.objectId);
            prop.Teardown();
        }

        public static void Unregister(ushort objectId) {
            NetworkProp prop;
            if (!byObjectId.TryGetValue(objectId, out prop))
                return;
            byObjectId.Remove(objectId);
            prop.Teardown();
        }

        public static bool TryGet(ushort objectId, out NetworkProp prop) => byObjectId.TryGetValue(objectId, out prop);

        public static NetworkProp Of(TransformSyncable sync) {
            if (sync == null)
                return null;
            NetworkProp prop;
            return TryGet(sync.objectId, out prop) ? prop : Register(sync);
        }

        public static void Clear() {
            List<ushort> ids = new List<ushort>(byObjectId.Keys);
            for (int i = 0; i < ids.Count; i++)
                Unregister(ids[i]);
            byObjectId.Clear();
        }

        void Bind(TransformSyncable sync) {
            Syncable = sync;
            if (NetworkEntity == null || NetworkEntity.Id != sync.objectId) {
                if (NetworkEntity != null) {
                    try { NetworkEntity.OnOwnershipTransfer -= OnOwnerChanged; }
                    catch { }
                    NetworkEntity.DisconnectExtender(this);
                }
                NetworkEntity = NetworkEntityManager.Register(sync.objectId);
                NetworkEntity.ConnectExtender(this);
                NetworkEntity.OnOwnershipTransfer += OnOwnerChanged;
                NetworkEntity.HookOnRegistered(_ => SyncOwnerFromSyncable());
            }
            SyncOwnerFromSyncable();
        }

        void Teardown() {
            if (NetworkEntity != null) {
                try { NetworkEntity.OnOwnershipTransfer -= OnOwnerChanged; }
                catch { }
                NetworkEntity.DisconnectExtender(this);
                NetworkEntityManager.Unregister(NetworkEntity);
                NetworkEntity = null;
            }
            Syncable = null;
        }

        void OnOwnerChanged(NetworkEntity entity, long ownerId) {
            if (Syncable != null && Syncable.staleOwner != ownerId)
                Syncable.ForceOwner(ownerId, false);
        }

        public void SyncOwnerFromSyncable() {
            if (NetworkEntity == null || Syncable == null)
                return;
            long owner = Syncable.staleOwner;
            if (NetworkEntity.OwnerId != owner)
                NetworkEntity.SetOwner(owner);
        }

        public void SetOwner(long ownerId) {
            NetworkEntity?.SetOwner(ownerId);
            if (Syncable != null)
                Syncable.ForceOwner(ownerId, false);
        }
    }
}
