using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

using MelonLoader;

using Steamworks;

using Entanglement.Representation;
using Entanglement.Data;
using Entanglement.Objects;
using Entanglement.Extensions;

using StressLevelZero;

using UnityEngine;

namespace Entanglement.Network
{
    // Maps onto Steam's ELobbyType, kept as its own enum so BoneMenu can display it
    public enum ServerVisibility : byte {
        Private,
        FriendsOnly,
        Public,
    }

    public class Server : Node {
        // Static preferences
        public static byte maxPlayers = 8;
        public static bool isLocked = false;
        public static ServerVisibility visibility = ServerVisibility.Private;

        // Hard locked settings
        public const byte serverMinimum = 1;
        public const byte serverCapacity = 250; // Steam lobbies max out at 250 members

        // The value of the dict increases with time. When a user sends a heartbeat reset it to 0.
        // If its ever greater than a certain amount of seconds we should disconnect them as they have likely lost connection.
        public Dictionary<long, float> userBeats = new Dictionary<long, float>();

        // There can only be one server, otherwise things will break
        public static Server instance = null;

        public static void StartServer() {
            if (instance != null)
                instance.Shutdown();

            if (SteamIntegration.isConnected) {
                EntangleLogger.Error("Already in a server!");
                return;
            }

            EntangleLogger.Log($"Started a new server instance!");
            activeNode = instance = new Server();

            if (PlayerScripts.playerHealth)
                PlayerScripts.playerHealth.reloadLevelOnDeath = false;
        }

        //
        // Actual code below
        //

        private CallResult<LobbyCreated_t> lobbyCreatedResult;

        private Server() {
            EntangleLogger.Log($"Creating a Steam lobby with a capacity of {maxPlayers} players!");

            lobbyCreatedResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyCreatedResult.Set(SteamMatchmaking.CreateLobby(GetLobbyType(), maxPlayers));
        }

        public static ELobbyType GetLobbyType() {
            switch (visibility) {
                default:
                case ServerVisibility.Private:
                    return ELobbyType.k_ELobbyTypePrivate;
                case ServerVisibility.FriendsOnly:
                    return ELobbyType.k_ELobbyTypeFriendsOnly;
                case ServerVisibility.Public:
                    return ELobbyType.k_ELobbyTypePublic;
            }
        }

