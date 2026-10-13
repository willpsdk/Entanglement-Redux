using System;
using System.Collections.Generic;

namespace Entanglement.Entities
{
    /// <summary>
    /// Fusion NetworkEntity for Boneworks: synced id, ownership, and extender hooks.
    /// </summary>
    public sealed class NetworkEntity : INetworkOwnable
    {
        public ushort Id { get; private set; }
        public bool IsRegistered { get; private set; }
        public bool IsDestroyed { get; private set; }

        long _ownerId;
        bool _ownerLocked;

        public long OwnerId => _ownerId;
        public bool HasOwner => _ownerId != 0;
        public bool IsOwner => HasOwner && _ownerId == Network.SteamIntegration.currentUserId;
        public bool IsOwnerLocked => _ownerLocked;

        /// <summary>Who simulates this entity (owner, or anyone when unowned/solo).</summary>
        public bool RunsLocally {
            get {
                if (Representation.PlayerRepresentation.reps.Count == 0)
                    return true;
                return !HasOwner || IsOwner;
            }
        }

        public event Action<NetworkEntity, long> OnOwnershipTransfer;
        public event Action<NetworkEntity> OnUnregistered;

        readonly HashSet<IEntityExtender> _extenders = new HashSet<IEntityExtender>();

        internal void Register(ushort id) {
            Id = id;
            IsRegistered = true;
            IsDestroyed = false;
        }

        internal void Unregister() {
            if (!IsRegistered)
                return;
            IsRegistered = false;
            IsDestroyed = true;
            try { OnUnregistered?.Invoke(this); }
            catch { }
            _extenders.Clear();
            Id = 0;
            _ownerId = 0;
            _ownerLocked = false;
        }

        public void ConnectExtender(IEntityExtender extender) {
            if (extender != null)
                _extenders.Add(extender);
        }

        public void DisconnectExtender(IEntityExtender extender) {
            if (extender != null)
                _extenders.Remove(extender);
        }

        public T GetExtender<T>() where T : class, IEntityExtender {
            foreach (IEntityExtender e in _extenders) {
                T typed = e as T;
                if (typed != null)
                    return typed;
            }
            return null;
        }

        public void SetOwner(long ownerId) {
            if (_ownerLocked && ownerId != _ownerId && _ownerId != 0)
                return;
            if (_ownerId == ownerId)
                return;
            _ownerId = ownerId;
            try { OnOwnershipTransfer?.Invoke(this, ownerId); }
            catch (Exception e) { EntangleLogger.Warn("[NetworkEntity] OnOwnershipTransfer: " + e.Message); }
        }

        public void LockOwner() => _ownerLocked = true;
        public void UnlockOwner() => _ownerLocked = false;

        public void RemoveOwner() {
            if (_ownerLocked)
                return;
            SetOwner(0);
        }
    }
}
