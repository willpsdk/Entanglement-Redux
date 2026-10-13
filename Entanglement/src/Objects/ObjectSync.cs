using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnhollowerRuntimeLib;

using UnityEngine;

using StressLevelZero.Interaction;
using StressLevelZero.Pool;
using StressLevelZero.Props.Weapons;
using StressLevelZero.Combat;
using StressLevelZero.Data;

using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.Data;
using Entanglement.Patching;

using MelonLoader;

namespace Entanglement.Objects
{
    public static class ObjectSync {
        public static Dictionary<Pool, List<Poolee>> poolPairs = new Dictionary<Pool, List<Poolee>>(new UnityComparer());

        public static Dictionary<ushort, Syncable> syncedObjects = new Dictionary<ushort, Syncable>(new UnityComparer());
        public static List<Syncable> queuedSyncs = new List<Syncable>();
        public static ushort lastId = 0;

        public static void OnCleanup() {
            // Grant numbers belong to this level's spawn ids (and this lobby's host).
            NpcOwnership.Clear();
            // Wipe NetworkEntity stack for this scene's spawn ids, then rebind living player entities.
            Entanglement.Entities.NetworkProp.Clear();
            Entanglement.Entities.NpcEntity.Clear();
            Entanglement.Entities.NetworkPlayer.Clear();
            Entanglement.Entities.NetworkEntityManager.Clear();
            try {
                foreach (Entanglement.Representation.PlayerRepresentation rep in Entanglement.Representation.PlayerRepresentation.representations.Values) {
                    if (rep != null)
                        Entanglement.Entities.NetworkPlayer.Register(rep);
                }
                if (Entanglement.Representation.PlayerRepresentation.debugRepresentation != null)
                    Entanglement.Entities.NetworkPlayer.Register(Entanglement.Representation.PlayerRepresentation.debugRepresentation);
            }
            catch { }
            TransformCollisionMessageHandler.Clear();
            HolsterSync.Clear();
            Entanglement.Patching.GunSlideSync.Clear();
            try { RemoveObjects(); } catch { }
            lastId = 0;
        }

        public static void RemoveObjects() {
            foreach (Syncable syncable in syncedObjects.Values) {
                try { syncable.Cleanup(); }
                catch { }
            }
            syncedObjects.Clear();
            queuedSyncs.Clear();

            TransformSyncable.cache = new CustomComponentCache<TransformSyncable>();
            TransformSyncable.DestructCache = new CustomComponentCache<TransformSyncable>();
        }

        public static void MoveSyncable(Syncable syncable, ushort newId) {
            ushort oldId = syncable.objectId;
            syncedObjects.Remove(oldId);
            syncedObjects.Remove(newId);

            syncedObjects.Add(newId, syncable);

            syncable.objectId = newId;

            try {
                TransformSyncable asTransform = syncable as TransformSyncable;
                if (asTransform != null) {
                    Entanglement.Entities.NetworkProp.Unregister(oldId);
                    Entanglement.Entities.NetworkProp.Register(asTransform);
                }
            }
            catch { }
        }

        public static void RegisterSyncable(Syncable syncable, ushort objectId) {
            if (syncedObjects.ContainsKey(objectId)) {
                try {
                    Syncable old = syncedObjects[objectId];
                    if (Live(old) && !ReferenceEquals(old, syncable))
                        old.Cleanup();
                }
                catch { }
                syncedObjects.Remove(objectId);
            }

            syncedObjects.Add(objectId, syncable);
            lastId = objectId;

            try {
                TransformSyncable asTransform = syncable as TransformSyncable;
                if (asTransform != null)
                    Entanglement.Entities.NetworkProp.Register(asTransform);
            }
            catch { }
        }

        public static ushort GetNextObjectId() {
            ushort start = lastId;
            do {
                lastId++;
                if (lastId == 0) lastId = 1; // 0 is reserved (host / unset)
                if (!syncedObjects.ContainsKey(lastId))
                    return lastId;
            } while (lastId != start);

            return lastId; // Table full (65k live objects), extremely unlikely
        }