        public void OnLobbyCreated(LobbyCreated_t result, bool bIOFailure)
        {
            if (bIOFailure || result.m_eResult != EResult.k_EResultOK) {
                EntangleLogger.Error($"Failed to create a Steam lobby with result {result.m_eResult}!");

                instance = null;
                activeNode = Client.instance;
                return;
            }

            SteamIntegration.lobby = new CSteamID(result.m_ulSteamIDLobby);

            // Lobby metadata, used by the lobby browser to filter and display servers
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, "entanglement", "true");
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, "version", EntanglementMod.VersionString);
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, "host_name", SteamIntegration.currentUserName);
            SteamMatchmaking.SetLobbyJoinable(SteamIntegration.lobby, !isLocked);

            RegisterLobbyCallbacks();

            SteamIntegration.UpdateActivity();

            EntangleNotif.LobbyStarted();
        }

        public override void Tick() {
            if (EntanglementMod.sceneChange != null) {
                EntangleLogger.Log($"Notifying clients of scene change to {EntanglementMod.sceneChange}...");

                LevelChangeMessageData levelChangeData = new LevelChangeMessageData() { sceneIndex = (byte)EntanglementMod.sceneChange, sceneReload = true };
                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.LevelChange, levelChangeData);

                byte[] msgBytes = message.GetBytes();
                foreach (long user in connectedUsers)
                    SendMessage(user, NetworkChannel.Reliable, msgBytes);

                EntanglementMod.sceneChange = null;
            }

            base.Tick();
        }

        public void UpdateLobbyConfig() {
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                return;

            SteamMatchmaking.SetLobbyType(SteamIntegration.lobby, GetLobbyType());
            SteamMatchmaking.SetLobbyMemberLimit(SteamIntegration.lobby, maxPlayers);
            SteamMatchmaking.SetLobbyJoinable(SteamIntegration.lobby, !isLocked);

            SteamIntegration.UpdateActivity();

            if (maxPlayers < connectedUsers.Count) {
                uint usersToDisconnect = (uint)connectedUsers.Count - maxPlayers;

                DisconnectMessageData disconnectData = new DisconnectMessageData();
                disconnectData.disconnectReason = (byte)DisconnectReason.ServerFull;

                NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
                byte[] disconnectBytes = disconnectMsg.GetBytes();

                for (int i = 0; i < usersToDisconnect; i++)
                    SendMessage(connectedUsers[i], NetworkChannel.Reliable, disconnectBytes);
            }
        }

        public void CloseLobby() {
            DisconnectMessageData disconnectData = new DisconnectMessageData();
            disconnectData.disconnectReason = (byte)DisconnectReason.ServerClosed;

            NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
            byte[] disconnectBytes = disconnectMsg.GetBytes();
            foreach (long user in connectedUsers) {
                SendMessage(user, NetworkChannel.Reliable, disconnectBytes);
            }

            if (SteamIntegration.hasLobby)
                SteamMatchmaking.LeaveLobby(SteamIntegration.lobby);

            SteamIntegration.lobby = CSteamID.Nil;

            CleanData();
        }

        public override void Shutdown() {
            if (SteamIntegration.hasLobby && !SteamIntegration.isHost) {
                EntangleLogger.Error("Unable to close the server as a client!");
                return;
            }

            CloseLobby();
            SteamIntegration.DefaultRichPresence();

            instance = null;
            activeNode = Client.instance;
        }

        public override void UserConnectedEvent(long userId) {
            // currentSceneIndex shouldn't ever be larger than 255 so a byte is fine
            LevelChangeMessageData levelChangeData = new LevelChangeMessageData() { sceneIndex = (byte)StressLevelZero.Utilities.BoneworksSceneManager.currentSceneIndex };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.LevelChange, levelChangeData);
            SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());

            SteamIntegration.UpdateActivity();

            foreach (KeyValuePair<byte, long> valuePair in SteamIntegration.byteIds) {
                if (valuePair.Value == userId) continue;

                ShortIdMessageData addMessageData = new ShortIdMessageData()
                {
                    userId = valuePair.Value,
                    byteId = valuePair.Key,
                };
                NetworkMessage addMessage = NetworkMessage.CreateMessage((byte)BuiltInMessageType.ShortId, addMessageData);
                SendMessage(userId, NetworkChannel.Reliable, addMessage.GetBytes());
            }

            ShortIdMessageData idMessageData = new ShortIdMessageData() {
                userId = userId,
                byteId = SteamIntegration.RegisterUser(userId)
            };
            NetworkMessage idMessage = NetworkMessage.CreateMessage((byte)BuiltInMessageType.ShortId, idMessageData);
            BroadcastMessage(NetworkChannel.Reliable, idMessage.GetBytes());

            userBeats.Add(userId, 0f);
        }

        public override void UserDisconnectEvent(long userId) {
            SteamIntegration.UpdateActivity();

            userBeats.Remove(userId);
            replayedUsers.Remove(userId);
        }

        // Users who already received the world state replay for the current scene, so a duplicate
        // ClientReady can't replay spawns twice. Cleared on level change and on disconnect.
        public readonly HashSet<long> replayedUsers = new HashSet<long>();

        // Replays the current world state to a late joiner once their scene is ready
        public void ReplayWorldStateTo(long userId) {
            if (!replayedUsers.Add(userId))
                return;

            int spawnCount = 0, propCount = 0, gripCount = 0;

            // Pooled objects (guns, items), sent at their current transform
            foreach (PooleeSyncable poolee in PooleeSyncable._PooleeLookup.Values.ToArray()) {
                if (!poolee || !poolee.gameObject || !poolee.gameObject.activeInHierarchy)
                    continue;

                if (!poolee.Poolee || !poolee.Poolee.pool)
                    continue;

                Rigidbody[] rbs = poolee.GetComponentsInChildren<Rigidbody>();

                SpawnClientMessageData spawnData = new SpawnClientMessageData() {
                    rbCount = (byte)rbs.Length,
                    spawnId = poolee.id,
                    title = SpawnManager.GetPoolTitle(poolee.Poolee.pool),
                    transform = new SimplifiedTransform(poolee.transform),
                };

                NetworkMessage spawnMessage = NetworkMessage.CreateMessage(BuiltInMessageType.SpawnClient, spawnData);
                if (spawnMessage != null) {
                    SendMessage(userId, NetworkChannel.Reliable, spawnMessage.GetBytes());
                    spawnCount++;
                }
            }

            // Interacted-with scene props, resolved by path then placed at their current transform.
            // Poolee children are skipped; the spawn replay above already recreated their syncables.
            foreach (Syncable syncable in ObjectSync.syncedObjects.Values.ToArray()) {
                TransformSyncable sync = syncable as TransformSyncable;
                if (!sync || !sync.transform)
                    continue;

                if (SceneEventSync.FindPooleeSyncable(sync.transform))
                    continue;

                ObjectSync.GetPooleeData(sync.transform, out _, out string overrideRootName, out short spawnIndex, out float spawnTime);

                TransformCreateMessageData createData = new TransformCreateMessageData() {
                    ownerId = sync.staleOwner != 0 ? sync.staleOwner : SteamIntegration.lobbyOwnerId,
                    objectId = sync.objectId,
                    callbackIndex = 0,
                    spawnIndex = spawnIndex,
                    spawnTime = spawnTime,
                    enqueueOwner = false,
                    objectPath = sync.transform.GetFullPath(overrideRootName),
                };

                NetworkMessage createMessage = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCreate, createData);
                if (createMessage == null)
                    continue;

                SendMessage(userId, NetworkChannel.Reliable, createMessage.GetBytes());

                TransformSyncMessageData syncData = new TransformSyncMessageData() {
                    objectId = sync.objectId,
                    simplifiedTransform = new SimplifiedTransform(sync.transform),
                    velocity = sync.rb ? sync.rb.velocity : Vector3.zero,
                    angularVelocity = sync.rb ? sync.rb.angularVelocity : Vector3.zero,
                };

                NetworkMessage syncMessage = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSync, syncData);
                if (syncMessage != null)
                    SendMessage(userId, NetworkChannel.Reliable, syncMessage.GetBytes());

                propCount++;
            }

            // Active grip ownership, so a currently-held object stays with its holder on the joiner
            foreach (Syncable syncable in ObjectSync.syncedObjects.Values.ToArray()) {
                if (syncable == null)
                    continue;

                foreach (long owner in syncable.ownerQueue) {
                    TransformQueueMessageData queueData = new TransformQueueMessageData() {
                        userId = owner,
                        objectId = syncable.objectId,
                        isAdd = true,
                    };

                    NetworkMessage queueMessage = NetworkMessage.CreateMessage(BuiltInMessageType.TransformQueue, queueData);
                    if (queueMessage != null) {
                        SendMessage(userId, NetworkChannel.Reliable, queueMessage.GetBytes());
                        gripCount++;
                    }
                }
            }

            // Story progress recorded this level
            int eventCount = SceneEventSync.ReplayEventsTo(userId);

            EntangleLogger.Log($"Replayed world state to {userId}: {spawnCount} spawns, {propCount} props, {gripCount} grips, {eventCount} scene events.");
        }

        public override void BroadcastMessage(NetworkChannel channel, byte[] data) => BroadcastMessageP2P(channel, data);

        // Unique to a server host; allows preventing a message sent to the host being sent back
        public void BroadcastMessageExcept(NetworkChannel channel, byte[] data, long toIgnore) => connectedUsers.ForEach((user) => {
            if (user != toIgnore) {
                SendMessage(user, channel, data);
            }
        });

        public void KickUser(long userId, string userName = null, DisconnectReason reason = DisconnectReason.Kicked) {
            DisconnectMessageData disconnectData = new DisconnectMessageData();
            disconnectData.disconnectReason = (byte)reason;

            NetworkMessage disconnectMsg = NetworkMessage.CreateMessage((byte)BuiltInMessageType.Disconnect, disconnectData);
            byte[] disconnectBytes = disconnectMsg.GetBytes();

            SendMessage(userId, NetworkChannel.Reliable, disconnectBytes);

            if (userName != null)
                EntangleLogger.Log($"Kicked {userName} from the server.");
        }

        public void TeleportTo(long userId) {
            if (PlayerRepresentation.representations.ContainsKey(userId)) {
                PlayerRepresentation rep = PlayerRepresentation.representations[userId];

                PlayerScripts.playerRig.Teleport(rep.repRoot.position);
                PlayerScripts.playerRig.physicsRig.ResetHands(Handedness.BOTH);
            }
        }
    }
}
