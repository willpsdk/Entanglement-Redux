using System;
using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.AI;
using StressLevelZero.Pool;

using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.Data;
using Entanglement.Representation;

using MelonLoader;

namespace Entanglement.Objects
{
    public static class TurretSync {
        static readonly HashSet<int> registered = new HashSet<int>();

        public static void OnSceneCleanup() {
            registered.Clear();
            placed.Clear();
            resolving.Clear();
            authority.Clear();
            nextReannounce = 0f;
        }

        const float ReannounceEvery = 10f;
        static float nextReannounce;
        static readonly List<PooleeSyncable> placed = new List<PooleeSyncable>();

        public static bool IsPooled(GameObject root) {
            try { return root && root.GetComponent<Poolee>(); }
            catch { return false; }
        }

        static string PlaceKey(GameObject root) {
            Vector3 p = root.transform.position;
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            return "~" + TransformExtensions.PoolBaseName(root.name) + "@"
                + p.x.ToString("F2", inv) + "," + p.y.ToString("F2", inv) + "," + p.z.ToString("F2", inv);
        }

        public static void BindByPlace(GameObject root) {
            if (!root || !Node.isServer || !SteamIntegration.hasLobby)
                return;
            // A pool's warm-up copy stands at the world origin until the pool places it. Bound there it
            // only sends places nobody else can find; it binds when it is spawned for real.
            if (AtOrigin(root))
                return;
            try {
                PooleeSyncable poolee = PooleeSyncable._Cache.Get(root);
                if (poolee == null || poolee.id == 0) {
                    Rigidbody[] bodies = PooleeSyncable.SyncBodies(root);
                    if (bodies.Length == 0 || bodies.Length > 255)
                        return;
                    ushort id = ObjectSync.GetNextObjectIdBlock(bodies.Length);
                    poolee = BindBodies(root, bodies, id, (byte)bodies.Length, poolee);
                }
                poolee.placeKey = PlaceKey(root);
                registered.Add(root.GetInstanceID());
                if (!placed.Contains(poolee))
                    placed.Add(poolee);

                Announce(poolee, false);
                SpawnCatchUp.QueuePoseForEveryone(poolee);
                Entanglement.Patching.TurretEventSync.SendCurrentState(root);
                NpcTrace.Log("turret " + root.name + " #" + poolee.id + " bodies=" + poolee.rbCount + " is synced by place " + poolee.placeKey);
            }
            catch (System.Exception e) { EntangleLogger.Warn("[Turret] Could not sync " + root.name + ": " + e.Message); }
        }

        static bool AtOrigin(GameObject root) {
            try { return FastMath.SqrMag(root.transform.position) < 0.0001f; }
            catch { return false; }
        }

        // "~name@x,y,z|bodies|state": a client whose turret only spawns later still starts it in
        // the host's state (dead, alert) when it binds.
        static string BindPath(PooleeSyncable poolee) {
            string path = poolee.placeKey + "|" + poolee.rbCount;
            try {
                EnemyTurret turret = poolee.GetComponentInChildren<EnemyTurret>(true);
                if (turret)
                    path += "|" + (int)turret.state;
            }
            catch { }
            return path;
        }

        static void Announce(PooleeSyncable poolee, bool record) {
            if (poolee == null || poolee.id == 0 || string.IsNullOrEmpty(poolee.placeKey))
                return;
            SceneEventSync.SendEventPath(SceneEventType.NpcBind, BindPath(poolee), poolee.id, record);
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || !Node.isServer || placed.Count == 0)
                return;
            if (Entanglement.Representation.PlayerRepresentation.RemoteCount == 0)
                return;
            if (Time.realtimeSinceStartup < nextReannounce)
                return;
            nextReannounce = Time.realtimeSinceStartup + ReannounceEvery;

            for (int i = placed.Count - 1; i >= 0; i--) {
                PooleeSyncable poolee = placed[i];
                try {
                    if (!ObjectSync.Live(poolee)) {
                        placed.RemoveAt(i);
                        continue;
                    }
                    if (poolee.gameObject.activeInHierarchy)
                        Announce(poolee, false);
                }
                catch { placed.RemoveAt(i); }
            }
        }

