using Steamworks;

namespace Entanglement.Network.Layers
{
    /// <summary>Current production transport. Wraps existing SteamIntegration P2P.</summary>
    public sealed class SteamNetworkLayer : INetworkLayer
    {
        public string Title => "Steam";
        public string Platform => "Steam";

        public bool IsAvailable => !SteamIntegration.isInvalid;
        public bool IsLoggedIn => !SteamIntegration.isInvalid && SteamIntegration.currentUserId != 0;
        public bool HasLobby => SteamIntegration.hasLobby;
        public bool IsHost => SteamIntegration.isHost;
        public bool IsClient => SteamIntegration.hasLobby;
        public long LocalUserId => SteamIntegration.currentUserId;
        public string LocalUserName => SteamIntegration.currentUserName;
        public long LobbyOwnerId => SteamIntegration.lobbyOwnerId;

        public void Initialize() {
            // SteamIntegration.Initialize() is still called from Mod for lobby/callbacks.
            EntangleLogger.Log("[NetLayer] Active transport: Steam (Quest/Meta layer can replace this later)");
        }

        public void Tick() {
            // Steam callbacks pumped by SteamIntegration.Tick.
        }

        public void Shutdown() {
            SteamAPI.Shutdown();
        }

        public bool Send(long userId, NetworkChannel channel, byte[] data) {
            return SteamIntegration.SendPacket(userId, channel, data);
        }

        public bool IsPacketAvailable(NetworkChannel channel, out uint size) {
            return SteamNetworking.IsP2PPacketAvailable(out size, (int)channel);
        }

        public bool TryReceive(NetworkChannel channel, out long senderId, out byte[] data) {
            senderId = 0;
            data = null;
            if (!SteamNetworking.IsP2PPacketAvailable(out uint packetSize, (int)channel))
                return false;

            byte[] buffer = new byte[packetSize];
            if (!SteamNetworking.ReadP2PPacket(buffer, packetSize, out uint bytesRead, out CSteamID remoteId, (int)channel))
                return false;

            senderId = (long)remoteId.m_SteamID;
            data = buffer;
            return true;
        }

        public void CloseSession(long userId) {
            SteamIntegration.CloseSession(userId);
        }
    }
}
