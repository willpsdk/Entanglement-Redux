using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using MelonLoader;

using Steamworks;

using Entanglement.Representation;
using Entanglement.Compat.Playermodels;
using Entanglement.Objects;
using Entanglement.Compat;
using Entanglement.Data;
using Entanglement.Network.Layers;

using StressLevelZero.Interaction;

namespace Entanglement.Network {
    public abstract class Node {
        public List<long> connectedUsers = new List<long>();
        public Dictionary<long, string> userNames = new Dictionary<long, string>();

        // Reset per frame, but used in Entanglement -> Stats to see the network load
        public uint sentByteCount, recievedByteCount;

        public static Node activeNode;

        public static bool isServer => activeNode is Server;

        // Every channel is polled for incoming packets each frame
        public static readonly NetworkChannel[] allChannels = {
            NetworkChannel.Reliable,
            NetworkChannel.Unreliable,
            NetworkChannel.Attack,
            NetworkChannel.Object,
            NetworkChannel.Transaction
        };

        static readonly NetworkChannel[] drainableChannels = { NetworkChannel.Unreliable };

        public void ClearMessageBuffer() {
            try {
                int drained = 0;
                INetworkLayer layer = NetworkLayerManager.Layer;

                foreach (NetworkChannel channel in drainableChannels) {
                    if (layer != null) {
                        while (layer.TryReceive(channel, out _, out _))
                            drained++;
                    }
                    else {
                        while (SteamNetworking.IsP2PPacketAvailable(out uint packetSize, (int)channel)) {
                            byte[] buffer = new byte[packetSize];
                            if (!SteamNetworking.ReadP2PPacket(buffer, packetSize, out uint bytesRead, out CSteamID remoteId, (int)channel))
                                break;
                            drained++;
                        }
                    }
                }

                if (drained > 0)
                    EntangleLogger.Debug($"Drained {drained} stale network packets after suspend.");
            }
            catch (Exception e) {
                EntangleLogger.Warn($"Failed to drain the network backlog: {e.Message}");
            }
        }

        protected Callback<LobbyChatUpdate_t> lobbyChatUpdateCallback;

        public void RegisterLobbyCallbacks() {
            if (lobbyChatUpdateCallback == null)
                lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        }

