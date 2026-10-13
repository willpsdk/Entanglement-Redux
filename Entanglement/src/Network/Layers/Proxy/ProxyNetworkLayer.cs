using System;
using System.Collections;
using System.Collections.Generic;

using LiteNetLib;
using LiteNetLib.Utils;

using MelonLoader;

using UnityEngine;

namespace Entanglement.Network.Layers.Proxy
{
    /// <summary>
    /// Fusion ProxyNetworkLayer ideology for Quest: no local Steamworks.
    /// Discovers a PC companion (Entanglement Helper / Fusion-Helper-shaped) over
    /// LiteNetLib UDP and tunnels Steam identity + P2P through it.
    /// </summary>
    public abstract class ProxyNetworkLayer : INetworkLayer
    {
        public abstract uint ApplicationID { get; }

        public virtual string Title => "Proxy";
        public virtual string Platform => "Steam";

        public static ProxyNetworkLayer Instance { get; private set; }

        public bool IsAvailable => _loggedIn && SteamId != 0;
        public bool IsLoggedIn => _loggedIn && SteamId != 0;
        public bool HasLobby => _hasLobby;
        public bool IsHost => _isServerActive;
        public bool IsClient => _isConnectionActive && !_isServerActive;

        public long LocalUserId => (long)SteamId;
        public string LocalUserName => _username ?? "Unknown";
        public long LobbyOwnerId => _lobbyOwnerId;

        public ulong SteamId { get; private set; }

        protected bool _isServerActive;
        protected bool _isConnectionActive;
        protected bool _hasLobby;
        protected bool _loggedIn;
        protected bool _isInitialized;
        protected long _lobbyOwnerId;
        string _username;

        NetManager _client;
        NetPeer _serverConnection;
        EventBasedNetListener _listener;

        readonly object _inboxLock = new object();
        readonly Queue<InboundPacket> _inbox = new Queue<InboundPacket>();

        struct InboundPacket
        {
            public NetworkChannel channel;
            public long senderId;
            public byte[] data;
        }

        public virtual bool CheckSupported() => Utilities.PlatformHelper.IsAndroid;

        public void Initialize() {
            Instance = this;
            SteamIntegration.EnterProxyMode(this);
            EntangleLogger.Log("[NetLayer] Active transport: " + Title
                + " (Quest → PC Steam companion, AppID " + ApplicationID + ")");
            StartProxyClient();
            MelonCoroutines.Start(DiscoverServer());
        }

        public void Tick() {
            try { _client?.PollEvents(); }
            catch (Exception e) { EntangleLogger.Warn("[Proxy] Poll: " + e.Message); }
        }

        public void Shutdown() {
            try {
                DisconnectProxy();
                _client?.Stop();
            }
            catch { }
            _client = null;
            _serverConnection = null;
            _loggedIn = false;
            if (Instance == this)
                Instance = null;
        }

        public bool Send(long userId, NetworkChannel channel, byte[] data) {
            if (_serverConnection == null || data == null)
                return false;

            try {
                NetDataWriter writer = NewWriter(ProxyMessageTypes.SendPacket);
                writer.Put((byte)channel);
                writer.Put(userId);
                writer.PutBytesWithLength(data);
                _serverConnection.Send(writer, IsReliable(channel) ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
                return true;
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Proxy] Send failed: " + e.Message);
                return false;
            }
        }

        public bool IsPacketAvailable(NetworkChannel channel, out uint size) {
            lock (_inboxLock) {
                foreach (InboundPacket p in _inbox) {
                    if (p.channel == channel) {
                        size = (uint)(p.data?.Length ?? 0);
                        return true;
                    }
                }
            }
            size = 0;
            return false;
        }

        public bool TryReceive(NetworkChannel channel, out long senderId, out byte[] data) {
            lock (_inboxLock) {
                int count = _inbox.Count;
                for (int i = 0; i < count; i++) {
                    InboundPacket p = _inbox.Dequeue();
                    if (p.channel == channel) {
                        senderId = p.senderId;
                        data = p.data;
                        return true;
                    }
                    _inbox.Enqueue(p);
                }
            }
            senderId = 0;
            data = null;
            return false;
        }

