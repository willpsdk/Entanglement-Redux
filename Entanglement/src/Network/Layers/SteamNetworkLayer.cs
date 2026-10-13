using Steamworks;

namespace Entanglement.Network.Layers
{
    /// <summary>Native Boneworks Steam transport (game AppID via Steam launch).</summary>
    public class SteamNetworkLayer : INetworkLayer
    {
        public virtual string Title => "Steam";
        public virtual string Platform => "Steam";

        /// <summary>0 = use the Steam client's game AppID (Boneworks when launched via Steam).</summary>
        public virtual uint ApplicationID => 0;

        public bool IsAvailable => !SteamIntegration.isInvalid;
        public bool IsLoggedIn => !SteamIntegration.isInvalid && SteamIntegration.currentUserId != 0;
        public bool HasLobby => SteamIntegration.hasLobby;
        public bool IsHost => SteamIntegration.isHost;
        public bool IsClient => SteamIntegration.hasLobby && !SteamIntegration.isHost;
        public long LocalUserId => SteamIntegration.currentUserId;
        public string LocalUserName => SteamIntegration.currentUserName;
        public long LobbyOwnerId => SteamIntegration.lobbyOwnerId;

        public virtual void Initialize() {
            EntangleLogger.Log("[NetLayer] Active transport: Steam (native Boneworks AppID)");
        }

        public virtual void Tick() {
            // Steam callbacks pumped by SteamIntegration.Tick.
        }

        public virtual void Shutdown() {
            // SteamAPI lifetime owned by SteamIntegration.Shutdown.
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
