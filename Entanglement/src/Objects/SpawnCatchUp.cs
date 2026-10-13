using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using StressLevelZero.Pool;

using Entanglement.Data;
using Entanglement.Network;

namespace Entanglement.Objects
{
    public static class SpawnCatchUp
    {
        static readonly Dictionary<long, HashSet<ushort>> known = new Dictionary<long, HashSet<ushort>>();
        static float nextCheck;
        const float CheckInterval = 2f;

        public static void OnSceneCleanup() {
            known.Clear();
            poses.Clear();
            lastRespawn.Clear();
            heldRespawns.Clear();
            nextCheck = 0f;
        }

        const float RespawnGap = 0.5f;
        static readonly Dictionary<ushort, float> lastRespawn = new Dictionary<ushort, float>();
        static readonly Dictionary<ushort, PooleeSyncable> heldRespawns = new Dictionary<ushort, PooleeSyncable>();
        static readonly List<ushort> dueRespawns = new List<ushort>();

        /// <summary>Host: an object that already has an id came out of its pool again.</summary>
        public static void BroadcastRespawn(PooleeSyncable poolee) {
            if (poolee == null || poolee.id == 0)
                return;

            float now = Time.realtimeSinceStartup;
            float last;
            if (lastRespawn.TryGetValue(poolee.id, out last) && now - last < RespawnGap) {
                heldRespawns[poolee.id] = poolee;
                return;
            }

            lastRespawn[poolee.id] = now;
            heldRespawns.Remove(poolee.id);
            Broadcast(poolee);
        }

        static void SendHeldRespawns() {
            if (heldRespawns.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            dueRespawns.Clear();
            foreach (KeyValuePair<ushort, PooleeSyncable> pair in heldRespawns) {
                float last;
                lastRespawn.TryGetValue(pair.Key, out last);
                if (now - last >= RespawnGap)
                    dueRespawns.Add(pair.Key);
            }

            for (int i = 0; i < dueRespawns.Count; i++) {
                ushort id = dueRespawns[i];
                PooleeSyncable poolee = heldRespawns[id];
                heldRespawns.Remove(id);
                lastRespawn[id] = now;
                try {
                    // Gone back in again: NpcDespawnSync says so, there is nothing to send.
                    if (ObjectSync.Live(poolee) && poolee.id == id && poolee.gameObject.activeInHierarchy)
                        Broadcast(poolee);
                }
                catch { }
            }
        }

        public static void OnUserLeft(long userId) {
            known.Remove(userId);
        }

        static HashSet<ushort> KnownBy(long userId) {
            HashSet<ushort> set;
            if (!known.TryGetValue(userId, out set)) {
                set = new HashSet<ushort>();
                known[userId] = set;
            }
            return set;
        }

        static byte[] BuildMessage(PooleeSyncable poolee) {
            try {
                if (poolee == null || poolee.id == 0 || poolee.pathAddressed)
                    return null;

                Poolee p = poolee.Poolee;
                if (!p || !p.pool)
                    return null;

                string title = SpawnManager.GetPoolTitle(p.pool);
                if (string.IsNullOrEmpty(title))
                    return null;

                SpawnClientMessageData data = new SpawnClientMessageData() {
                    rbCount = poolee.rbCount,
                    spawnId = poolee.id,
                    title = title,
                    transform = new SimplifiedTransform(poolee.transform),
                };

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SpawnClient, data);
                return message?.GetBytes();
            }
            catch { return null; }
        }

        public static void Broadcast(PooleeSyncable poolee) {
            if (Node.activeNode == null)
                return;

            byte[] bytes = BuildMessage(poolee);
            if (bytes == null)
                return;

            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, bytes);

            foreach (long user in Node.activeNode.connectedUsers) {
                KnownBy(user).Add(poolee.id);
                QueuePose(poolee, user);
            }

            // A new life, maybe on a different copy: its config goes out again (NpcConfigSync).
            NpcConfigSync.Forget(poolee.id);
        }

        public static bool SendTo(long userId, PooleeSyncable poolee) {
            if (Node.activeNode == null)
                return false;

            byte[] bytes = BuildMessage(poolee);
            if (bytes == null)
                return false;

            Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, bytes);
            KnownBy(userId).Add(poolee.id);
            QueuePose(poolee, userId);
            NpcConfigSync.SendTo(userId, poolee);

