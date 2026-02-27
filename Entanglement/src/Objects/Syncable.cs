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

        // CHANGED: long -> ulong for Steam IDs
        public List<ulong> ownerQueue = new List<ulong>();
        public ulong staleOwner = 0;
        public ulong lastOwner = 0;

        // Ownership conflict detection
        private float ownershipTimeoutTimer = 0f;
        private const float OWNERSHIP_TIMEOUT = 5f; // Reset ownership if not updated in 5 seconds

        public ushort objectId = 0;
        public bool isValid = false;

        public virtual void RemoveFromQueue(ushort id) {
            if (isValid) return; 

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

        // CHANGED: long -> ulong
        public virtual void EnqueueOwner(ulong owner) {
            if (!ownerQueue.Contains(owner)) ownerQueue.Add(owner);
            UpdateStale();
            ownershipTimeoutTimer = 0f; // Reset timeout on ownership change
            UpdateOwner();
        }

        // CHANGED: long -> ulong
        public virtual void DequeueOwner(ulong owner) {
            if (ownerQueue.Contains(owner)) ownerQueue.Remove(owner);
            UpdateStale();
            ownershipTimeoutTimer = 0f; // Reset timeout on ownership change
            UpdateOwner();
        }

        public virtual void ClearOwner() {
            ownerQueue.Clear();
            lastOwner = staleOwner;
            staleOwner = 0;
        }

        // CHANGED: long -> ulong
        public virtual void TrySetStale(ulong owner) {
            lastOwner = staleOwner;
            if (ownerQueue.Count == 0) staleOwner = owner;
            else EnqueueOwner(owner);
            UpdateOwner();
        }

        // CHANGED: long -> ulong
        public virtual void ForceOwner(ulong owner, bool checkForMag = true) {
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
            // CHANGED: SteamIntegration.currentUser.m_SteamID -> SteamIntegration.currentUser.m_SteamID
            return staleOwner == SteamIntegration.currentUser.m_SteamID;
        }
    }
}