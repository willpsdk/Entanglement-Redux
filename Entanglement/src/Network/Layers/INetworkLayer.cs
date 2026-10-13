namespace Entanglement.Network.Layers
{
    /// <summary>
    /// Transport abstraction: Steam (Boneworks) or SteamVR (Meta Horizon Link AppID 250820).
    /// </summary>
    public interface INetworkLayer
    {
        string Title { get; }
        string Platform { get; }

        bool IsAvailable { get; }
        bool IsLoggedIn { get; }
        bool HasLobby { get; }
        bool IsHost { get; }
        bool IsClient { get; }

        long LocalUserId { get; }
        string LocalUserName { get; }
        long LobbyOwnerId { get; }

        void Initialize();
        void Tick();
        void Shutdown();

        bool Send(long userId, NetworkChannel channel, byte[] data);
        bool TryReceive(NetworkChannel channel, out long senderId, out byte[] data);

        bool IsPacketAvailable(NetworkChannel channel, out uint size);
        void CloseSession(long userId);
    }
}