        // The same body numbering on both ends: host-owned syncables with ids from the first one.
        static PooleeSyncable BindBodies(GameObject go, Rigidbody[] bodies, ushort id, byte count, PooleeSyncable existing) {
            long host = SteamIntegration.lobbyOwnerId;
            for (ushort i = 0; i < count; i++) {
                Rigidbody rb = bodies[i];
                if (!rb)
                    continue;
                ushort thisId = (ushort)(id + i);
                TransformSyncable sync = TransformSyncable.cache.GetOrAdd(rb.gameObject);
                if (sync) {
                    ObjectSync.MoveSyncable(sync, thisId);
                    sync.ClearOwner();
                    sync.TrySetStale(host);
                }
                else
                    TransformSyncable.CreateSync(host, ComponentCacheExtensions.m_RigidbodyCache.GetOrAdd(rb.gameObject), thisId);
                ObjectSync.lastId = thisId;
            }

            PooleeSyncable poolee = existing;
            if (poolee == null)
                poolee = PooleeSyncable.Attach(go);
            else if (poolee.id != 0 && poolee.id != id) {
                PooleeSyncable stale;
                if (PooleeSyncable._PooleeLookup.TryGetValue(poolee.id, out stale) && stale == poolee)
                    PooleeSyncable._PooleeLookup.Remove(poolee.id);
            }
            poolee.pathAddressed = true;
            poolee.AssignId(id, count);
            poolee.BindTransforms(go.GetComponentsInChildren<TransformSyncable>(true));
            return poolee;
        }

        // Binds still looking for their turret, so the host's repeats don't start more.
        static readonly HashSet<string> resolving = new HashSet<string>();

        /// <summary>Client: the host's turret "~name@x,y,z" is numbered from id; bind ours to it.
        /// state is the host turret's, or -1 when the host didn't say.</summary>
        public static void ApplyPlaceBind(ushort id, string key, int count, int state) {
            if (count <= 0 || count > 255 || !resolving.Add(key))
                return;
            MelonCoroutines.Start(CoApplyPlaceBind(id, key, count, state));
        }

        static System.Collections.IEnumerator CoApplyPlaceBind(ushort id, string key, int count, int state) {
            try {
                System.Collections.IEnumerator inner = CoResolvePlace(id, key, count, state);
                while (inner.MoveNext())
                    yield return inner.Current;
            }
            finally {
                resolving.Remove(key);
            }
        }

        static System.Collections.IEnumerator CoResolvePlace(ushort id, string key, int count, int state) {
            ushort scene = SceneEventSync.CurrentSceneTag();
            float waited = 0f;
            Transform root = key.GetFromFullPath();

            // Its area may not have started here yet; the host says it again every few seconds too.
            while (!root && waited < 15f) {
                float step = 0f;
                while (step < 0.5f) {
                    yield return null;
                    if (!SceneLoader.loading)
                        step += Time.unscaledDeltaTime;
                }
                waited += step;
                if (SceneEventSync.CurrentSceneTag() != scene)
                    yield break;
                root = key.GetFromFullPath();
            }
            if (!root)
                yield break;

            try {
                GameObject go = root.gameObject;
                PooleeSyncable existing = PooleeSyncable._Cache.Get(go);
                if (existing != null && existing.id == id) {
                    existing.placeKey = key;
                }
                else {
                    Rigidbody[] bodies = PooleeSyncable.SyncBodies(go);
                    int bind = Math.Min(bodies.Length, count);
                    if (bind <= 0) {
                        EntangleLogger.Warn("[Turret] " + go.name + " has no bodies here, so it stays local");
                    }
                    else {
                        if (bodies.Length != count)
                            EntangleLogger.Warn("[Turret] " + go.name + " has " + bodies.Length + " bodies here but " + count
                                + " on the host; binding the first " + bind);
                        PooleeSyncable poolee = BindBodies(go, bodies, id, (byte)bind, existing);
                        poolee.placeKey = key;
                        NpcTrace.Log("turret " + go.name + " #" + id + " follows the host's turret at " + key);
                    }
                }
                DisableRemoteAi(go);
                ApplyHostState(go, state);
            }
            catch (System.Exception e) { EntangleLogger.Debug("[Turret] Could not bind " + key + ": " + e.Message); }
        }

