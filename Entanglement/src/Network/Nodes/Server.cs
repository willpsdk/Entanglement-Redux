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
        public static bool clientTeleport = true;
        public static ServerVisibility visibility = ServerVisibility.Public;

        // Hard locked settings
        public const byte serverMinimum = 1;
        public const byte serverCapacity = 250; // Steam lobbies max out at 250 members

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

        public static bool StopHosting() {
            if (instance != null) {
                instance.Shutdown();
                return true;
            }

            if (!SteamIntegration.hasLobby)
                return false;

            if (!SteamIntegration.isHost) {
                EntangleLogger.Warn("StopHosting ignored, this peer is not the lobby owner.");
                return false;
            }

            EntangleLogger.Warn("StopHosting: no Server.instance, leaving orphan Steam lobby.");
            try { SteamMatchmaking.LeaveLobby(SteamIntegration.lobby); } catch { }
            SteamIntegration.lobby = CSteamID.Nil;
            SteamIntegration.DefaultRichPresence();
            activeNode = Client.instance;
            try { Client.instance?.CleanData(); } catch { }
            EntangleNotif.LeftServer();
            return true;
        }

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
            // User hit Stop Server (or Start raced) before Steam answered — abandon the new lobby.
            if (instance != this) {
                if (!bIOFailure && result.m_eResult == EResult.k_EResultOK)
                    try { SteamMatchmaking.LeaveLobby(new CSteamID(result.m_ulSteamIDLobby)); } catch { }
                return;
            }

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
            // Which build exactly; joiners compare it with theirs (Client.CheckHostBuild).
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, "build", EntanglementVersion.BuildTag);
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, "host_name", SteamIntegration.currentUserName);
            SteamMatchmaking.SetLobbyJoinable(SteamIntegration.lobby, !isLocked);
            ModDependency.PublishHostRequirements();

            RegisterLobbyCallbacks();

            SteamIntegration.UpdateActivity();

            if (!SceneLoader.loading) {
                try { Entanglement.Objects.TurretSync.OnSceneReady(); } catch { }
                try { Entanglement.Objects.NpcPoolGuard.OnSceneReady(); } catch { }
                try { Entanglement.Objects.LargePropSync.OnSceneReady(); } catch { }
            }

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
            try {
                if (lobbyCreatedResult != null) {
                    lobbyCreatedResult.Dispose();
                    lobbyCreatedResult = null;
                }
            }
            catch { }

            CloseLobby();
            SteamIntegration.DefaultRichPresence();
            EntangleNotif.LeftServer();

            if (instance == this)
                instance = null;
            activeNode = Client.instance;
            EntangleLogger.Log("Server stopped.");
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

            // Seen again without a leave in between (a quick rejoin): same number, same entry.
            userBeats[userId] = 0f;
        }

        public override void UserDisconnectEvent(long userId) {
            SteamIntegration.UpdateActivity();

            userBeats.Remove(userId);
            replayedUsers.Remove(userId);
        }

        public readonly HashSet<long> replayedUsers = new HashSet<long>();

        // Replays the current world state to a late joiner once their scene is ready
        public void ReplayWorldStateTo(long userId) {
            if (!replayedUsers.Add(userId))
                return;
            MelonCoroutines.Start(CoReplayWorldState(userId));
        }

        const int ReplayPerFrame = 12;

        System.Collections.IEnumerator CoReplayWorldState(long userId) {
            ushort scene = SceneEventSync.CurrentSceneTag();
            int spawnCount = 0, propCount = 0, gripCount = 0, sinceYield = 0;

            foreach (PooleeSyncable poolee in PooleeSyncable._PooleeLookup.Values.ToArray()) {
                if (++sinceYield >= ReplayPerFrame) {
                    sinceYield = 0;
                    yield return null;
                    if (!ReplayStillWanted(userId, scene))
                        yield break;
                }
                try {
                    if (!poolee || !poolee.gameObject || !poolee.gameObject.activeInHierarchy)
                        continue;

                    if (Entanglement.Objects.SpawnCatchUp.SendTo(userId, poolee))
                        spawnCount++;
                }
                catch { }
            }

            foreach (Syncable syncable in ObjectSync.syncedObjects.Values.ToArray()) {
                if (++sinceYield >= ReplayPerFrame) {
                    sinceYield = 0;
                    yield return null;
                    if (!ReplayStillWanted(userId, scene))
                        yield break;
                }
                if (ReplayProp(userId, syncable))
                    propCount++;
            }

            if (!ReplayStillWanted(userId, scene))
                yield break;

            // Active grip ownership, so a currently-held object stays with its holder on the joiner
            foreach (Syncable syncable in ObjectSync.syncedObjects.Values.ToArray()) {
                try {
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
                catch { }
            }

            // Host-elected claw prey, so a joiner's BossClawAi agrees from its first frame
            Entanglement.Objects.BossClawSync.ReplayTo(userId);

            // Living NPC condition, so a joiner does not see a room of untouched enemies
            int npcCount = Entanglement.Objects.NpcStateSync.ReplayTo(userId);

            // What kind of enemy each one is, and its size (placed ones don't come as spawns)
            Entanglement.Objects.NpcConfigSync.ReplayTo(userId);

            // Story progress recorded this level
            int eventCount = SceneEventSync.ReplayEventsTo(userId);

            int turretCount = Entanglement.Objects.TurretSync.ReplayTo(userId);
            int monoMatCount = Entanglement.Patching.MonoMatSync.ReplayTo(userId);

            EntangleLogger.Debug($"Replayed world state to {userId}: {spawnCount} spawns, {propCount} props, "
                + $"{gripCount} grips, {eventCount} scene events, {npcCount} NPCs, {turretCount} turrets, "
                + $"{monoMatCount} MonoMats.");
        }

        // Still in the lobby, and still this level (a level change replays from scratch).
        bool ReplayStillWanted(long userId, ushort scene) {
            return instance == this && connectedUsers.Contains(userId) && replayedUsers.Contains(userId)
                && SceneEventSync.CurrentSceneTag() == scene;
        }

        bool ReplayProp(long userId, Syncable syncable) {
            try {
                TransformSyncable sync = syncable as TransformSyncable;
                if (!sync || !sync.transform)
                    return false;

                PooleeSyncable owningPoolee = SceneEventSync.FindPooleeSyncable(sync.transform);
                if (owningPoolee && !owningPoolee.pathAddressed)
                    return false;
                if (owningPoolee && !string.IsNullOrEmpty(owningPoolee.placeKey))
                    return false;

                if (BreakPieces.IsPiece(sync.transform))
                    return false;

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
                    return false;

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

                return true;
            }
            catch { return false; }
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

        // The host's setting, or what the host published for everyone else. A lobby that hasn't
        // said yet counts as allowed.
        public static bool ClientTeleportAllowed {
            get {
                if (!SteamIntegration.hasLobby || SteamIntegration.isHost)
                    return clientTeleport;
                try { return SteamMatchmaking.GetLobbyData(SteamIntegration.lobby, "teleport") != "0"; }
                catch { return true; }
            }
        }

        // Null when it worked, otherwise what to tell the player.
        public static string TeleportTo(long userId) {
            if (!SteamIntegration.isHost && !ClientTeleportAllowed)
                return "The host has turned off teleporting.";

            PlayerRepresentation rep;
            if (!PlayerRepresentation.representations.TryGetValue(userId, out rep) || rep == null || !rep.repRoot)
                return "They aren't here.";
            if (rep.Parked)
                return "They're still loading in.";
            if (rep.Dead)
                return "They're respawning.";
            if (rep.Hidden)
                return "They're changing levels.";
            if (!PlayerScripts.playerRig)
                return "Can't teleport right now.";

            Vector3 facing;
            bool beside;
            Vector3 landing = LandingBeside(rep, out facing, out beside);

            // Landing that close would otherwise throw us apart.
            rep.GraceAfterTeleport();
            PlayerScripts.playerRig.Teleport(landing, facing, true);
            PlayerScripts.playerRig.physicsRig.ResetHands(Handedness.BOTH);
            EntangleLogger.Debug("[Teleport] To " + rep.playerName + " at " + landing
                + (beside ? " beside them" : ", no room beside them so on top of them"));
            return null;
        }

        const float LandDistance = 1f;
        const float LandStep = 0.6f;          // how far above or below their feet the floor may be
        const int LandMask = ~(1 << 2);       // everything but Ignore Raycast
        static readonly float[] LandAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        static readonly UnhollowerBaseLib.Il2CppStructArray<RaycastHit> landHits = new UnhollowerBaseLib.Il2CppStructArray<RaycastHit>(16);
        static readonly UnhollowerBaseLib.Il2CppReferenceArray<Collider> landRoom = new UnhollowerBaseLib.Il2CppReferenceArray<Collider>(32);

        // In front of them if there's floor and room, otherwise round them; where they stand if nowhere.
        static Vector3 LandingBeside(PlayerRepresentation rep, out Vector3 facing, out bool beside) {
            Vector3 feet = rep.repRoot.position;
            Transform headBone = rep.repTransforms[0];
            Vector3 head = headBone ? headBone.position : feet + Vector3.up * 1.6f;
            Vector3 forward = headBone ? headBone.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();

            for (int i = 0; i < LandAngles.Length; i++) {
                Vector3 dir = Quaternion.Euler(0f, LandAngles[i], 0f) * forward;
                Vector3 spot;
                bool open;
                try { open = OpenSpot(feet + dir * LandDistance, feet, head, out spot); }
                catch { open = false; spot = feet; }
                if (!open)
                    continue;
                facing = -dir;
                beside = true;
                return spot;
            }

            facing = forward;
            beside = false;
            return feet;
        }

        static bool OpenSpot(Vector3 around, Vector3 theirFeet, Vector3 theirHead, out Vector3 spot) {
            spot = around;

            Vector3 from = new Vector3(around.x, theirHead.y, around.z);
            int count = Physics.RaycastNonAlloc(from, Vector3.down, landHits, (theirHead.y - theirFeet.y) + LandStep, LandMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++) {
                RaycastHit hit = landHits[i];
                if (hit.distance >= nearest || !InTheWay(hit.collider))
                    continue;
                nearest = hit.distance;
                spot = hit.point;
            }
            if (nearest == float.MaxValue || Mathf.Abs(spot.y - theirFeet.y) > LandStep)
                return false;

            // They can see it: no wall between us.
            Vector3 ours = spot + Vector3.up * 1.5f;
            Vector3 toOurs = ours - theirHead;
            float gap = toOurs.magnitude;
            if (gap > 0.01f) {
                count = Physics.RaycastNonAlloc(theirHead, toOurs / gap, landHits, gap, LandMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++) {
                    if (InTheWay(landHits[i].collider))
                        return false;
                }
            }

            count = Physics.OverlapCapsuleNonAlloc(spot + Vector3.up * 0.45f, spot + Vector3.up * 1.55f, 0.25f, landRoom, LandMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) {
                if (InTheWay(landRoom[i]))
                    return false;
            }
            return true;
        }

        // Loose props get pushed aside; us, other players and their ragdolls don't count.
        static bool InTheWay(Collider collider) {
            if (!collider || collider.isTrigger)
                return false;
            Rigidbody body = collider.attachedRigidbody;
            if (body != null && !body.isKinematic)
                return false;
            Transform root = collider.transform.root;
            if (root && (root.name.StartsWith("PlayerRep.") || root.name.StartsWith("Ragdoll.")))
                return false;
            if (PlayerScripts.playerRig && collider.transform.IsChildOf(PlayerScripts.playerRig.transform))
                return false;
            return true;
        }
    }
}
