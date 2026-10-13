using System;
using System.Collections.Generic;

using LiteNetLib;
using LiteNetLib.Utils;

using MelonLoader;

using Steamworks;

namespace Entanglement.Network.Layers.Proxy
{
    /// <summary>
    /// In-mod Fusion-Helper ideology: when PC runs SteamVR (Meta Horizon Link), also host a
    /// LiteNetLib companion so Quest Proxy SteamVR clients can tunnel Steamworks through this machine.
    /// Discovery: ENTANGLE_SERVER_DISCOVERY → YOU_FOUND_ME on <see cref="NetworkPrefs.proxyPort"/>.
    /// </summary>
    public static class ProxyCompanionHost
    {
        static NetManager server;
        static EventBasedNetListener listener;
        static bool running;
        static readonly Dictionary<NetPeer, PeerState> peers = new Dictionary<NetPeer, PeerState>();

        class PeerState
        {
            public uint appId;
            public bool steamReady;
        }

        public static bool IsRunning => running;

        public static void StartIfNeeded() {
            if (running || Utilities.PlatformHelper.IsAndroid)
                return;
            if (!(NetworkLayerManager.Layer is SteamVRNetworkLayer))
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
                listener.PeerDisconnectedEvent += (peer, info) => {
                    peers.Remove(peer);
                    EntangleLogger.Log("[ProxyHost] Quest peer left (" + info.Reason + ")");
                };
                listener.NetworkReceiveUnconnectedEvent += OnUnconnected;
                listener.NetworkReceiveEvent += OnReceive;

                int port = NetworkPrefs.ResolvedProxyPort();
                if (!server.Start(port)) {
                    EntangleLogger.Error("[ProxyHost] Failed to bind UDP " + port);
                    server = null;
                    return;
                }

                running = true;
                EntangleLogger.Log("[ProxyHost] Meta Horizon companion listening on " + port
                    + " (SteamVR AppID " + SteamAppIds.SteamVR + ") — Quest can join via Proxy SteamVR");
            }
            catch (Exception e) {
                EntangleLogger.Error("[ProxyHost] Start failed: " + e.Message);
                Stop();
            }
        }

        public static void Tick() {
            if (!running || server == null)
                return;
            try { server.PollEvents(); }
            catch (Exception e) { EntangleLogger.Warn("[ProxyHost] Poll: " + e.Message); }
        }

        public static void Stop() {
            try { server?.Stop(); }
            catch { }
            server = null;
            listener = null;
            peers.Clear();
            running = false;
        }

        static void OnPeerConnected(NetPeer peer) {
            peers[peer] = new PeerState();
            EntangleLogger.Log("[ProxyHost] Quest peer connected from " + peer.EndPoint);
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

                byte type = reader.GetByte();
                switch ((ProxyMessageTypes)type) {
                    case ProxyMessageTypes.SteamID: {
                        state.appId = reader.GetUInt();
                        ulong steamId = SteamUser.GetSteamID().m_SteamID;
                        NetDataWriter writer = NewWriter(ProxyMessageTypes.SteamID);
                        writer.Put(steamId);
                        peer.Send(writer, DeliveryMethod.ReliableOrdered);
                        state.steamReady = steamId != 0;

                        NetDataWriter name = NewWriter(ProxyMessageTypes.GetUsername);
                        name.Put(SteamFriends.GetPersonaName() ?? "Player");
                        peer.Send(name, DeliveryMethod.ReliableOrdered);
                        EntangleLogger.Log("[ProxyHost] Sent SteamID " + steamId + " to Quest peer (requested AppID "
                            + state.appId + ")");
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
                        SteamNetworking.SendP2PPacket(new CSteamID((ulong)userId), payload, (uint)payload.Length, send, ch);
                        break;
                    }
                    case ProxyMessageTypes.CloseSession: {
                        long userId = reader.GetLong();
                        SteamNetworking.CloseP2PSessionWithUser(new CSteamID((ulong)userId));
                        break;
                    }
                    case ProxyMessageTypes.StartServer: {
                        // Companion marks host session; actual Boneworks lobby stays on this PC.
                        NetDataWriter writer = NewWriter(ProxyMessageTypes.StartServer);
                        peer.Send(writer, DeliveryMethod.ReliableOrdered);
                        EntangleLogger.Log("[ProxyHost] Quest peer requested StartServer — use this PC lobby");
                        break;
                    }
                    case ProxyMessageTypes.JoinServer: {
                        ulong serverId = reader.GetULong();
                        NetDataWriter writer = NewWriter(ProxyMessageTypes.JoinServer);
                        writer.Put(serverId);
                        peer.Send(writer, DeliveryMethod.ReliableOrdered);
                        break;
                    }
                    case ProxyMessageTypes.Disconnect: {
                        NetDataWriter writer = NewWriter(ProxyMessageTypes.Disconnect);
                        peer.Send(writer, DeliveryMethod.ReliableOrdered);
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

        /// <summary>Forward inbound Steam P2P to all Quest peers (host relay for proxy clients).</summary>
        public static void ForwardSteamPacket(long senderId, NetworkChannel channel, byte[] data) {
            if (!running || peers.Count == 0 || data == null)
                return;

            NetDataWriter writer = NewWriter(ProxyMessageTypes.OnPacket);
            writer.Put((byte)channel);
            writer.Put(senderId);
            writer.PutBytesWithLength(data);

            foreach (NetPeer peer in peers.Keys) {
                try {
                    peer.Send(writer, channel == NetworkChannel.Unreliable
                        ? DeliveryMethod.Unreliable
                        : DeliveryMethod.ReliableOrdered);
                }
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