        static void ApplyHostState(GameObject go, int state) {
            if (state < 0)
                return;
            try {
                EnemyTurret turret = go.GetComponentInChildren<EnemyTurret>(true);
                if (!turret || (int)turret.state == state)
                    return;
                bool dead = state == (int)EnemyTurret.TurretStates.DEAD;
                Entanglement.Patching.TurretEventSync.Apply(dead ? SceneEventType.TurretKill : SceneEventType.TurretState,
                    (ushort)state, go.transform);
            }
            catch { }
        }

        /// <summary>Client: a turret this level spawned here. It stays, with its AI off.</summary>
        public static void KeepLocal(GameObject root) {
            if (!root)
                return;
            registered.Add(root.GetInstanceID());
            DisableRemoteAi(root);
        }

        public static void Unbind(GameObject root) {
            if (!root)
                return;
            try {
                TransformSyncable[] syncs = root.GetComponentsInChildren<TransformSyncable>(true);
                for (int i = 0; i < syncs.Length; i++) {
                    TransformSyncable sync = syncs[i];
                    if (!sync)
                        continue;
                    Syncable current;
                    if (sync.objectId != 0 && ObjectSync.syncedObjects.TryGetValue(sync.objectId, out current) && ReferenceEquals(current, sync))
                        ObjectSync.syncedObjects.Remove(sync.objectId);
                    sync.Cleanup();
                }

                PooleeSyncable poolee = PooleeSyncable._Cache.Get(root);
                if (poolee != null) {
                    PooleeSyncable current;
                    if (poolee.id != 0 && PooleeSyncable._PooleeLookup.TryGetValue(poolee.id, out current) && current == poolee)
                        PooleeSyncable._PooleeLookup.Remove(poolee.id);
                    NpcTrace.Log("turret " + root.name + " came out of the pool here still bound to #" + poolee.id + ", unbound it");
                    poolee.id = 0;
                    poolee.placeKey = null;
                    PooleeSyncable._Cache.Remove(root);
                    UnityEngine.Object.Destroy(poolee);
                }
            }
            catch { }
        }

        /// <summary>Host: a pooled turret came out; bind it once it stands where it will stay.</summary>
        public static void BindLater(GameObject root) {
            MelonCoroutines.Start(CoBindLater(root));
        }

        static System.Collections.IEnumerator CoBindLater(GameObject root) {
            while (SceneLoader.loading)
                yield return null;
            for (int i = 0; i < 3; i++) {
                yield return null;
                if (!root)
                    yield break;
            }
            if (root && root.activeInHierarchy)
                BindByPlace(root);
        }

        public static void OnSceneReady() {
            if (!SteamIntegration.hasLobby)
                return;
            // Solo lobby: still register host sync for late joiners, but never disable AI on host
            MelonCoroutines.Start(CoRegister());
        }

        static System.Collections.IEnumerator CoRegister() {
            for (int i = 0; i < 5; i++)
                yield return null;

            if (!SteamIntegration.hasLobby)
                yield break;

            EnemyTurret[] turrets = null;
            try { turrets = UnityEngine.Object.FindObjectsOfType<EnemyTurret>(); }
            catch { yield break; }

            if (turrets == null)
                yield break;

            for (int i = 0; i < turrets.Length; i++) {
                EnemyTurret turret = turrets[i];
                if (!turret)
                    continue;

                GameObject root = turret.gameObject;
                try {
                    Poolee poolee = turret.GetComponentInParent<Poolee>();
                    if (poolee)
                        root = poolee.gameObject;
                    else if (turret.transform.root)
                        root = turret.transform.root.gameObject;
                }
                catch { }

                int instanceId = root.GetInstanceID();
                if (!registered.Add(instanceId))
                    continue;

                if (Node.isServer)
                    EnsureHostSync(root);
                else
                    DisableRemoteAi(root);
            }
        }

