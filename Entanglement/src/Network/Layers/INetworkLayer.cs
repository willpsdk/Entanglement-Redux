using System;

namespace Entanglement.Network.Layers
{
    /// <summary>
    /// Fusion-style transport abstraction.
    /// Steam (Boneworks), SteamVR (Meta Horizon Link AppID 250820), Proxy SteamVR (Quest → PC helper).
    /// </summary>
    public interface INetworkLayer
    {
        /// <summary>Human-readable name, e.g. "Steam", "Meta", "Proxy".</summary>
        string Title { get; }

        /// <summary>Platform id for bans / identity, e.g. "Steam", "Meta".</summary>
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
        /// <summary>Drain available packets. Returns false when no more this channel.</summary>
        bool TryReceive(NetworkChannel channel, out long senderId, out byte[] data);

        bool IsPacketAvailable(NetworkChannel channel, out uint size);
        void CloseSession(long userId);
    }
}