        public static ushort GetNextObjectIdBlock(int count) {
            if (count <= 1)
                return GetNextObjectId();

            ushort start = lastId;
            do {
                lastId++;
                if (lastId == 0) lastId = 1;

                bool blockFree = true;
                for (int i = 0; i < count; i++) {
                    ushort id = (ushort)(lastId + i);
                    if (id == 0 || syncedObjects.ContainsKey(id)) {
                        blockFree = false;
                        break;
                    }
                }

                if (blockFree)
                    return lastId;
            } while (lastId != start);

            return lastId;
        }

        public static ushort QueueSyncable(Syncable syncable) {
            int existing = -1;
            for (int i = 0; i < queuedSyncs.Count; i++) {
                try {
                    if (queuedSyncs[i] != null && queuedSyncs[i] == syncable) {
                        existing = i;
                        break;
                    }
                }
                catch { queuedSyncs[i] = null; }
            }

            if (existing >= 0)
                queuedSyncs[existing] = null;

            queuedSyncs.Add(syncable);
            return (ushort)(queuedSyncs.Count - 1);
        }

        public static bool TryGetSyncable(ushort id, out Syncable syncable) {
            if (!syncedObjects.TryGetValue(id, out syncable))
                return false;
            if (Live(syncable))
                return true;
            syncedObjects.Remove(id);
            syncable = null;
            return false;
        }

        /// <summary>False for null, destroyed, or already collected in the Il2Cpp domain.</summary>
        public static bool Live(UnityEngine.Object obj) {
            try { return obj != null; }
            catch { return false; }
        }

        public static void GetPooleeData(Transform obj, out Rigidbody[] rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime) {
            Transform objRoot = obj.transform.root;

            overrideRootName = null;
            spawnIndex = -1;
            spawnTime = -1f;
            rigidbodies = default;

            Magazine magazine = Magazine.Cache.Get(objRoot.gameObject);
            if (magazine) {
                SpawnableObject spawnable = magazine.magazineData.spawnableObject;

                if (!spawnable) return;

                spawnTime = 0f;
                spawnIndex = 0;
                overrideRootName = spawnable.title;

                rigidbodies = objRoot.GetChildBodies();
                return;
            }

            Poolee objPoolee = Poolee.Cache.Get(objRoot.gameObject);

            if (objPoolee) {
                Pool objPool = objPoolee.pool;
            
                if (objPool) {
                    List<Poolee> allPoolees = objPool.GetAllPoolees();
                    spawnIndex = (short)allPoolees.FindIndex(o => o == objPoolee);
                    spawnTime = objPoolee.GetRelativeSpawnTime();

                    overrideRootName = objPool.name.Remove(0, 7);
                }
                rigidbodies = objPoolee.transform.GetChildBodies();
            }
            else
                rigidbodies = obj.transform.GetJointedBodies();

            rigidbodies = WithoutPieces(rigidbodies);
        }

        static Rigidbody[] WithoutPieces(Rigidbody[] rigidbodies) {
            if (rigidbodies == null)
                return rigidbodies;

            List<Rigidbody> kept = null;
            for (int i = 0; i < rigidbodies.Length; i++) {
                Rigidbody rb = rigidbodies[i];
                bool piece = rb && BreakPieces.IsPiece(rb.transform);
                if (piece && kept == null) {
                    kept = new List<Rigidbody>(rigidbodies.Length);
                    for (int j = 0; j < i; j++)
                        kept.Add(rigidbodies[j]);
                }
                if (!piece && kept != null)
                    kept.Add(rb);
            }
            return kept == null ? rigidbodies : kept.ToArray();
        }

        public static bool CheckForInstantiation(GameObject prefab, string poolName) {
            switch (poolName.ToLower()) {
                case "nimbus gun":
                case "utility gun":
                    return true;
            }
            
            Magazine magazineScript = prefab.GetComponent<Magazine>();
            if (magazineScript)
                return true;
            else
                return false;
        }

