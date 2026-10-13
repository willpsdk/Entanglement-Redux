using System;
using System.Collections.Generic;

namespace Entanglement.Entities
{
    /// <summary>Fusion NetworkEntityManager — register, lookup, cleanup, fixed updates.</summary>
    public static class NetworkEntityManager
    {
        static readonly Dictionary<ushort, NetworkEntity> byId = new Dictionary<ushort, NetworkEntity>();
        static readonly List<IEntityFixedUpdatable> fixedUpdatables = new List<IEntityFixedUpdatable>();

        public static IEnumerable<NetworkEntity> All => byId.Values;
        public static int Count => byId.Count;

        public static NetworkEntity Register(ushort id) {
            if (id == 0)
                return null;

            NetworkEntity existing;
            if (byId.TryGetValue(id, out existing) && existing != null && existing.IsRegistered)
                return existing;

            var entity = new NetworkEntity();
            entity.Register(id);
            byId[id] = entity;
            return entity;
        }

        public static void Unregister(ushort id) {
            NetworkEntity entity;
            if (!byId.TryGetValue(id, out entity))
                return;
            byId.Remove(id);
            try { entity.Unregister(); }
            catch { }
        }

        public static void Unregister(NetworkEntity entity) {
            if (entity == null)
                return;
            Unregister(entity.Id);
        }

        public static bool TryGet(ushort id, out NetworkEntity entity) => byId.TryGetValue(id, out entity);

        public static NetworkEntity Get(ushort id) {
            NetworkEntity entity;
            return byId.TryGetValue(id, out entity) ? entity : null;
        }

        public static void Clear() {
            List<ushort> ids = new List<ushort>(byId.Keys);
            for (int i = 0; i < ids.Count; i++)
                Unregister(ids[i]);
            byId.Clear();
            fixedUpdatables.Clear();
        }

        public static void RegisterFixedUpdatable(IEntityFixedUpdatable updatable) {
            if (updatable != null && !fixedUpdatables.Contains(updatable))
                fixedUpdatables.Add(updatable);
        }

        public static void UnregisterFixedUpdatable(IEntityFixedUpdatable updatable) {
            fixedUpdatables.Remove(updatable);
        }

        public static void OnFixedUpdate() {
            for (int i = fixedUpdatables.Count - 1; i >= 0; i--) {
                try { fixedUpdatables[i].OnEntityFixedUpdate(); }
                catch (Exception e) {
                    EntangleLogger.Warn("[NetworkEntity] FixedUpdate: " + e.Message);
                }
            }
        }

        public static void OnPlayerLeft(long userId) {
            foreach (NetworkEntity entity in byId.Values) {
                if (entity.OwnerId == userId)
                    entity.RemoveOwner();
            }
        }
    }

    public interface IEntityFixedUpdatable
    {
        void OnEntityFixedUpdate();
    }
}
