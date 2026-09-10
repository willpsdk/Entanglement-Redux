using System;
using System.Collections.Generic;

using UnityEngine;

using Entanglement.Network;

using MelonLoader;

namespace Entanglement.Objects
{
    [RegisterTypeInIl2Cpp]
    public abstract class Syncable : MonoBehaviour {
        public Syncable(IntPtr intPtr) : base(intPtr) { }

        public List<long> ownerQueue = new List<long>();
        public long staleOwner = 0;
        public long lastOwner = 0;

        public ushort objectId = 0;
        public bool isValid = false;

        public virtual void RemoveFromQueue(ushort id) {
            if (isValid) return; // We don't want to replace valid syncables, this could cause the exact problem its trying to solve

            objectId = id;
            isValid = true;
            ObjectSync.RegisterSyncable(this, id);

#if DEBUG
            EntangleLogger.Log($"Recieved ID from host of {id} on object {gameObject.name}!");
#endif
        }

        public virtual bool ShouldSync() => true;

        public abstract void SyncUpdate();

        protected virtual void FixedUpdate() {
            if (!isValid) return;

            if (IsOwner() && ShouldSync())
                SyncUpdate();
        }

        protected abstract void UpdateOwner(bool checkForMag = true);

        public virtual void EnqueueOwner(long owner) {
            if (!ownerQueue.Contains(owner)) ownerQueue.Add(owner);
            UpdateStale();
            UpdateOwner();
        }

        public virtual void DequeueOwner(long owner) {
            if (ownerQueue.Contains(owner)) ownerQueue.Remove(owner);
            UpdateStale();
            UpdateOwner();
        }

        public virtual void ClearOwner() {
            ownerQueue.Clear();
            lastOwner = staleOwner;
            staleOwner = 0;
        }

        public virtual void TrySetStale(long owner) {
            // Collision / pool steals must not override an active held lock
            if (!CanStealOwnership(owner))
                return;

            lastOwner = staleOwner;
            if (ownerQueue.Count == 0) staleOwner = owner;
            else EnqueueOwner(owner);
            UpdateOwner();
        }

        public virtual void ForceOwner(long owner, bool checkForMag = true) {
            lastOwner = staleOwner;
            ownerQueue.Clear();
            staleOwner = owner;
            UpdateOwner(checkForMag);
        }

        public virtual void SendEnqueue() { }

        public virtual void SendDequeue() { }

        public virtual void Cleanup() => GameObject.Destroy(this);

        public void UpdateStale() {
            lastOwner = staleOwner;
            if (ownerQueue.Count > 0) staleOwner = ownerQueue[0]; 
        }

        public bool IsOwner() {
            return staleOwner == SteamIntegration.currentUserId;
        }

        /// <summary>
        /// While someone is in the owner queue (holding / transferring), treat ownership as locked
        /// so punches and collision steals cannot yank the object out from under them.
        /// </summary>
        public bool IsOwnershipLocked => ownerQueue.Count > 0;

        public bool CanStealOwnership(long requester) {
            if (!IsOwnershipLocked)
                return true;
            // The current holder and anyone already queued may continue the handoff
            if (requester == staleOwner || ownerQueue.Contains(requester))
                return true;
            return false;
        }

        public virtual bool TryForceOwner(long owner, bool checkForMag = true) {
            if (!CanStealOwnership(owner))
                return false;
            ForceOwner(owner, checkForMag);
            return true;
        }
    }
}
