namespace Entanglement.Entities
{
    public interface INetworkOwnable
    {
        long OwnerId { get; }
        bool IsOwner { get; }
        bool HasOwner { get; }
        bool IsOwnerLocked { get; }

        void SetOwner(long ownerId);
        void LockOwner();
        void UnlockOwner();
        void RemoveOwner();
    }
}
