using System;
using System.Collections.Generic;

using LiteNetLib;
using LiteNetLib.Utils;

using Steamworks;

namespace Entanglement.Network.Layers.Proxy
{
    /// <summary>
    /// Full Fusion-Helper ideology in-mod: PC SteamVR/Meta hosts LiteNetLib and tunnels
    /// Steamworks (identity, lobbies, P2P) for Quest Proxy SteamVR clients.
    /// When a Quest peer is connected, this process owns Steam P2P drain (PC yields).
    /// </summary>
    public static class ProxyCompanionHost
    {
        static NetManager server;
        static EventBasedNetListener listener;
        static bool running;

        static readonly Dictionary<NetPeer, PeerState> peers = new Dictionary<NetPeer, PeerState>();
        static readonly NetworkChannel[] steamChannels = {
            NetworkChannel.Reliable,
            NetworkChannel.Unreliable,
            NetworkChannel.Attack,
            NetworkChannel.Object,
            NetworkChannel.Transaction,
        };

        static CallResult<LobbyCreated_t> lobbyCreated;
        static CallResult<LobbyEnter_t> lobbyEntered;
        static NetPeer pendingLobbyPeer;

        class PeerState
        {
            public uint appId;
            public bool steamReady;
            public CSteamID lobby = CSteamID.Nil;
            public bool isHost;
        }

        public static bool IsRunning => running;
        public static int PeerCount => peers.Count;
        /// <summary>True when Quest peers own Steam P2P drain (PC Node must not ReadP2P).</summary>
        public static bool OwnsSteamDrain => running && peers.Count > 0;

        public static void StartIfNeeded() {
            if (running || Utilities.PlatformHelper.IsAndroid)
                return;

            bool steamVr = NetworkLayerManager.Layer is SteamVRNetworkLayer
                || NetworkLayerManager.IsSteamVRTitle(NetworkLayerManager.PreferredLayerName)
                || NetworkLayerManager.IsSteamVRTitle(NetworkLayerManager.ActiveTitle);
            if (!steamVr)
                return;

            try {
                listener = new EventBasedNetListener();
                server = new NetManager(listener)
                {
                    BroadcastReceiveEnabled = true,
                    UnconnectedMessagesEnabled = true,
                    DisconnectTimeout = 15000,
                };

                listener.ConnectionRequestEvent += req => req.AcceptIfKey("EntangleProxy");
                listener.PeerConnectedEvent += OnPeerConnected;
                listener.PeerDisconnectedEvent += OnPeerDisconnected;
                listener.NetworkReceiveUnconnectedEvent += OnUnconnected;
                listener.NetworkReceiveEvent += OnReceive;

                int port = NetworkPrefs.ResolvedProxyPort();
                if (!server.Start(port)) {
                    EntangleLogger.Error("[ProxyHost] Failed to bind UDP " + port);
                    server = null;
                    return;
                }

                lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
                lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);

                running = true;
                EntangleLogger.Log("[ProxyHost] Listening on " + port
                    + " — Quest Proxy SteamVR can tunnel Steamworks through this PC");
            }
            catch (Exception e) {
                EntangleLogger.Error("[ProxyHost] Start failed: " + e.Message);
                Stop();
            }
        }

        public static void Tick() {
            if (!running || server == null)
                return;
            try {
                server.PollEvents();
                if (OwnsSteamDrain)
                    DrainSteamToPeers();
            }
            catch (Exception e) {
                EntangleLogger.Warn("[ProxyHost] Tick: " + e.Message);
            }
        }

        public static void Stop() {
            try { server?.Stop(); }
            catch { }
            server = null;
            listener = null;
            peers.Clear();
            pendingLobbyPeer = null;
            running = false;
        }

        static void OnPeerConnected(NetPeer peer) {
            peers[peer] = new PeerState();
            EntangleLogger.Log("[ProxyHost] Quest peer connected — this PC now tunnels Steam P2P for them");
        }