        public static void EnsureRegistered(EnemyTurret turret) {
            if (!SteamIntegration.hasLobby || !Node.isServer || !turret)
                return;

            try {
                GameObject root = ResolveTurretRoot(turret);
                if (root == null || PooleeSyncable._Cache.Get(root))
                    return;

                registered.Add(root.GetInstanceID());
                EnsureHostSync(root);
            }
            catch { }
        }

        public static GameObject ResolveTurretRoot(EnemyTurret turret) {
            if (!turret)
                return null;

            GameObject root = turret.gameObject;
            try {
                Poolee poolee = turret.GetComponentInParent<Poolee>();
                if (poolee)
                    root = poolee.gameObject;
                else if (turret.transform.root)
                    root = turret.transform.root.gameObject;
            }
            catch { }

            return root;
        }

        static void EnsureHostSync(GameObject root) {
            if (!root)
                return;

            if (PooleeSyncable._Cache.Get(root))
                return;

            // Pooled turrets: each machine has its own, bound by place (BindByPlace).
            if (IsPooled(root)) {
                BindByPlace(root);
                return;
            }

            TransformSyncable[] existing = TransformSyncable.Real(root.GetComponentsInChildren<TransformSyncable>(true));
            if (existing != null && existing.Length > 0) {
                ushort firstId = 0;
                bool haveId = false;
                for (int i = 0; i < existing.Length; i++) {
                    if (!existing[i] || existing[i].objectId == 0)
                        continue;
                    if (!haveId || existing[i].objectId < firstId) {
                        firstId = existing[i].objectId;
                        haveId = true;
                    }
                }

                if (haveId) {
                    var existingPoolee = PooleeSyncable.Attach(root);
                    existingPoolee.pathAddressed = true;
                    existingPoolee.AssignId(firstId, 0);
                    existingPoolee.BindTransforms(existing);
                    return;
                }
            }

            Rigidbody[] rbs = root.GetComponentsInChildren<Rigidbody>(true);
            if (rbs == null || rbs.Length == 0)
                return;

            ushort firstNewId = ObjectSync.GetNextObjectIdBlock(rbs.Length);
            for (ushort i = 0; i < rbs.Length; i++) {
                Rigidbody rb = rbs[i];
                if (!rb)
                    continue;
                if (TransformSyncable.cache.Get(rb.gameObject))
                    continue;

                ushort thisId = (ushort)(firstNewId + i);
                TransformSyncable.CreateSync(SteamIntegration.lobbyOwnerId, ComponentCacheExtensions.m_RigidbodyCache.GetOrAdd(rb.gameObject), thisId);
                ObjectSync.lastId = thisId;

                TransformCreateMessageData createData = new TransformCreateMessageData() {
                    ownerId = SteamIntegration.lobbyOwnerId,
                    objectId = thisId,
                    callbackIndex = 0,
                    spawnIndex = -1,
                    spawnTime = -1f,
                    enqueueOwner = false,
                    objectPath = rb.transform.GetFullPath(null),
                };
                NetworkMessage createMessage = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCreate, createData);
                if (createMessage != null && Node.activeNode != null)
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, createMessage.GetBytes());
            }

            TransformSyncable[] bound = TransformSyncable.Real(root.GetComponentsInChildren<TransformSyncable>(true));
            if (bound == null || bound.Length == 0)
                return;

            ushort pooleeId = firstNewId;
            for (int i = 0; i < bound.Length; i++) {
                if (bound[i] && bound[i].objectId != 0 && bound[i].objectId < pooleeId)
                    pooleeId = bound[i].objectId;
            }

            var pooleeSyncable = PooleeSyncable.Attach(root);
            pooleeSyncable.pathAddressed = true;
            pooleeSyncable.AssignId(pooleeId, 0);
            pooleeSyncable.BindTransforms(bound);
        }

        /// <summary>For the solo turret check: each turret, who runs it, and who is in its range.</summary>
        public static string Describe() {
            EnemyTurret[] turrets;
            try { turrets = UnityEngine.Object.FindObjectsOfType<EnemyTurret>(); }
            catch { return "Could not look for turrets."; }
            if (turrets == null || turrets.Length == 0)
                return "No turrets in this level.";

            int synced = 0, running = 0, dummyInRange = 0;
            StressLevelZero.AI.TriggerRefProxy local = Entanglement.Data.PlayerScripts.LocalProxy();
            for (int i = 0; i < turrets.Length; i++) {
                EnemyTurret turret = turrets[i];
                try {
                    if (!turret)
                        continue;
                    PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(turret.transform);
                    bool isSynced = poolee != null && poolee.id != 0;
                    if (isSynced)
                        synced++;
                    if (turret.enabled)
                        running++;
                    float reach = Reach(turret);
                    Vector3 at = turret.transform.position;
                    bool you = local != null && (local.transform.position - at).sqrMagnitude <= reach * reach;
                    float sqr;
                    bool dummy = InRange(PlayerRepresentation.debugRepresentation, at, out sqr) && sqr <= reach * reach;
                    if (dummy)
                        dummyInRange++;
                    EntangleLogger.Log("[Turret] " + turret.name + (isSynced ? " #" + poolee.id + " owner " + poolee.CurrentOwner() : " not synced")
                        + ", AI " + (turret.enabled ? "on" : "off") + " here, state " + turret.state + ", range " + reach.ToString("F0") + "m"
                        + (you ? ", you are in range" : "") + (dummy ? ", the dummy is in range" : ""));
                }
                catch { }
            }
            return turrets.Length + " turret(s): " + synced + " synced, AI on here for " + running
                + (dummyInRange > 0 ? ", " + dummyInRange + " with the dummy in range" : "") + ". Details in the log.";
        }

        /// <summary>Its AI off here until this machine runs it (TickAuthority).</summary>
        public static void DisableRemoteAi(GameObject root) {
            if (!root)
                return;
            try {
                EnemyTurret[] parts = root.GetComponentsInChildren<EnemyTurret>(true);
                for (int i = 0; parts != null && i < parts.Length; i++) {
                    if (parts[i])
                        StopHere(parts[i], AuthorityOf(parts[i]));
                }
            }
            catch { }
        }

        // ---- who runs a turret ------------------------------------------------------------------

        // A turret's AI only ever sees the player on its own machine: its sight check skips any
        // target the game's TriggerManager does not know, and other players are not the game's
        // targets. So it runs on the machine of a player it can see. Whoever runs it hands it on
        // once another player has been in its range a moment while its own player is not. Its
        // shots then hit that player's real body; everyone else sees it aim and fire by its events.
        const float HandoffHold = 0.75f;
        const float HandoffTenure = 3f;
        const float RangeSlack = 1f;

        class Authority {
            public bool aiOffByUs;
            public ConfigurableJoint[] aimJoints;
            public JointDrive[] savedAim;
            public long candidate;
            public float candidateSince;
            public float nextDummyReport;
        }

        static readonly Dictionary<int, Authority> authority = new Dictionary<int, Authority>();

        static Authority AuthorityOf(EnemyTurret turret) {
            int key = turret.GetInstanceID();
            Authority a;
            if (!authority.TryGetValue(key, out a)) {
                a = new Authority();
                authority[key] = a;
            }
            return a;
        }

        /// <summary>A synced turret's tick: its AI on where it runs, off elsewhere, and handed on when it should be.</summary>
        public static void TickAuthority(PooleeSyncable poolee, EnemyTurret turret, bool owned, bool held) {
            Authority a = AuthorityOf(turret);
            if (!owned) {
                StopHere(turret, a);
                a.candidate = 0L;
                return;
            }
            RunHere(turret, a);
            if (held || (PlayerRepresentation.RemoteCount == 0 && PlayerRepresentation.debugRepresentation == null)) {
                a.candidate = 0L;
                return;
            }
            ConsiderHandoff(poolee, turret, a);
        }

        // Its AI off: another machine runs it. The aim joints go slack so the pose coming from
        // there turns it; what they held is kept to give back.
        static void StopHere(EnemyTurret turret, Authority a) {
            try {
                if (turret.enabled) {
                    turret.enabled = false;
                    a.aiOffByUs = true;
                }
            }
            catch { }

            if (a.savedAim != null)
                return;
            ConfigurableJoint[] joints = new ConfigurableJoint[2];
            try { joints[0] = turret.bodyJnt; } catch { }
            try { joints[1] = turret.gunJnt; } catch { }
            JointDrive[] saved = new JointDrive[joints.Length * 3];
            JointDrive off = new JointDrive();
            for (int i = 0; i < joints.Length; i++) {
                ConfigurableJoint joint = joints[i];
                if (!joint)
                    continue;
                try {
                    saved[i * 3] = joint.slerpDrive;
                    saved[i * 3 + 1] = joint.angularXDrive;
                    saved[i * 3 + 2] = joint.angularYZDrive;
                    joint.slerpDrive = off;
                    joint.angularXDrive = off;
                    joint.angularYZDrive = off;
                }
                catch { }
            }
            a.aimJoints = joints;
            a.savedAim = saved;
        }

        // Runs here: its aim joints back as they were, and its AI back on (OnEnable starts its
        // checks again). Only what we switched off is switched back on.
        static void RunHere(EnemyTurret turret, Authority a) {
            if (a.savedAim != null) {
                for (int i = 0; i < a.aimJoints.Length; i++) {
                    ConfigurableJoint joint = a.aimJoints[i];
                    if (!joint)
                        continue;
                    try {
                        joint.slerpDrive = a.savedAim[i * 3];
                        joint.angularXDrive = a.savedAim[i * 3 + 1];
                        joint.angularYZDrive = a.savedAim[i * 3 + 2];
                    }
                    catch { }
                }
                a.savedAim = null;
                a.aimJoints = null;
            }
            if (!a.aiOffByUs)
                return;
            a.aiOffByUs = false;
            try {
                if (!turret.enabled)
                    turret.enabled = true;
            }
            catch { }
        }

        static void ConsiderHandoff(PooleeSyncable poolee, EnemyTurret turret, Authority a) {
            long wanted = 0L;
            try {
                EnemyTurret.TurretStates state = turret.state;
                // Dead, asleep or knocked over: there is no one it is looking at.
                if (state == EnemyTurret.TurretStates.DEAD || state == EnemyTurret.TurretStates.SLEEP
                    || state == EnemyTurret.TurretStates.TIPPED) {
                    a.candidate = 0L;
                    return;
                }
                float reach = Reach(turret);
                Vector3 at = turret.transform.position;
                StressLevelZero.AI.TriggerRefProxy local = Entanglement.Data.PlayerScripts.LocalProxy();
                if (local != null && (local.transform.position - at).sqrMagnitude <= reach * reach) {
                    a.candidate = 0L;
                    return;
                }
                wanted = NearestInRange(at, reach);
            }
            catch { wanted = 0L; }

            if (wanted == 0L || Time.time - poolee.LastOwnerChange < HandoffTenure) {
                a.candidate = 0L;
                return;
            }
            if (wanted != a.candidate) {
                a.candidate = wanted;
                a.candidateSince = Time.time;
                return;
            }
            if (Time.time - a.candidateSince < HandoffHold)
                return;
            a.candidate = 0L;

            if (wanted == PlayerRepresentation.TesterRepId) {
                if (Time.time >= a.nextDummyReport) {
                    a.nextDummyReport = Time.time + 10f;
                    EntangleLogger.Debug("[Turret] " + turret.name + " #" + poolee.id + " has the dummy in range and not you; a real player would run it now");
                }
                return;
            }

            poolee.HandTo(wanted);
            NpcTrace.Log("turret " + turret.name + " #" + poolee.id + " handed to " + wanted + ", who is in its range");
        }

        // How far it looks: the larger of its vision and its sense trigger, and a little slack.
        static float Reach(EnemyTurret turret) {
            float reach = turret.visionRadius;
            try {
                if (turret.trigger)
                    reach = Mathf.Max(reach, turret.trigger.radius);
            }
            catch { }
            return reach + RangeSlack;
        }

        static long NearestInRange(Vector3 at, float reach) {
            long best = 0L;
            float bestSqr = reach * reach;
            foreach (KeyValuePair<long, PlayerRepresentation> pair in PlayerRepresentation.representations) {
                long id = pair.Key;
                float sqr;
                if (InRange(pair.Value, at, out sqr) && sqr <= bestSqr) {
                    best = id;
                    bestSqr = sqr;
                }
            }
            float dummySqr;
            if (best == 0L && InRange(PlayerRepresentation.debugRepresentation, at, out dummySqr) && dummySqr <= reach * reach)
                best = PlayerRepresentation.TesterRepId;
            return best;
        }

        static bool InRange(PlayerRepresentation rep, Vector3 at, out float sqr) {
            sqr = float.MaxValue;
            if (rep == null)
                return false;
            try {
                StressLevelZero.AI.TriggerRefProxy proxy = rep.EnemyProxy;
                if (proxy == null)
                    return false;
                sqr = (proxy.transform.position - at).sqrMagnitude;
                return true;
            }
            catch { return false; }
        }

        public static int ReplayTo(long userId) {
            if (!SteamIntegration.isHost || Node.activeNode == null)
                return 0;

            EnemyTurret[] turrets;
            try { turrets = UnityEngine.Object.FindObjectsOfType<EnemyTurret>(); }
            catch { return 0; }

            if (turrets == null)
                return 0;

            int sent = 0;
            for (int i = 0; i < turrets.Length; i++) {
                EnemyTurret turret = turrets[i];
                if (!turret)
                    continue;

                try {
                    PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(turret.transform);
                    if (poolee == null || (poolee.id == 0 && !poolee.pathAddressed)) {
                        EnsureRegistered(turret);
                        poolee = SceneEventSync.FindPooleeSyncable(turret.transform);
                    }
                    if (poolee == null || (poolee.id == 0 && !poolee.pathAddressed))
                        continue;
                    Transform addressed = poolee.transform;

                    bool byPlace = !string.IsNullOrEmpty(poolee.placeKey);
                    if (byPlace && SendTo(userId, SceneEventType.NpcBind, poolee.id, BindPath(poolee)))
                        sent++;

                    bool dead = byPlace && turret.state == EnemyTurret.TurretStates.DEAD;
                    if (SendTo(userId, dead ? SceneEventType.TurretKill : SceneEventType.TurretState,
                            dead ? (ushort)0 : (ushort)turret.state, SceneEventSync.BuildEventPath(addressed)))
                        sent++;
                }
                catch { }
            }

            return sent;
        }

        static bool SendTo(long userId, SceneEventType type, ushort arg, string objectPath) {
            SceneEventMessageData data = new SceneEventMessageData() {
                eventType = type,
                arg = arg,
                objectPath = objectPath,
            };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, data);
            if (message == null)
                return false;
            Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
            return true;
        }

        public static bool IsTurret(GameObject go) {
            if (!go)
                return false;
            try {
                if (go.GetComponentInChildren<EnemyTurret>(true))
                    return true;
            }
            catch { }
            try {
                if (go.GetComponentInChildren<Enemy_Turret>(true))
                    return true;
            }
            catch { }
            try {
                if (go.GetComponentInChildren<Arena_Turret>(true))
                    return true;
            }
            catch { }
            return false;
        }
    }
}