        public static void OnGripAttached(GameObject grip) {
            if (!SteamIntegration.hasLobby)
                return;

            // Taken from the radial inventory: out of its slot on everyone's screen now.
            try { HolsterSync.OnLocalGrab(grip); }
            catch { }

            MelonCoroutines.Start(OnGripValid(grip));
        }

        // We wait two frames so custom gun magazines don't spawn regular ones at 0, 0, 0 too
        public static IEnumerator OnGripValid(GameObject grip) {
            yield return null;
            yield return null;

            if (!grip || !grip.activeInHierarchy)
                yield break;

            if (grip.IsBlacklisted()) yield break;

            // A crate's piece stays this machine's own, held or not.
            if (BreakPieces.IsPiece(grip.transform)) yield break;

            if (PooleeSyncable.IsNpcBody(grip.transform)) {
                PooleeSyncable npc = PooleeSyncable.SyncedNpcOf(grip.transform);
                if (npc)
                    npc.Hold(true);
                yield break;
            }

            Rigidbody[] rigidbodies = null;

            GetPooleeData(grip.transform, out rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime);
            if (rigidbodies == null)
                yield break;

            rigidbodies = OnlyTheMagazine(grip.transform, rigidbodies);

            if (EntangleLogger.verbose)
                EntangleLogger.Debug("[Grab] Holding " + grip.transform.root.name + ": " + BodyIds(rigidbodies)
                    + (spawnIndex >= 0 ? ", spawnable " + overrideRootName : ""));

            for (int i = 0; i < rigidbodies.Length; i++) SyncUtilities.UpdateBodyAttached(rigidbodies[i], overrideRootName, spawnIndex, spawnTime);
        }

        static Rigidbody[] OnlyTheMagazine(Transform grip, Rigidbody[] bodies) {
            try {
                Magazine magazine = grip.GetComponentInParent<Magazine>();
                if (!magazine || magazine.transform.root == magazine.transform)
                    return bodies;
                Gun gun = magazine.transform.parent ? magazine.transform.parent.GetComponentInParent<Gun>() : null;
                if (!gun)
                    return bodies;
                TransformSyncable gunSync = TransformSyncable.cache.Get(gun.gameObject);
                bool heldByOther = false;
                if (gunSync && gunSync.ownerQueue != null) {
                    for (int i = 0; i < gunSync.ownerQueue.Count; i++) {
                        if (gunSync.ownerQueue[i] != SteamIntegration.currentUserId)
                            heldByOther = true;
                    }
                }
                if (!heldByOther)
                    return bodies;

                List<Rigidbody> kept = new List<Rigidbody>();
                Transform magazineRoot = magazine.transform;
                for (int i = 0; i < bodies.Length; i++) {
                    if (bodies[i] && bodies[i].transform.IsChildOf(magazineRoot))
                        kept.Add(bodies[i]);
                }
                EntangleLogger.Debug("[Mag] Took hold of " + magazine.name + " in " + gun.name + (gunSync ? " #" + gunSync.objectId : "")
                    + ", which someone else holds: asking for the mag only (" + kept.Count + " of " + bodies.Length + " bodies)");
                return kept.ToArray();
            }
            catch { return bodies; }
        }

        public static void OnBodyPunched(GameObject hit) {
            if (!SteamIntegration.hasLobby)
                return;

            if (!hit || hit.IsBlacklisted())
                return;

            MelonCoroutines.Start(OnPunchValid(hit));
        }

        public static IEnumerator OnPunchValid(GameObject hit) {
            yield return null;

            if (!hit || !hit.activeInHierarchy)
                yield break;

            PooleeSyncable npc = PooleeSyncable.SyncedNpcOf(hit.transform);
            if (npc) {
                npc.ClaimFight();
                yield break;
            }
            // A walk up from what was hit, not a search of its whole level (see OnCollisionEnterSafe).
            if (PooleeSyncable.IsNpcBody(hit.transform) || SpawnManager.IsDebris(hit.transform))
                yield break;

            GetPooleeData(hit.transform, out Rigidbody[] rigidbodies, out string overrideRootName, out short spawnIndex, out float spawnTime);

            if (rigidbodies == null)
                yield break;

            for (int i = 0; i < rigidbodies.Length; i++) {
                Rigidbody rb = rigidbodies[i];
                if (!rb || rb.isKinematic)
                    continue;

                if (TransformSyncable.cache.Get(rb.gameObject))
                    continue;

                SyncUtilities.UpdateBodyAttached(rb, overrideRootName, spawnIndex, spawnTime);
                SyncUtilities.UpdateBodyDetached(rb);
            }
        }