        static void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) {
            if (peers.TryGetValue(peer, out PeerState state) && state.lobby.m_SteamID != 0) {
                try { SteamMatchmaking.LeaveLobby(state.lobby); }
                catch { }
            }
            peers.Remove(peer);
            if (pendingLobbyPeer == peer)
                pendingLobbyPeer = null;
            EntangleLogger.Log("[ProxyHost] Quest peer left (" + info.Reason + ")");
        }

        static void OnUnconnected(System.Net.IPEndPoint endPoint, NetPacketReader reader, UnconnectedMessageType type) {
            try {
                if (reader.TryGetString(out string data) && data == "ENTANGLE_SERVER_DISCOVERY") {
                    NetDataWriter reply = new NetDataWriter();
                    reply.Put("YOU_FOUND_ME");
                    server.SendUnconnectedMessage(reply, endPoint);
                }
            }
            catch { }
            finally { reader.Recycle(); }
        }

        static void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method) {
            try {
                if (!peers.TryGetValue(peer, out PeerState state))
                    state = peers[peer] = new PeerState();

                switch ((ProxyMessageTypes)reader.GetByte()) {
                    case ProxyMessageTypes.SteamID: {
                        state.appId = reader.GetUInt();
                        ulong steamId = SteamUser.GetSteamID().m_SteamID;
                        NetDataWriter idWriter = NewWriter(ProxyMessageTypes.SteamID);
                        idWriter.Put(steamId);
                        peer.Send(idWriter, DeliveryMethod.ReliableOrdered);
                        state.steamReady = steamId != 0;

                        NetDataWriter name = NewWriter(ProxyMessageTypes.GetUsername);
                        name.Put(SteamFriends.GetPersonaName() ?? "Player");
                        peer.Send(name, DeliveryMethod.ReliableOrdered);
                        break;
                    }
                    case ProxyMessageTypes.GetUsername: {
                        NetDataWriter name = NewWriter(ProxyMessageTypes.GetUsername);
                        name.Put(SteamFriends.GetPersonaName() ?? "Player");
                        peer.Send(name, DeliveryMethod.ReliableOrdered);
                        break;
                    }
                    case ProxyMessageTypes.SendPacket: {
                        byte ch = reader.GetByte();
                        long userId = reader.GetLong();
                        byte[] payload = reader.GetBytesWithLength();
                        EP2PSend send = ch == (byte)NetworkChannel.Unreliable
                            ? EP2PSend.k_EP2PSendUnreliable
                            : EP2PSend.k_EP2PSendReliable;
                        SteamNetworking.SendP2PPacket(
                            new CSteamID((ulong)userId), payload, (uint)payload.Length, send, ch);
                        break;
                    }
                    case ProxyMessageTypes.CloseSession: {
                        long userId = reader.GetLong();
                        SteamNetworking.CloseP2PSessionWithUser(new CSteamID((ulong)userId));
                        break;
                    }
                    case ProxyMessageTypes.StartServer: {
                        pendingLobbyPeer = peer;
                        EntangleLogger.Log("[ProxyHost] Creating Steam lobby for Quest host...");
                        lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 8));
                        break;
                    }
                    case ProxyMessageTypes.JoinServer: {
                        ulong serverId = reader.GetULong();
                        pendingLobbyPeer = peer;
                        EntangleLogger.Log("[ProxyHost] Joining Steam lobby " + serverId + " for Quest...");
                        lobbyEntered.Set(SteamMatchmaking.JoinLobby(new CSteamID(serverId)));
                        break;
                    }
                    case ProxyMessageTypes.Disconnect: {
                        if (state.lobby.m_SteamID != 0) {
                            try { SteamMatchmaking.LeaveLobby(state.lobby); }
                            catch { }
                            state.lobby = CSteamID.Nil;
                            state.isHost = false;
                        }
                        peer.Send(NewWriter(ProxyMessageTypes.Disconnect), DeliveryMethod.ReliableOrdered);
                        break;
                    }
                    case ProxyMessageTypes.Ping: {
                        double t = reader.GetDouble();
                        NetDataWriter writer = NewWriter(ProxyMessageTypes.Ping);
                        writer.Put(t);
                        peer.Send(writer, DeliveryMethod.ReliableOrdered);
                        break;
                    }
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn("[ProxyHost] Receive: " + e.Message);
            }
            finally {
                reader.Recycle();
            }
        }

        static void OnLobbyCreated(LobbyCreated_t result, bool ioFail) {
            NetPeer peer = pendingLobbyPeer;
            pendingLobbyPeer = null;
            if (peer == null || !peers.ContainsKey(peer))
                return;

            if (ioFail || result.m_eResult != EResult.k_EResultOK) {
                EntangleLogger.Error("[ProxyHost] Lobby create failed: " + result.m_eResult);
                peer.Send(NewWriter(ProxyMessageTypes.Disconnect), DeliveryMethod.ReliableOrdered);
                return;
            }

            PeerState state = peers[peer];
            state.lobby = new CSteamID(result.m_ulSteamIDLobby);
            state.isHost = true;
            try {
                SteamMatchmaking.SetLobbyData(state.lobby, "entangle_proxy", "1");
                SteamMatchmaking.SetLobbyData(state.lobby, "name", (SteamFriends.GetPersonaName() ?? "Player") + " (Quest)");
            }
            catch { }

            peer.Send(NewWriter(ProxyMessageTypes.StartServer), DeliveryMethod.ReliableOrdered);
            EntangleLogger.Log("[ProxyHost] Quest lobby ready: " + state.lobby.m_SteamID);
        }

        static void OnLobbyEntered(LobbyEnter_t result, bool ioFail) {
            NetPeer peer = pendingLobbyPeer;
            pendingLobbyPeer = null;
            if (peer == null || !peers.ContainsKey(peer))
                return;

            if (ioFail || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) {
                EntangleLogger.Error("[ProxyHost] Lobby join failed");
                peer.Send(NewWriter(ProxyMessageTypes.Disconnect), DeliveryMethod.ReliableOrdered);
                return;
            }

            PeerState state = peers[peer];
            state.lobby = new CSteamID(result.m_ulSteamIDLobby);
            state.isHost = false;

            ulong owner = SteamMatchmaking.GetLobbyOwner(state.lobby).m_SteamID;
            NetDataWriter writer = NewWriter(ProxyMessageTypes.JoinServer);
            writer.Put(owner);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
            EntangleLogger.Log("[ProxyHost] Quest joined lobby " + state.lobby.m_SteamID);
        }

        static void DrainSteamToPeers() {
            foreach (NetworkChannel channel in steamChannels) {
                while (SteamNetworking.IsP2PPacketAvailable(out uint size, (int)channel)) {
                    byte[] buffer = new byte[size];
                    if (!SteamNetworking.ReadP2PPacket(buffer, size, out uint read, out CSteamID remote, (int)channel))
                        break;
                    if (read == 0)
                        continue;
                    ForwardToPeers((long)remote.m_SteamID, channel, buffer);
                }
            }
        }

        static void ForwardToPeers(long senderId, NetworkChannel channel, byte[] data) {
            NetDataWriter writer = NewWriter(ProxyMessageTypes.OnPacket);
            writer.Put((byte)channel);
            writer.Put(senderId);
            writer.PutBytesWithLength(data);

            DeliveryMethod delivery = channel == NetworkChannel.Unreliable
                ? DeliveryMethod.Unreliable
                : DeliveryMethod.ReliableOrdered;

            foreach (NetPeer peer in peers.Keys) {
                try { peer.Send(writer, delivery); }
                catch { }
            }
        }

        static NetDataWriter NewWriter(ProxyMessageTypes type) {
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)type);
            return writer;
        }
    }
}
