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

        // When staleOwner last changed, for anything that shouldn't take it straight back.
        public float ownerChangedAt = -100f;

        void NoteOwnerChange(long was) {
            if (was != staleOwner)
                ownerChangedAt = Time.time;
        }

        public ushort objectId = 0;
        public bool isValid = false;

        // Set on NPC and turret bodies, so every owner change on them lands in the log.
        public bool traceOwner;

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
            long was = staleOwner;
            ownerQueue.Clear();
            lastOwner = staleOwner;
            staleOwner = 0;
            NoteOwnerChange(was);
            if (traceOwner) NpcTrace.Owner(this, was, staleOwner, "ClearOwner");
        }

        public virtual void TrySetStale(long owner) {
            long was = staleOwner;
            lastOwner = staleOwner;
            if (ownerQueue.Count == 0) staleOwner = owner;
            else EnqueueOwner(owner);
            NoteOwnerChange(was);
            if (traceOwner) NpcTrace.Owner(this, was, staleOwner, "TrySetStale");
            UpdateOwner();
        }

        public virtual void ForceOwner(long owner, bool checkForMag = true) {
            long was = staleOwner;
            lastOwner = staleOwner;
            ownerQueue.Clear();
            staleOwner = owner;
            NoteOwnerChange(was);
            if (traceOwner) NpcTrace.Owner(this, was, staleOwner, "ForceOwner");
            UpdateOwner(checkForMag);
        }

        public virtual void SendEnqueue() { }

        public virtual void SendDequeue() { }

        public virtual void Cleanup() => GameObject.Destroy(this);

        public void UpdateStale() {
            long was = staleOwner;
            lastOwner = staleOwner;
            if (ownerQueue.Count > 0) staleOwner = ownerQueue[0];
            NoteOwnerChange(was);
            if (traceOwner) NpcTrace.Owner(this, was, staleOwner, "UpdateStale");
        }

        public bool IsOwner() {
            return staleOwner == SteamIntegration.currentUserId;
        }
    }
}