        public void CloseSession(long userId) {
            if (_serverConnection == null)
                return;
            NetDataWriter writer = NewWriter(ProxyMessageTypes.CloseSession);
            writer.Put(userId);
            _serverConnection.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void RequestStartServer() {
            SendToProxyServer(ProxyMessageTypes.StartServer);
        }

        public void RequestJoinServer(ulong serverId) {
            NetDataWriter writer = NewWriter(ProxyMessageTypes.JoinServer);
            writer.Put(serverId);
            SendToProxyServer(writer);
            _isServerActive = false;
            _isConnectionActive = true;
            _hasLobby = true;
        }

        public void RequestDisconnect() {
            SendToProxyServer(ProxyMessageTypes.Disconnect);
            _isServerActive = false;
            _isConnectionActive = false;
            _hasLobby = false;
        }

        void StartProxyClient() {
            if (_client != null) {
                _client.Stop();
                _client = null;
                _serverConnection = null;
            }

            _listener = new EventBasedNetListener();
            _client = new NetManager(_listener)
            {
                UnconnectedMessagesEnabled = true,
                BroadcastReceiveEnabled = true,
                DisconnectOnUnreachable = true,
                DisconnectTimeout = 10000,
                PingInterval = 5000,
            };

            _listener.NetworkReceiveEvent += OnNetworkReceive;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveUnconnectedEvent += OnUnconnected;

            _client.Start();
            EntangleLogger.Log("[Proxy] Beginning companion discovery on port "
                + NetworkPrefs.ResolvedProxyPort() + "...");
        }

        IEnumerator DiscoverServer() {
            int port = NetworkPrefs.ResolvedProxyPort();
            NetDataWriter writer = new NetDataWriter();
            writer.Put("ENTANGLE_SERVER_DISCOVERY");

            while (_serverConnection == null && _client != null) {
                try { _client.SendBroadcast(writer, port); }
                catch { }

                float waited = 0f;
                while (waited < 5f && _serverConnection == null) {
                    try { _client?.PollEvents(); }
                    catch { }
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        void OnPeerConnected(NetPeer peer) {
            _serverConnection = peer;
            _loggedIn = true;
            EntangleLogger.Log("[Proxy] Connected to PC Steam companion.");

            NetDataWriter writer = NewWriter(ProxyMessageTypes.SteamID);
            writer.Put(ApplicationID);
            SendToProxyServer(writer);
        }

        void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) {
            EntangleLogger.Error("[Proxy] Companion disconnected (" + info.Reason + ").");
            _serverConnection = null;
            _loggedIn = false;
            _isInitialized = false;
            _hasLobby = false;
            SteamIntegration.OnProxyLoggedOut();
        }

        void OnUnconnected(System.Net.IPEndPoint endPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
            if (reader.TryGetString(out string data) && data == "YOU_FOUND_ME") {
                EntangleLogger.Log("[Proxy] Found companion at " + endPoint);
                _client.Connect(endPoint, "EntangleProxy");
            }
            reader.Recycle();
        }

        void OnNetworkReceive(NetPeer fromPeer, NetPacketReader dataReader, byte channel, DeliveryMethod deliveryMethod) {
            try {
                EvaluateMessage(dataReader);
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Proxy] EvaluateMessage: " + e.Message);
            }
            finally {
                dataReader.Recycle();
            }
        }

        void EvaluateMessage(NetPacketReader dataReader) {
            byte id = dataReader.GetByte();
            switch ((ProxyMessageTypes)id) {
                case ProxyMessageTypes.Ping: {
                    double theTime = dataReader.GetDouble();
                    double curTime = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds;
                    EntangleLogger.Debug("[Proxy] RTT hint " + (curTime - theTime) + " ms");
                    NetDataWriter writer = NewWriter(ProxyMessageTypes.Ping);
                    writer.Put(curTime);
                    SendToProxyServer(writer);
                    break;
                }
                case ProxyMessageTypes.SteamID: {
                    SteamId = dataReader.GetULong();
                    if (SteamId == 0) {
                        EntangleLogger.Error("[Proxy] Steamworks failed to initialize on companion!");
                        break;
                    }
                    SteamIntegration.OnProxySteamReady((long)SteamId, _username);
                    NetDataWriter writer = NewWriter(ProxyMessageTypes.GetUsername);
                    writer.Put(SteamId);
                    SendToProxyServer(writer);
                    EntangleLogger.Log("[Proxy] Steamworks via companion, SteamID " + SteamId);
                    _isInitialized = true;
                    break;
                }
                case ProxyMessageTypes.GetUsername: {
                    _username = dataReader.GetString();
                    SteamIntegration.OnProxySteamReady((long)SteamId, _username);
                    break;
                }
                case ProxyMessageTypes.OnPacket: {
                    byte ch = dataReader.GetByte();
                    long sender = dataReader.GetLong();
                    byte[] payload = dataReader.GetBytesWithLength();
                    Enqueue((NetworkChannel)ch, sender, payload);
                    break;
                }
                case ProxyMessageTypes.OnMessage:
                case ProxyMessageTypes.OnConnectionMessage: {
                    // Fusion-shaped: payload + platform id (no channel) → Unreliable inbox
                    byte[] payload = dataReader.GetBytesWithLength();
                    ulong platformId = dataReader.GetULong();
                    Enqueue(NetworkChannel.Reliable, (long)platformId, payload);
                    break;
                }
                case ProxyMessageTypes.StartServer: {
                    _isServerActive = true;
                    _isConnectionActive = true;
                    _hasLobby = true;
                    _lobbyOwnerId = (long)SteamId;
                    SteamIntegration.OnProxyLobbyChanged(true, true, (long)SteamId);
                    EntangleLogger.Log("[Proxy] Companion started Steam lobby / host.");
                    break;
                }
                case ProxyMessageTypes.JoinServer: {
                    ulong serverId = dataReader.GetULong();
                    _isServerActive = false;
                    _isConnectionActive = true;
                    _hasLobby = true;
                    _lobbyOwnerId = (long)serverId;
                    SteamIntegration.OnProxyLobbyChanged(true, false, (long)serverId);
                    break;
                }
                case ProxyMessageTypes.OnConnectionDisconnected:
                case ProxyMessageTypes.Disconnect: {
                    _isServerActive = false;
                    _isConnectionActive = false;
                    _hasLobby = false;
                    SteamIntegration.OnProxyLobbyChanged(false, false, 0);
                    break;
                }
                case ProxyMessageTypes.OnDisconnected: {
                    ulong left = dataReader.GetULong();
                    EntangleLogger.Log("[Proxy] Peer left: " + left);
                    break;
                }
            }
        }

        void Enqueue(NetworkChannel channel, long senderId, byte[] data) {
            lock (_inboxLock) {
                _inbox.Enqueue(new InboundPacket {
                    channel = channel,
                    senderId = senderId,
                    data = data,
                });
            }
        }

        static bool IsReliable(NetworkChannel channel) => channel != NetworkChannel.Unreliable;

        internal static NetDataWriter NewWriter(ProxyMessageTypes type) {
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)type);
            return writer;
        }

        public void SendToProxyServer(NetDataWriter writer) {
            if (_serverConnection == null) {
                EntangleLogger.Warn("[Proxy] No companion — is Entanglement Helper running on the PC?");
                return;
            }
            _serverConnection.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        void SendToProxyServer(ProxyMessageTypes type) {
            SendToProxyServer(NewWriter(type));
        }

        void DisconnectProxy() {
            if (_isServerActive || _isConnectionActive)
                RequestDisconnect();
        }
    }
}