        static readonly Dictionary<int, float> lastContact = new Dictionary<int, float>();

        public static void OnBodyContact(Rigidbody rb) {
            if (!SteamIntegration.hasLobby || Node.activeNode == null || !rb)
                return;

            int key = rb.GetInstanceID();
            float now = Time.time;
            float last;
            if (lastContact.TryGetValue(key, out last) && now - last < 1f)
                return;
            if (lastContact.Count > 512)
                lastContact.Clear();
            lastContact[key] = now;

            GameObject go = rb.gameObject;
            if (go.IsBlacklisted())
                return;

            if (PlayerScripts.playerRig && rb.transform.IsChildOf(PlayerScripts.playerRig.transform))
                return;

            // Things jointed to another body, or sliding on a rail (lifts), are left alone. One hinged to
            // the world (a door) is picked up like a loose prop: before, nobody who pushed a door that
            // another player had last moved ever took it over, and their copy kept being pulled back to
            // the other player's last pose, so it felt very heavy until they grabbed the handle.
            if (HasBodyJoint(rb))
                return;

            if (PooleeSyncable.IsNpcBody(rb.transform) || SpawnManager.IsDebris(rb.transform))
                return;

            TransformSyncable sync = TransformSyncable.cache.Get(go);
            if (!sync) {
                OnBodyPunched(go);
                return;
            }

            if (!sync.isValid || sync.IsOwner() || sync.ownerQueue.Count > 0 || sync.holdPose)
                return;

            if (Time.time - sync.ownerChangedAt < ContactClaimCooldown)
                return;

            bool keepsOwner = rb.mass >= HeavyMass || Moving(rb) || IsBig(rb);
            if (keepsOwner && OwnerIsNear(sync.staleOwner, rb))
                return;

            RequestOwnership(sync);
        }

        static bool HasBodyJoint(Rigidbody rb) {
            try {
                Joint[] joints = rb.GetComponents<Joint>();
                if (joints == null || joints.Length == 0)
                    return false;
                if (Entanglement.Objects.RepCollisionGuard.IsPlatform(rb))
                    return true;
                for (int i = 0; i < joints.Length; i++) {
                    Joint joint = joints[i];
                    if (!joint)
                        continue;
                    Rigidbody anchor = joint.connectedBody;
                    if (anchor != null && !anchor.isKinematic)
                        return true;
                }
                return false;
            }
            catch { return true; }
        }

        const float ContactClaimCooldown = 2f;
        const float HeavyMass = 30f;
        const float OwnerNearRange = 4f;
        const float BigSize = 1.5f;

        static bool Moving(Rigidbody rb) {
            try { return rb.velocity.sqrMagnitude > 0.09f || rb.angularVelocity.sqrMagnitude > 0.25f; }
            catch { return false; }
        }

        static bool IsBig(Rigidbody rb) {
            try {
                Collider[] cols = rb.GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++) {
                    Collider col = cols[i];
                    if (!col || col.isTrigger || col.attachedRigidbody != rb)
                        continue;
                    Vector3 size = col.bounds.size;
                    if (size.x >= BigSize || size.y >= BigSize || size.z >= BigSize)
                        return true;
                }
            }
            catch { }
            return false;
        }