        public void OnLobbyChatUpdate(LobbyChatUpdate_t update) {
            if (update.m_ulSteamIDLobby != SteamIntegration.lobby.m_SteamID)
                return;

            long userId = (long)update.m_ulSteamIDUserChanged;

            if (userId == SteamIntegration.currentUserId)
                return;

            if ((update.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
                OnUserJoined(userId);
            else
                OnUserLeft(userId);
        }

        public void OnUserJoined(long userId) {
            if (EntangleLogger.verbose)
                EntangleLogger.Debug("[Join] " + userId + " entered the lobby. " + DebugLog.Snapshot());
            CreatePlayerRep(userId);

            // Send PlayerModel
            if (PlayermodelsPatch.lastLoadedPath != null) {
                string path = PlayermodelsPatch.lastLoadedPath;
                LoadCustomPlayerMessageData msgData = new LoadCustomPlayerMessageData();
                msgData.userId = SteamIntegration.currentUserId;
                msgData.modelPath = Path.GetFileName(path);
                msgData.requestCallback = true;
                SendMessage(userId, NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.PlayerModel, msgData).GetBytes());
            }

            UserConnectedEvent(userId);
        }

        public void OnUserLeft(long userId) {
            EntangleLogger.Debug("[Join] " + userId + " left the lobby: releasing what they held, owned and holstered");

            // Drop stuck grips / ownership so remotes don't keep hard-tracking a ghost hold
            ClearOwnershipForUser(userId);

            // What they had holstered stays where it was, simulated again.
            try { TransformCollisionMessageHandler.ReleaseHolder(userId); }
            catch { }

            if (PlayerRepresentation.representations.TryGetValue(userId, out PlayerRepresentation rep)) {
                EntangleNotif.PlayerLeave($"{rep.playerName}");

                // Fades out where they stood rather than vanishing.
                rep.DeleteRepresentations(true);
                PlayerRepresentation.representations.Remove(userId);
            }

            Entanglement.Objects.BossClawSync.OnUserLeft(userId);
            Entanglement.Objects.SpawnCatchUp.OnUserLeft(userId);
            try { Entanglement.Entities.NetworkEntityManager.OnPlayerLeft(userId); }
            catch { }

            userNames.Remove(userId);
            connectedUsers.Remove(userId);
            SteamIntegration.RemoveUser(userId);
            SteamIntegration.CloseSession(userId);

            UserDisconnectEvent(userId);
        }

        public static void SweepGhostReps() {
            if (!SteamIntegration.hasLobby)
                return;

            List<long> ghosts = null;

            foreach (KeyValuePair<long, PlayerRepresentation> pair in PlayerRepresentation.representations) {
                long userId = pair.Key;

                if (userId == SteamIntegration.currentUserId)
                    continue;
                if (userId == PlayerRepresentation.TesterRepId)
                    continue;

                try {
                    if (SteamIntegration.IsLobbyMember(new CSteamID((ulong)userId)))
                        continue;
                }
                catch { continue; }

                if (ghosts == null)
                    ghosts = new List<long>();
                ghosts.Add(userId);
            }

            if (ghosts == null)
                return;

            for (int i = 0; i < ghosts.Count; i++) {
                long userId = ghosts[i];
                EntangleLogger.Warn("[Join] Removing a rep for " + userId + " - not in the lobby any more.");

                try {
                    if (PlayerRepresentation.representations.TryGetValue(userId, out PlayerRepresentation rep)) {
                        rep.DeleteRepresentations();
                        PlayerRepresentation.representations.Remove(userId);
                    }
                }
                catch (Exception e) { EntangleLogger.Warn("[Join] Ghost cleanup: " + e.Message); }

                try { activeNode?.userNames.Remove(userId); } catch { }
                try { activeNode?.connectedUsers.Remove(userId); } catch { }
                try { Entanglement.Objects.BossClawSync.OnUserLeft(userId); } catch { }
            }
        }

        static void ClearOwnershipForUser(long userId) {
            try {
                long hostId = SteamIntegration.lobbyOwnerId;
                foreach (Syncable syncable in ObjectSync.syncedObjects.Values) {
                    if (syncable == null)
                        continue;
                    try {
                        if (syncable.ownerQueue != null && syncable.ownerQueue.Contains(userId))
                            syncable.DequeueOwner(userId);

                        // Departed player must not keep ghost ownership - props would freeze.
                        if (syncable.staleOwner == userId) {
                            long next = hostId != 0 ? hostId : SteamIntegration.currentUserId;
                            if (next != 0 && next != userId)
                                syncable.ForceOwner(next);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void CreatePlayerRep(long userId)
        {
            if (PlayerRepresentation.representations.ContainsKey(userId))
                return;

            if (!connectedUsers.Contains(userId))
                connectedUsers.Add(userId);
            SteamIntegration.FetchUser(userId, OnUserFetched);
        }

        public void CleanData() {
            ReleaseLocalHands();

            foreach (long user in connectedUsers)
                SteamIntegration.CloseSession(user);

            connectedUsers.Clear();
            userNames.Clear();
            ObjectSync.OnCleanup();

            foreach (PlayerRepresentation playerRep in PlayerRepresentation.representations.Values)
                playerRep.DeleteRepresentations();

            PlayerRepresentation.representations.Clear();
            SteamIntegration.byteIds.Clear();
            SteamIntegration.localByteId = 0;
            SteamIntegration.lastByteId = 1;

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = PlayerScripts.reloadLevelOnDeath;

            Entanglement.Patching.ClientSceneGate.Clear();
            DownloadGate.Reset();
            Entanglement.Sync.CustomItemSync.ClearAllRequested();

            if (lobbyChatUpdateCallback != null) {
                lobbyChatUpdateCallback.Dispose();
                lobbyChatUpdateCallback = null;
            }

            CleanupEvent();
        }

        public static void ReleaseLocalHands() {
            try {
                if (PlayerScripts.playerLeftHand && PlayerScripts.playerLeftHand.m_CurrentAttachedObject)
                    PlayerScripts.playerLeftHand.DetachObject(PlayerScripts.playerLeftHand.m_CurrentAttachedObject);
            }
            catch { }
            try {
                if (PlayerScripts.playerRightHand && PlayerScripts.playerRightHand.m_CurrentAttachedObject)
                    PlayerScripts.playerRightHand.DetachObject(PlayerScripts.playerRightHand.m_CurrentAttachedObject);
            }
            catch { }
        }

        public void OnUserFetched(long userId, string userName) {
            if (PlayerRepresentation.representations.ContainsKey(userId))
                return;

            if (!connectedUsers.Contains(userId)) {
                EntangleLogger.Warn("[Join] Dropped a late profile for " + userId
                    + " - they left before it arrived. No ghost rep created.");
                return;
            }

            // A blank name shows up as an unlabelled grey row in the player list.
            if (string.IsNullOrEmpty(userName))
                userName = "Player " + (userId % 100000L);

            PlayerRepresentation.representations.Add(userId, new PlayerRepresentation(userName, userId));
            userNames[userId] = userName;
            Entanglement.Sync.PlayermodelSync.ApplyWanted(userId);

            EntangleNotif.PlayerJoin(userName);
        }

        // A burst (a wave of NPCs, a pile of crates breaking) was all handled in one frame, building every
        // object at once: 12-24ms frames on Oct 8, more on slow PCs. Past this much time in a frame the
        // rest stays queued in Steam, in order, for the next frame. Not while loading.
        const double ReceiveBudgetMs = 8.0;
        static readonly long receiveBudgetTicks = (long)(System.Diagnostics.Stopwatch.Frequency * ReceiveBudgetMs / 1000.0);

        public void ReceiveMessages() {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            bool budgeted = !SceneLoader.loading;
            int handled = 0;
            INetworkLayer layer = NetworkLayerManager.Layer;

            foreach (NetworkChannel channel in allChannels) {
                while (true) {
                    // At least 48 a frame whatever the time, so a slow PC's queue still drains.
                    if (budgeted && handled >= 48 && System.Diagnostics.Stopwatch.GetTimestamp() - start > receiveBudgetTicks)
                        return;

                    long senderId;
                    byte[] buffer;
                    if (layer != null) {
                        if (!layer.TryReceive(channel, out senderId, out buffer))
                            break;
                    }
                    else {
                        if (!SteamNetworking.IsP2PPacketAvailable(out uint packetSize, (int)channel))
                            break;
                        buffer = new byte[packetSize];
                        if (!SteamNetworking.ReadP2PPacket(buffer, packetSize, out uint bytesRead, out CSteamID remoteId, (int)channel))
                            continue;
                        senderId = (long)remoteId.m_SteamID;
                    }

                    handled++;
                    try { OnMessageReceived(senderId, buffer); }
                    catch (Exception e) { NoteMessageFailure(e); }
                }
            }
        }

        static int messageFailures;

        static void NoteMessageFailure(Exception e) {
            messageFailures++;

            if (messageFailures <= 3 || messageFailures % 600 == 0)
                EntangleLogger.Warn("[Net] Dropped a message (" + messageFailures + "x): " + e.Message);
        }

        public void OnMessageReceived(long userId, byte[] data)
        {
            if (data.Length <= 0) // Idk
                throw new Exception("Data was invalid!");

            NetworkMessage message = new NetworkMessage();

            message.messageType = data[0];
            message.messageData = new byte[data.Length - sizeof(byte)];

            // Every packet goes through here; a block copy instead of a byte-at-a-time loop.
            Buffer.BlockCopy(data, sizeof(byte), message.messageData, 0, message.messageData.Length);

            recievedByteCount += (uint)data.Length;
            SteamIntegration.CheckSenderId(message, userId);
            NetworkMessage.ReadMessage(message, userId);
        }

        public void SendMessage(long userId, NetworkChannel channel, byte[] data) {
            if (!SteamIntegration.hasLobby && (NetworkLayerManager.Layer == null || !NetworkLayerManager.Layer.HasLobby))
                return;

            INetworkLayer layer = NetworkLayerManager.Layer;
            bool sent = layer != null
                ? layer.Send(userId, channel, data)
                : SteamIntegration.SendPacket(userId, channel, data);
            if (sent)
                sentByteCount += (uint)data.Length;
        }

        public virtual void BroadcastMessage(NetworkChannel channel, byte[] data) { }

        public void BroadcastMessageP2P(NetworkChannel channel, byte[] data) {
            connectedUsers.ForEach((user) => { SendMessage(user, channel, data); });

            long host = SteamIntegration.lobbyOwnerId;
            if (!isServer && host != 0 && !connectedUsers.Contains(host))
                SendMessage(host, channel, data);
        }

        public virtual void Tick() { }

        public virtual void UserConnectedEvent(long userId) { }

        public virtual void UserDisconnectEvent(long userId) { }

        public virtual void CleanupEvent() { }

        // The active node's shutdown is called upon closing the game
        public virtual void Shutdown() { }
    }
}
