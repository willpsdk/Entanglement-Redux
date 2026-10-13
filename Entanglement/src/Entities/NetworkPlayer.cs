using System;
using System.Collections.Generic;

using Entanglement.Representation;

namespace Entanglement.Entities
{
    /// <summary>
    /// Fusion NetworkPlayer for Boneworks remotes: NetworkEntity + RigPuppet (PhysicsRig PD)
    /// wrapping PlayerRepresentation.
    /// </summary>
    public sealed class NetworkPlayer : IEntityExtender, IEntityFixedUpdatable
    {
        static readonly Dictionary<long, NetworkPlayer> byUserId = new Dictionary<long, NetworkPlayer>();

        public NetworkEntity NetworkEntity { get; private set; }
        public PlayerRepresentation Representation { get; private set; }
        public RigPuppet Puppet { get; private set; }

        public long UserId { get; private set; }
        public bool HasRig => Representation != null && Representation.repRoot;

        public static NetworkPlayer Register(PlayerRepresentation rep) {
            if (rep == null)
                return null;

            NetworkPlayer existing;
            if (byUserId.TryGetValue(rep.playerId, out existing) && existing != null) {
                existing.Bind(rep);
                return existing;
            }

            var player = new NetworkPlayer();
            player.Bind(rep);
            byUserId[rep.playerId] = player;
            NetworkEntityManager.RegisterFixedUpdatable(player);
            return player;
        }

        public static void Unregister(long userId) {
            NetworkPlayer player;
            if (!byUserId.TryGetValue(userId, out player))
                return;
            byUserId.Remove(userId);
            NetworkEntityManager.UnregisterFixedUpdatable(player);
            player.Teardown();
        }

        public static void Unregister(PlayerRepresentation rep) {
            if (rep != null)
                Unregister(rep.playerId);
        }

        public static bool TryGet(long userId, out NetworkPlayer player) => byUserId.TryGetValue(userId, out player);

        public static bool IsRegistered(long userId) => byUserId.ContainsKey(userId);

        public static void Clear() {
            List<long> ids = new List<long>(byUserId.Keys);
            for (int i = 0; i < ids.Count; i++)
                Unregister(ids[i]);
            byUserId.Clear();
        }

        void Bind(PlayerRepresentation rep) {
            Representation = rep;
            UserId = rep.playerId;

            // Player entities use a high id band so they don't collide with spawn ids.
            ushort entityId = PlayerEntityId(UserId);
            if (NetworkEntity == null || NetworkEntity.Id != entityId) {
                if (NetworkEntity != null)
                    NetworkEntity.DisconnectExtender(this);
                NetworkEntity = NetworkEntityManager.Register(entityId);
                NetworkEntity.ConnectExtender(this);
                NetworkEntity.SetOwner(UserId);
                NetworkEntity.LockOwner();
            }

            if (Puppet == null)
                Puppet = new RigPuppet();
            Puppet.Attach(rep);
            Puppet.CreatePuppet();
        }

        void Teardown() {
            if (Puppet != null) {
                Puppet.DestroyPuppet();
                Puppet.Detach();
                Puppet = null;
            }
            if (NetworkEntity != null) {
                NetworkEntity.UnlockOwner();
                NetworkEntity.DisconnectExtender(this);
                NetworkEntityManager.Unregister(NetworkEntity);
                NetworkEntity = null;
            }
            Representation = null;
        }

        public void OnEntityFixedUpdate() {
            if (Puppet != null && HasRig)
                Puppet.OnFixedUpdate();
        }

        static ushort PlayerEntityId(long userId) {
            // Stable high-band id from steam id low bits; avoid 0.
            uint low = (uint)(userId & 0x7FFF);
            if (low == 0)
                low = 1;
            return (ushort)(0x8000 | (low & 0x7FFF));
        }
    }
}