        static bool OwnerIsNear(long owner, Rigidbody rb) {
            if (owner == 0 || owner == SteamIntegration.currentUserId)
                return false;
            try {
                Entanglement.Representation.PlayerRepresentation rep;
                if (!Entanglement.Representation.PlayerRepresentation.representations.TryGetValue(owner, out rep) || rep == null)
                    return false;
                if (rep.Parked || !rep.repRoot)
                    return false;

                // Nearest point of the body, not its middle.
                Vector3 at = rep.repRoot.position;
                float nearest = (at - rb.worldCenterOfMass).sqrMagnitude;
                Collider[] cols = rb.GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++) {
                    Collider col = cols[i];
                    if (!col || col.isTrigger || col.attachedRigidbody != rb)
                        continue;
                    float d = (at - col.ClosestPointOnBounds(at)).sqrMagnitude;
                    if (d < nearest)
                        nearest = d;
                }
                return nearest <= OwnerNearRange * OwnerNearRange;
            }
            catch { return false; }
        }

        public static void RequestOwnership(Syncable sync) {
            long me = SteamIntegration.currentUserId;

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SyncOwner,
                new SyncOwnerMessageData { objectId = sync.objectId, owner = me });
            if (message == null)
                return;

            // The host decides straight away. A client waits for the host's answer.
            if (Server.instance != null)
                sync.ForceOwner(me);

            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        public static void OnGripDetached(Hand __instance) {
            if (!SteamIntegration.hasLobby)
                return;

            GameObject currentObject = __instance.m_CurrentAttachedObject;
            if (!currentObject)
                return;

            if (currentObject.IsBlacklisted()) return;

            if (PooleeSyncable.IsNpcBody(currentObject.transform)) {
                PooleeSyncable npc = PooleeSyncable.SyncedNpcOf(currentObject.transform);
                if (!npc)
                    return;
                // Still in the other hand.
                Rigidbody other = __instance.otherHand ? __instance.otherHand.GetHeldObject() : null;
                if (other && PooleeSyncable.SyncedNpcOf(other.transform) == npc)
                    return;
                npc.Hold(false);
                return;
            }

            Rigidbody[] rigidbodies = currentObject.transform.GetJointedBodies();

            // Two hand check
            Rigidbody otherRb = __instance.otherHand.GetHeldObject();
            if (otherRb && rigidbodies.Has(otherRb))
                return;

            if (EntangleLogger.verbose)
                EntangleLogger.Debug("[Grab] Let go of " + currentObject.transform.root.name + ": " + BodyIds(rigidbodies));

            for (int i = 0; i < rigidbodies.Length; i++)
                SyncUtilities.UpdateBodyDetached(rigidbodies[i]);
        }

        // "3 bodies (#12 owner 7656..., #13 new)": which synced ids a grab or release is about.
        static string BodyIds(Rigidbody[] bodies) {
            try {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(bodies.Length).Append(bodies.Length == 1 ? " body (" : " bodies (");
                int shown = 0;
                for (int i = 0; i < bodies.Length && shown < 6; i++) {
                    if (!bodies[i])
                        continue;
                    TransformSyncable sync = TransformSyncable.cache.Get(bodies[i].gameObject);
                    if (shown > 0)
                        sb.Append(", ");
                    if (sync && sync.objectId != 0)
                        sb.Append('#').Append(sync.objectId).Append(" owner ").Append(sync.staleOwner);
                    else
                        sb.Append(sync ? "waiting for an id" : "new");
                    shown++;
                }
                if (bodies.Length > shown)
                    sb.Append(", ...");
                return sb.Append(')').ToString();
            }
            catch { return bodies.Length + " bodies"; }
        }

        public static void OnForcePullCancelled(GameObject grip) {
            if (!SteamIntegration.hasLobby)
                return;

            if (grip.IsBlacklisted()) return;

            if (PooleeSyncable.IsNpcBody(grip.transform)) {
                PooleeSyncable npc = PooleeSyncable.SyncedNpcOf(grip.transform);
                if (npc)
                    npc.Hold(false);
                return;
            }

            Rigidbody[] rigidbodies = grip.transform.GetJointedBodies();

            for (int i = 0; i < rigidbodies.Length; i++)
                SyncUtilities.UpdateBodyDetached(rigidbodies[i]);
        }
    }
}