            try {
                StressLevelZero.AI.AIBrain brain = poolee.GetComponentInChildren<StressLevelZero.AI.AIBrain>(true);
                if (brain && NpcStateSync.IsDead(brain))
                    NpcStateSync.SendDeath(userId, poolee.id);
            }
            catch { }
            return true;
        }

        struct PendingPose {
            public PooleeSyncable poolee;
            public long user;
            public float due;
        }

        static readonly List<PendingPose> poses = new List<PendingPose>();
        const float PoseDelay = 0.75f;

        public static void QueuePoseForEveryone(PooleeSyncable poolee) {
            if (Node.activeNode == null || poolee == null)
                return;
            foreach (long user in Node.activeNode.connectedUsers) {
                KnownBy(user).Add(poolee.id);
                QueuePose(poolee, user);
            }
        }

        static void QueuePose(PooleeSyncable poolee, long user) {
            if (poolee == null || poolee.rbCount == 0)
                return;
            poses.Add(new PendingPose { poolee = poolee, user = user, due = Time.realtimeSinceStartup + PoseDelay });
        }

        static void SendDuePoses() {
            if (poses.Count == 0)
                return;
            float now = Time.realtimeSinceStartup;
            for (int i = poses.Count - 1; i >= 0; i--) {
                if (poses[i].due > now)
                    continue;
                PendingPose pose = poses[i];
                poses.RemoveAt(i);
                try { SendPose(pose.poolee, pose.user); }
                catch { }
            }
        }

        static void SendPose(PooleeSyncable poolee, long user) {
            if (Node.activeNode == null || !ObjectSync.Live(poolee) || poolee.transforms == null)
                return;
            if (!poolee.gameObject.activeInHierarchy)
                return;

            TransformSyncBatchData batch = new TransformSyncBatchData();
            for (int i = 0; i < poolee.transforms.Length; i++) {
                TransformSyncable sync = poolee.transforms[i];
                if (!ObjectSync.Live(sync) || sync.objectId == 0)
                    continue;

                batch.entries.Add(new TransformSyncMessageData() {
                    objectId = sync.objectId,
                    simplifiedTransform = new SimplifiedTransform(sync.transform),
                    velocity = Vector3.zero,
                    angularVelocity = Vector3.zero,
                    resting = false,
                });

                if (batch.entries.Count >= TransformSyncBatchMessageHandler.maxEntriesPerMessage) {
                    SendBatch(batch, user);
                    batch.entries.Clear();
                }
            }
            if (batch.entries.Count > 0)
                SendBatch(batch, user);
        }

        static void SendBatch(TransformSyncBatchData batch, long user) {
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformSyncBatch, batch);
            if (message != null)
                Node.activeNode.SendMessage(user, NetworkChannel.Reliable, message.GetBytes());
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || Server.instance == null)
                return;

            SendDuePoses();
            SendHeldRespawns();

            float now = Time.realtimeSinceStartup;
            if (now < nextCheck)
                return;
            nextCheck = now + CheckInterval;

            // Only players whose world replay already ran; anyone still loading gets it there.
            if (Server.instance.replayedUsers.Count == 0)
                return;

            PooleeSyncable[] all = PooleeSyncable._PooleeLookup.Values.ToArray();
            int sent = 0;

            foreach (long user in Server.instance.replayedUsers) {
                HashSet<ushort> set = KnownBy(user);
                for (int i = 0; i < all.Length; i++) {
                    try {
                        PooleeSyncable poolee = all[i];
                        if (!poolee || set.Contains(poolee.id))
                            continue;
                        if (!poolee.gameObject.activeInHierarchy)
                            continue;
                        // A placed NPC reaches a joiner through its recorded bind, not a spawn;
                        // it still needs the pose.
                        if (poolee.pathAddressed) {
                            set.Add(poolee.id);
                            QueuePose(poolee, user);
                            continue;
                        }
                        if (SendTo(user, poolee))
                            sent++;
                    }
                    catch { }
                }
            }

            if (sent > 0)
                NpcTrace.Log("catch-up sent " + sent + " spawn(s) a client did not have yet");
        }
    }
}
