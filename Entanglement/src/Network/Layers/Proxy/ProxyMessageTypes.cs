namespace Entanglement.Network.Layers.Proxy
{
    /// <summary>
    /// Wire types for the Quest ↔ PC Steam companion (Fusion Helper–shaped).
    /// Values match Fusion's Proxy MessageTypes where the roles overlap so a
    /// Fusion-style helper can be adapted for Redux P2P.
    /// </summary>
    internal enum ProxyMessageTypes : byte
    {
        SteamID = 0,
        OnDisconnected = 1,
        OnMessage = 2,
        GetUsername = 3,
        UnreliableBroadcastToClients = 4,
        ReliableBroadcastToClients = 5,
        UnreliableBroadcastToServer = 6,
        ReliableBroadcastToServer = 7,
        OnConnectionDisconnected = 8,
        OnConnectionMessage = 9,
        JoinServer = 10,
        Disconnect = 11,
        Ping = 12,
        StartServer = 13,
        UnreliableSendFromServer = 14,
        ReliableSendFromServer = 15,
        LobbyIDs = 16,
        LobbyMetadata = 17,

        SetLobbyMetadata = 20,
        SteamFriends = 21,
        DisconnectUser = 22,

        /// <summary>Redux P2P: channel, targetUserId, payload.</summary>
        SendPacket = 30,
        /// <summary>Redux P2P inbound: channel, senderUserId, payload.</summary>
        OnPacket = 31,
        CloseSession = 32,
    }
}
