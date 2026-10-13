using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;

using StressLevelZero.AI;
using Entanglement.Data;
using Entanglement.Extensions;

using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.Objects
{
    public static class BossClawSync
    {
        // Player proxy GameObject instance id -> owning player id.
        static readonly Dictionary<int, long> proxyOwners = new Dictionary<int, long>();

        // Owning player id -> proxy.
        static readonly Dictionary<long, TriggerRefProxy> playerProxies = new Dictionary<long, TriggerRefProxy>();

        static BossClawAi[] claws;
        static float lastScan = -99f;

        static long prey;
        static bool hasPrey;

        // Change detection so we only send on transition, not every tick.
        static long lastSentPrey;
        static bool lastSentValid;

        // Our own TriggerStateChange calls re-enter the prefix.
        static bool applying;

        /// <summary>Logs claw state changes. Turned on with the representation test.</summary>
        public static bool traceEnabled;

        static BossClawAi.MentalState lastMental;
        static BossClawAi.PounceState lastPounce;
        static long lastTracedTarget;
        static bool tracePrimed;

        // Bodies that make up the claw, and their syncables once they exist on this machine.
        static readonly List<Rigidbody> clawBodies = new List<Rigidbody>();
        static readonly List<TransformSyncable> clawSyncs = new List<TransformSyncable>();
        static bool bodiesRegistered;

        static long appliedOwner;
        static bool authorityPrimed;

        public static bool Applying => applying;

        // Inside BossClawAi.SwitchPounceState (BossClawPouncePatch).
        public static int pounceDepth;

        // Playing out the running machine's pounce or scoop here.
        static bool applyingState;

        /// <summary>The machine running the claw: its pounce or scoop changed, tell the others.</summary>
        public static void SendState(BossClawAi claw, SceneEventType type, ushort arg) {
            if (applyingState || !SteamIntegration.hasLobby || !RunningHere || claw == null)
                return;
            SceneEventSync.SendEvent(type, claw.transform, arg, false);
        }

        /// <summary>Another machine runs the claw and its pounce, scoop, mental state or bind changed.</summary>
        public static bool ApplyEvent(SceneEventType type, ushort arg, Transform target, string objectPath = null) {
            if (type == SceneEventType.ClawBind) {
                ApplyBind(arg, objectPath);
                return true;
            }

            if (type != SceneEventType.ClawPounce && type != SceneEventType.ClawScoop
                && type != SceneEventType.ClawMental && type != SceneEventType.ClawExtend)
                return false;

            // It runs here now; this machine's own AI decides.
            if (RunningHere)
                return true;

            BossClawAi claw = target ? target.GetComponent<BossClawAi>() : null;
            if (claw == null)
                claw = FirstClaw();
            if (claw == null)
                return true;

            applyingState = true;
            try {
                switch (type) {
                    case SceneEventType.ClawPounce:
                        claw.SwitchPounceState((BossClawAi.PounceState)arg);
                        break;
                    case SceneEventType.ClawScoop:
                        claw.ToggleScoop(arg != 0);
                        break;
                    case SceneEventType.ClawMental:
                        claw.SwitchMentalState((BossClawAi.MentalState)arg);
                        break;
                    case SceneEventType.ClawExtend:
                        ApplyExtension(claw, arg, objectPath);
                        break;
                }
            }
            catch (Exception e) { EntangleLogger.Debug("BossClawSync: " + type + " " + arg + ": " + e.Message); }
            finally { applyingState = false; }
            return true;
        }

        static void ApplyExtension(BossClawAi claw, ushort arg, string objectPath) {
            float cur, targetExt;
            if (!TryParseExtension(objectPath, out cur, out targetExt))
                UnpackExtension(arg, out cur, out targetExt);
            try { claw._curExtension = cur; }
            catch { }
            try { claw._targetExtension = targetExt; }
            catch { }
        }

        static bool TryParseExtension(string path, out float cur, out float target) {
            cur = 0f;
            target = 0f;
            if (string.IsNullOrEmpty(path))
                return false;
            int bar = path.LastIndexOf('|');
            string pair = bar >= 0 ? path.Substring(bar + 1) : path;
            string[] parts = pair.Split(',');
            return parts.Length >= 2
                && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cur)
                && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out target);
        }

        static void UnpackExtension(ushort packed, out float cur, out float target) {
            cur = (packed & 0xFF) / 20f;
            target = ((packed >> 8) & 0xFF) / 20f;
        }

        static ushort PackExtension(float cur, float target) {
            int c = Mathf.Clamp(Mathf.RoundToInt(cur * 20f), 0, 255);
            int t = Mathf.Clamp(Mathf.RoundToInt(target * 20f), 0, 255);
            return (ushort)(c | (t << 8));
        }

        static BossClawAi FirstClaw() {
            BossClawAi[] found = Claws();
            return found != null && found.Length > 0 ? found[0] : null;
        }
        public static long Prey => prey;
        public static bool HasPrey => hasPrey;

        /// <summary>The player whose machine runs the claw.</summary>
        public static long Owner => hasPrey ? prey : SteamIntegration.lobbyOwnerId;

        static bool RunningHere => Owner == SteamIntegration.currentUserId;

        public static void OnSceneCleanup() {
            proxyOwners.Clear();
            playerProxies.Clear();
            claws = null;
            lastScan = -99f;
            prey = 0L;
            hasPrey = false;
            lastSentPrey = 0L;
            lastSentValid = false;
            applying = false;
            tracePrimed = false;
            lastTracedTarget = 0L;
            emptyScans = 0;
            pendingPrimed = false;
            pendingPrey = 0L;
            pendingValid = false;
            clawBodies.Clear();
            clawSyncs.Clear();
            bodiesRegistered = false;
            boundFirstId = 0;
            boundCount = 0;
            nextBindAnnounce = 0f;
            nextExtendSend = 0f;
            lastSentCur = float.NaN;
            lastSentTarget = float.NaN;
            appliedOwner = 0L;
            authorityPrimed = false;
            lastHandover = -100f;
            zone.Clear();
            focus = 0L;
            lastFocus = 0L;
            focusSince = 0f;
            wasCommitted = false;
            lastMentalWasUnload = false;
            pounceDepth = 0;
        }

        /// <summary>A player left. If they were the prey, the claw goes back to the host.</summary>
        public static void OnUserLeft(long userId) {
            TriggerRefProxy proxy;
            if (playerProxies.TryGetValue(userId, out proxy)) {
                playerProxies.Remove(userId);
                if (proxy != null) {
                    try { proxyOwners.Remove(proxy.gameObject.GetInstanceID()); }
                    catch { }
                }
            }

            List<int> stale = null;
            foreach (KeyValuePair<int, long> pair in proxyOwners) {
                if (pair.Value != userId)
                    continue;
                if (stale == null)
                    stale = new List<int>();
                stale.Add(pair.Key);
            }
            if (stale != null) {
                for (int i = 0; i < stale.Count; i++)
                    proxyOwners.Remove(stale[i]);
            }

            zone.Remove(userId);
            if (focus == userId)
                focus = 0L;

            if (hasPrey && prey == userId) {
                prey = 0L;
                hasPrey = false;
                lastSentPrey = 0L;
                lastSentValid = false;
                EntangleLogger.Debug("BossClawSync: prey " + userId + " left, the host runs the claw again.");
            }
        }

        /// <summary>Called by PlayerRepresentation.EnsureEnemyTarget for remote players.</summary>
        public static void RegisterProxy(TriggerRefProxy proxy, long playerId) {
            if (proxy == null)
                return;

            try {
                proxyOwners[proxy.gameObject.GetInstanceID()] = playerId;
                playerProxies[playerId] = proxy;
            }
            catch { }
        }

        public static void UnregisterProxy(TriggerRefProxy proxy) {
            if (proxy == null)
                return;

            try {
                int instanceId = proxy.gameObject.GetInstanceID();
                long owner;
                if (proxyOwners.TryGetValue(instanceId, out owner))
                    playerProxies.Remove(owner);
                proxyOwners.Remove(instanceId);
            }
            catch { }
        }

        public static long OwnerOf(TriggerRefProxy proxy) {
            if (proxy == null)
                return 0L;

            int instanceId;
            try { instanceId = proxy.gameObject.GetInstanceID(); }
            catch { return 0L; }

            long owner;
            if (proxyOwners.TryGetValue(instanceId, out owner))
                return owner;

            if (IsLocalRigProxy(proxy)) {
                proxyOwners[instanceId] = SteamIntegration.currentUserId;
                playerProxies[SteamIntegration.currentUserId] = proxy;
                return SteamIntegration.currentUserId;
            }

            return 0L;
        }

        static bool IsLocalRigProxy(TriggerRefProxy proxy) {
            try {
                if (!PlayerScripts.playerRig)
                    return false;

                return proxy.transform.IsChildOf(PlayerScripts.playerRig.transform);
            }
            catch { return false; }
        }

        static bool IsPlayerProxy(TriggerRefProxy proxy) {
            try { return proxy.triggerType == TriggerRefProxy.TriggerType.Player; }
            catch { return false; }
        }

        static readonly Dictionary<long, TriggerRefProxy> zone = new Dictionary<long, TriggerRefProxy>();
        static long focus;
        static long lastFocus;
        static float focusSince;
        static bool wasCommitted;
        static bool lastMentalWasUnload;
        const float FocusPatience = 12f;

        /// <summary>The one player the claw may go for now, or 0.</summary>
        public static long Focus => focus;

        public static bool AllowTriggerStateChange(TriggerRefProxy trp, bool enter) {
            if (applying || !SteamIntegration.hasLobby)
                return true;

            if (trp == null || !IsPlayerProxy(trp))
                return true;

            long owner = OwnerOf(trp);
            if (owner == 0L)
                return true;

            if (enter)
                zone[owner] = trp;
            else
                zone.Remove(owner);

            if (!RunningHere)
                return true;

            if (enter) {
                if (focus == 0L)
                    SetFocus(owner);
                return owner == focus;
            }

            // Leaving: only someone the claw actually has on its list is taken off it.
            if (owner == focus)
                focus = 0L;
            return OnClawList(trp);
        }

        static void SetFocus(long player) {
            if (focus == player)
                return;
            if (focus != 0L)
                lastFocus = focus;
            focus = player;
            focusSince = Time.realtimeSinceStartup;
            wasCommitted = false;
            EntangleLogger.Debug("BossClawSync: claw focus -> " + (player == 0L ? "nobody" : player.ToString()));
        }

        static bool OnClawList(TriggerRefProxy trp) {
            BossClawAi[] found = claws;
            if (found == null)
                return true;
            for (int i = 0; i < found.Length; i++) {
                try {
                    Il2CppSystem.Collections.Generic.List<TriggerRefProxy> list = found[i] ? found[i].targetList : null;
                    if (list == null)
                        continue;
                    for (int j = 0; j < list.Count; j++) {
                        if (list[j] == trp)
                            return true;
                    }
                }
                catch { return true; }
            }
            return false;
        }

        // Host: who the claw goes for next.
        static void ChooseFocus(BossClawAi claw) {
            // Seed from the claw's own list, for anyone who walked in before we were watching.
            try {
                Il2CppSystem.Collections.Generic.List<TriggerRefProxy> list = claw.targetList;
                if (list != null) {
                    for (int j = 0; j < list.Count; j++) {
                        TriggerRefProxy entry = list[j];
                        if (entry == null || !IsPlayerProxy(entry))
                            continue;
                        long owner = OwnerOf(entry);
                        if (owner != 0L && !zone.ContainsKey(owner))
                            zone[owner] = entry;
                    }
                }
            }
            catch { }

            bool committed = Committed(claw);
            bool unloading = false;
            try { unloading = claw._mentalState == BossClawAi.MentalState.Unload; }
            catch { }

            // Finished with the focus: it was carried and dropped off.
            if (wasCommitted && !committed && lastMentalWasUnload)
                SetFocus(0L);
            if (committed)
                wasCommitted = true;
            lastMentalWasUnload = unloading || (lastMentalWasUnload && committed);

            if (focus != 0L && !zone.ContainsKey(focus))
                SetFocus(0L);

            // Not getting anywhere with this one while others wait their turn.
            if (focus != 0L && !committed && zone.Count > 1 && Time.realtimeSinceStartup - focusSince > FocusPatience)
                SetFocus(0L);

            if (focus != 0L || zone.Count == 0)
                return;

            Vector3 from;
            try { from = claw._baseRb ? claw._baseRb.position : claw.transform.position; }
            catch { return; }

            long best = 0L;
            float bestDistance = float.MaxValue;
            foreach (KeyValuePair<long, TriggerRefProxy> pair in zone) {
                float distance;
                try {
                    if (pair.Value == null)
                        continue;
                    distance = (pair.Value.transform.position - from).sqrMagnitude;
                }
                catch { continue; }
                // Someone other than the one it just finished with, if anyone else is there.
                if (pair.Key == lastFocus && zone.Count > 1)
                    distance += 10000f;
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = pair.Key;
                }
            }
            if (best != 0L)
                SetFocus(best);
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby)
                return;

            BossClawAi[] found = Claws();
            if (found == null || found.Length == 0)
                return;

            if (traceEnabled)
                Trace(found);

            EnsureBodies(found);
            ApplyAuthority(found);

            if (!RunningHere)
                return;

            SendMotion(found[0]);

            if (SteamIntegration.isHost)
                ChooseFocus(found[0]);
            else {
                long me = SteamIntegration.currentUserId;
                SetFocus(me);
                if (!zone.ContainsKey(me) && Time.realtimeSinceStartup - lastHandover < 3f) {
                    TriggerRefProxy local = PlayerScripts.LocalProxy();
                    if (local != null) {
                        OwnerOf(local);
                        zone[me] = local;
                    }
                }
            }

            KeepOnlyFocus(found);

            ElectTick(found);
        }

        // ---- bodies ---------------------------------------------------------------------------

        // The claw's bodies in a fixed order, the same on every machine (same prefab, same fields):
        // the base, the bodies of its arm joints, then the scoop's (the jaws that close on the prey,
        // which used not to be synced at all). The bind carries only the first id and a count.
        static void CollectBodies(BossClawAi claw) {
            clawBodies.Clear();
            AddBody(claw._baseRb);

            ConfigurableJoint[] joints = new ConfigurableJoint[5];
            try { joints[0] = claw._jointBase; } catch { }
            try { joints[1] = claw._jointSpineA; } catch { }
            try { joints[2] = claw._jointSpineB; } catch { }
            try { joints[3] = claw._jointSpineC; } catch { }
            try { joints[4] = claw._jointCabin; } catch { }

            for (int i = 0; i < joints.Length; i++) {
                ConfigurableJoint joint = joints[i];
                if (!joint)
                    continue;
                try { AddBody(joint.GetComponent<Rigidbody>()); } catch { }
                try { AddBody(joint.connectedBody); } catch { }
            }

            try {
                UnhollowerBaseLib.Il2CppReferenceArray<GameObject> scoops = claw._scoopObjects;
                if (scoops != null) {
                    for (int i = 0; i < scoops.Length; i++) {
                        GameObject scoop = scoops[i];
                        if (!scoop)
                            continue;
                        Rigidbody[] bodies = scoop.GetComponentsInChildren<Rigidbody>(true);
                        for (int b = 0; b < bodies.Length; b++)
                            AddBody(bodies[b]);
                    }
                }
            }
            catch { }
        }

        static void AddBody(Rigidbody rb) {
            if (!rb)
                return;
            for (int i = 0; i < clawBodies.Count; i++) {
                if (clawBodies[i] == rb)
                    return;
            }
            clawBodies.Add(rb);
        }

        static ushort boundFirstId;
        static byte boundCount;
        static float nextBindAnnounce;
        static float nextExtendSend;
        static float lastSentCur = float.NaN;
        static float lastSentTarget = float.NaN;

        static void EnsureBodies(BossClawAi[] found) {
            if (clawBodies.Count == 0) {
                try { CollectBodies(found[0]); }
                catch { return; }
            }

            if (!bodiesRegistered && SteamIntegration.isHost && Node.activeNode != null && clawBodies.Count > 0) {
                bodiesRegistered = true;
                int count = Math.Min(clawBodies.Count, 255);
                ushort id = ObjectSync.GetNextObjectIdBlock(count);
                BindLocalBodies(id, (byte)count);
                boundFirstId = id;
                boundCount = (byte)count;
                AnnounceBind(false);
                EntangleLogger.Debug("BossClawSync: syncing " + count + " claw bodies from #" + id);
            }

            if (SteamIntegration.isHost && boundCount > 0 && Time.realtimeSinceStartup >= nextBindAnnounce
                && PlayerRepresentation.RemoteCount > 0) {
                nextBindAnnounce = Time.realtimeSinceStartup + 8f;
                AnnounceBind(false);
            }

            // Pick up syncables as they appear here (clients get them from the host's bind).
            if (clawSyncs.Count >= clawBodies.Count)
                return;

            for (int i = 0; i < clawBodies.Count; i++) {
                try {
                    Rigidbody rb = clawBodies[i];
                    if (!rb)
                        continue;
                    TransformSyncable sync = TransformSyncable.cache.GetOrAdd(rb.gameObject);
                    if (!sync || clawSyncs.Contains(sync))
                        continue;
                    // Its base can be kinematic, moved by the claw's own script: that one is moved
                    // to the owner's pose here, the jointed arm is steered there with forces.
                    sync.followKinematic = true;
                    // The claw lowers and scoops by joint targets its AI sets. Where it doesn't run, those
                    // joints stay relaxed and the owner's pose is held.
                    sync.holdPose = true;
                    if (sync.minSendInterval < 1f / 20f)
                        sync.minSendInterval = 1f / 20f;
                    clawSyncs.Add(sync);
                    authorityPrimed = false; // New body: hand it to the current owner
                }
                catch { }
            }
        }

        static void BindLocalBodies(ushort id, byte count) {
            long owner = Owner;
            int n = Math.Min(clawBodies.Count, count);
            for (ushort i = 0; i < n; i++) {
                Rigidbody rb = clawBodies[i];
                if (!rb)
                    continue;
                ushort thisId = (ushort)(id + i);
                TransformSyncable existing = TransformSyncable.cache.GetOrAdd(rb.gameObject);
                if (existing) {
                    ObjectSync.MoveSyncable(existing, thisId);
                    existing.ClearOwner();
                    existing.TrySetStale(owner);
                }
                else
                    TransformSyncable.CreateSync(owner, rb, thisId);
                ObjectSync.lastId = thisId;
            }
        }

        static void AnnounceBind(bool record) {
            if (boundCount == 0 || boundFirstId == 0)
                return;
            BossClawAi claw = FirstClaw();
            if (claw == null)
                return;
            string path;
            try { path = claw.transform.GetFullPath() + "|" + boundCount; }
            catch { path = "|" + boundCount; }
            SceneEventSync.SendEventPath(SceneEventType.ClawBind, path, boundFirstId, record);
        }

        static void ApplyBind(ushort id, string objectPath) {
            if (id == 0 || Node.isServer)
                return;
            int count = boundCount;
            if (!string.IsNullOrEmpty(objectPath)) {
                int bar = objectPath.LastIndexOf('|');
                int parsed;
                if (bar >= 0 && int.TryParse(objectPath.Substring(bar + 1), out parsed) && parsed > 0 && parsed <= 255)
                    count = parsed;
            }
            if (count <= 0)
                return;
            // Said again every few seconds for anyone who missed it; once bound there is nothing to do.
            if (bodiesRegistered && boundFirstId == id && boundCount == count && clawSyncs.Count > 0)
                return;

            BossClawAi claw = FirstClaw();
            if (claw == null)
                return;
            if (clawBodies.Count == 0) {
                try { CollectBodies(claw); }
                catch { return; }
            }
            if (clawBodies.Count == 0)
                return;

            BindLocalBodies(id, (byte)count);
            boundFirstId = id;
            boundCount = (byte)count;
            bodiesRegistered = true;
            authorityPrimed = false;
            EntangleLogger.Debug("BossClawSync: bound " + Math.Min(clawBodies.Count, count) + " claw bodies from #" + id);
        }

        static void SendMotion(BossClawAi claw) {
            if (claw == null || applyingState)
                return;
            float now = Time.realtimeSinceStartup;
            if (now < nextExtendSend)
                return;
            nextExtendSend = now + 0.2f;

            float cur = 0f, target = 0f;
            try { cur = claw._curExtension; }
            catch { return; }
            try { target = claw._targetExtension; }
            catch { }

            if (!float.IsNaN(lastSentCur) && Math.Abs(cur - lastSentCur) < 0.02f && Math.Abs(target - lastSentTarget) < 0.02f)
                return;
            lastSentCur = cur;
            lastSentTarget = target;

            string path;
            try {
                path = claw.transform.GetFullPath() + "|"
                    + cur.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ","
                    + target.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { path = cur + "," + target; }
            SceneEventSync.SendEventPath(SceneEventType.ClawExtend, path, PackExtension(cur, target), false);
        }

        static void ApplyAuthority(BossClawAi[] found) {
            long owner = Owner;
            if (authorityPrimed && owner == appliedOwner)
                return;

            authorityPrimed = true;
            appliedOwner = owner;
            bool here = owner == SteamIntegration.currentUserId;

            for (int i = 0; i < found.Length; i++) {
                BossClawAi claw = found[i];
                if (claw == null)
                    continue;
                try { claw.enabled = here; } catch { }
                try {
                    NavMeshAgent agent = claw._navAgent;
                    if (agent) {
                        agent.enabled = here;
                        if (here && claw._baseRb && agent.isOnNavMesh)
                            agent.Warp(claw._baseRb.position);
                    }
                }
                catch { }
                // Decide straight away from what is in front of it now, not from its old state.
                if (here) {
                    try { claw._forceAiTick = true; } catch { }
                }
            }

            for (int i = 0; i < clawSyncs.Count; i++) {
                try {
                    if (clawSyncs[i])
                        clawSyncs[i].ForceOwner(owner, false);
                }
                catch { }
            }

            EntangleLogger.Debug("BossClawSync: claw now runs on " + (here ? "this machine" : owner.ToString()));
        }

        static void KeepOnlyFocus(BossClawAi[] found) {
            TriggerRefProxy mine = null;
            if (focus != 0L && zone.TryGetValue(focus, out mine) && mine == null)
                zone.Remove(focus);
            if (focus == SteamIntegration.currentUserId && mine == null && zone.ContainsKey(focus)) {
                try {
                    if (PlayerScripts.playerRig)
                        mine = PlayerScripts.playerRig.GetComponentInChildren<TriggerRefProxy>(true);
                }
                catch { mine = null; }
                if (mine != null)
                    OwnerOf(mine);
            }

            for (int i = 0; i < found.Length; i++) {
                BossClawAi claw = found[i];
                if (claw == null)
                    continue;

                Il2CppSystem.Collections.Generic.List<TriggerRefProxy> list;
                try { list = claw.targetList; }
                catch { continue; }
                if (list == null)
                    continue;

                int count;
                try { count = list.Count; }
                catch { continue; }

                bool mePresent = false;
                for (int j = count - 1; j >= 0; j--) {
                    TriggerRefProxy entry;
                    try { entry = list[j]; }
                    catch { continue; }

                    if (entry == null || !IsPlayerProxy(entry))
                        continue;

                    long owner = OwnerOf(entry);
                    if (owner == 0L)
                        continue;

                    if (owner == focus && focus != 0L) {
                        mePresent = true;
                        continue;
                    }

                    Drive(claw, entry, false);
                }

                if (!mePresent && mine != null)
                    Drive(claw, mine, true);
            }
        }

        static void Drive(BossClawAi claw, TriggerRefProxy proxy, bool enter) {
            applying = true;
            try { claw.TriggerStateChange(proxy, enter); }
            catch (Exception e) {
                EntangleLogger.Warn("BossClawSync " + (enter ? "admit" : "evict") + ": " + e.Message);
            }
            finally { applying = false; }
        }

        // ---- election -------------------------------------------------------------------------

        const float electionHoldTime = 0.35f;

        const float switchHoldTime = 1.5f;
        const float releaseHoldTime = 1.5f;
        const float minimumTenure = 3f;
        static float lastHandover = -100f;

        static bool Committed(BossClawAi claw) {
            try {
                BossClawAi.MentalState mental = claw._mentalState;
                return mental == BossClawAi.MentalState.Pounce || mental == BossClawAi.MentalState.Unload;
            }
            catch { return false; }
        }

        static long pendingPrey;
        static bool pendingValid;
        static float pendingSince;
        static bool pendingPrimed;

        static void ElectTick(BossClawAi[] found) {
            long current = 0L;
            bool valid = false;

            BossClawAi claw = found[0];
            if (claw == null)
                return;

            TriggerRefProxy active;
            try { active = claw._activeTarget; }
            catch { return; }

            // Only a player it has committed to counts; aiming at someone while patrolling doesn't.
            if (active != null && !Committed(claw))
                active = null;

            if (active != null) {
                if (!IsPlayerProxy(active))
                    return;

                long owner = OwnerOf(active);
                if (owner == 0L)
                    return;

                current = owner;
                valid = true;
            }

            if (!pendingPrimed || current != pendingPrey || valid != pendingValid) {
                pendingPrey = current;
                pendingValid = valid;
                pendingSince = Time.realtimeSinceStartup;
                pendingPrimed = true;
                return;
            }

            // The host running it for itself is the same as nobody.
            if (valid && current == SteamIntegration.lobbyOwnerId)
                valid = false;

            if (valid == hasPrey && (!valid || current == prey))
                return;

            float hold = !hasPrey ? electionHoldTime : (valid ? switchHoldTime : releaseHoldTime);
            float now = Time.realtimeSinceStartup;
            if (now - pendingSince < hold || now - lastHandover < minimumTenure)
                return;

            prey = valid ? current : 0L;
            hasPrey = valid;
            lastHandover = now;
            Send(prey, hasPrey);
        }

        static void Send(long newPrey, bool valid) {
            if (Node.activeNode == null)
                return;
            if (lastSentValid == valid && lastSentPrey == newPrey)
                return;
            lastSentPrey = newPrey;
            lastSentValid = valid;

            ClawTargetMessageData data = new ClawTargetMessageData();
            data.prey = newPrey;
            data.valid = valid;

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ClawTarget, data);
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            EntangleLogger.Debug("BossClawSync: prey -> " + (valid ? newPrey.ToString() : "none"));
        }

        public static void ApplyRemote(long newPrey, bool valid) {
            prey = valid ? newPrey : 0L;
            hasPrey = valid;
            lastSentPrey = prey;
            lastSentValid = valid;
            pendingPrimed = false;
            lastHandover = Time.realtimeSinceStartup;
            // Back to the host after a grab elsewhere: next in line, not straight back to them.
            if (!valid && SteamIntegration.isHost)
                SetFocus(0L);
        }

        /// <summary>Late joiners need the current decision straight away.</summary>
        public static void ReplayTo(long userId) {
            if (!SteamIntegration.isHost || Node.activeNode == null)
                return;

            ClawTargetMessageData data = new ClawTargetMessageData();
            data.prey = hasPrey ? prey : 0L;
            data.valid = hasPrey;

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ClawTarget, data);
            if (message != null)
                Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());

            if (boundCount > 0 && boundFirstId != 0) {
                BossClawAi claw = FirstClaw();
                string path = "|" + boundCount;
                try { if (claw) path = claw.transform.GetFullPath() + "|" + boundCount; }
                catch { }
                SceneEventMessageData bind = new SceneEventMessageData {
                    eventType = SceneEventType.ClawBind,
                    arg = boundFirstId,
                    objectPath = path,
                    replay = true,
                };
                NetworkMessage bindMessage = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, bind);
                if (bindMessage != null)
                    Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, bindMessage.GetBytes());

                if (claw != null) {
                    try {
                        SceneEventMessageData mental = new SceneEventMessageData {
                            eventType = SceneEventType.ClawMental,
                            arg = (ushort)claw._mentalState,
                            objectPath = path,
                            replay = true,
                        };
                        NetworkMessage mentalMessage = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, mental);
                        if (mentalMessage != null)
                            Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, mentalMessage.GetBytes());
                    }
                    catch { }
                    try {
                        SceneEventMessageData pounce = new SceneEventMessageData {
                            eventType = SceneEventType.ClawPounce,
                            arg = (ushort)claw._pounceState,
                            objectPath = path,
                            replay = true,
                        };
                        NetworkMessage pounceMessage = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, pounce);
                        if (pounceMessage != null)
                            Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, pounceMessage.GetBytes());
                    }
                    catch { }
                }
            }
        }

        /// <summary>For the solo claw check: its bodies, how many are synced, and who runs it.</summary>
        public static string Describe() {
            BossClawAi[] found = Claws();
            if (found == null || found.Length == 0)
                return "No boss claw in this level.";
            if (clawBodies.Count == 0) {
                try { CollectBodies(found[0]); }
                catch { }
            }
            int kinematic = 0;
            for (int i = 0; i < clawBodies.Count; i++) {
                try {
                    if (clawBodies[i] && clawBodies[i].isKinematic)
                        kinematic++;
                }
                catch { }
            }
            string bound = boundCount > 0 ? "ids #" + boundFirstId + "-" + (boundFirstId + boundCount - 1) : "not bound (needs a lobby)";
            return "Claw: " + clawBodies.Count + " bodies (" + kinematic + " kinematic), " + clawSyncs.Count + " synced, " + bound
                + ", runs on " + (RunningHere ? "this machine" : Owner.ToString()) + ".";
        }

        // ---- helpers --------------------------------------------------------------------------

        static int emptyScans;

        const float ScanEvery = 5f;
        // A level's claws are there from the start, so one with none found after a few looks is only
        // checked now and then: searching every object in the game took ~20ms (Oct 8 logs).
        const float IdleScanEvery = 120f;

        static BossClawAi[] Claws() {
            float now = Time.realtimeSinceStartup;
            if (claws != null && claws.Length > 0) {
                if (now - lastScan < ScanEvery)
                    return claws;
                bool alive = true;
                for (int i = 0; i < claws.Length; i++) {
                    try {
                        if (!claws[i])
                            alive = false;
                    }
                    catch { alive = false; }
                }
                if (alive) {
                    lastScan = now;
                    return claws;
                }
            }
            else if (now - lastScan < (emptyScans >= 4 ? IdleScanEvery : ScanEvery))
                return claws;

            lastScan = now;
            claws = FindClaws();

            if (claws == null || claws.Length == 0)
                emptyScans++;
            else {
                if (emptyScans > 0 || clawBodies.Count == 0)
                    EntangleLogger.Debug("BossClawSync: found " + claws.Length + " claw(s)");
                emptyScans = 0;
            }

            return claws;
        }

        // Every claw in a loaded scene, switched on or not (prefabs and assets left out).
        static BossClawAi[] FindClaws() {
            UnhollowerBaseLib.Il2CppReferenceArray<UnityEngine.Object> found;
            try { found = Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<BossClawAi>()); }
            catch { return null; }
            if (found == null || found.Length == 0)
                return null;

            List<BossClawAi> inScene = new List<BossClawAi>();
            for (int i = 0; i < found.Length; i++) {
                try {
                    BossClawAi claw = found[i] ? found[i].TryCast<BossClawAi>() : null;
                    if (!claw)
                        continue;
                    GameObject go = claw.gameObject;
                    if (!go.scene.IsValid() || !go.scene.isLoaded)
                        continue;
                    inScene.Add(claw);
                }
                catch { }
            }
            return inScene.Count > 0 ? inScene.ToArray() : null;
        }

        // Reads only. Logs claw state changes as they happen.
        static void Trace(BossClawAi[] found) {
            BossClawAi claw = found[0];
            if (claw == null)
                return;

            try {
                BossClawAi.MentalState mental = claw._mentalState;
                BossClawAi.PounceState pounce = claw._pounceState;

                TriggerRefProxy active = claw._activeTarget;
                long target = active != null ? OwnerOf(active) : 0L;

                bool changed = !tracePrimed
                    || mental != lastMental
                    || pounce != lastPounce
                    || target != lastTracedTarget;

                if (!changed)
                    return;

                tracePrimed = true;
                lastMental = mental;
                lastPounce = pounce;
                lastTracedTarget = target;

                int listCount = -1;
                try { listCount = claw.targetList != null ? claw.targetList.Count : -1; }
                catch { }

                string who = target != 0L
                    ? (target == SteamIntegration.currentUserId ? "me(" + target + ")" : target.ToString())
                    : (active != null ? "non-player" : "none");

                // Following: how many of its bodies are synced here, and how many had a pose lately.
                int fresh = 0;
                for (int i = 0; i < clawSyncs.Count; i++) {
                    try {
                        if (clawSyncs[i] && clawSyncs[i].lastRemotePoseAt >= 0f && Time.time - clawSyncs[i].lastRemotePoseAt < 2f)
                            fresh++;
                    }
                    catch { }
                }

                EntangleLogger.Log("[ClawTrace] " + (RunningHere ? "RUNNING" : "FOLLOWING")
                    + " mental=" + mental
                    + " pounce=" + pounce
                    + " target=" + who
                    + " targetList=" + listCount
                    + " owner=" + Owner
                    + " ext=" + claw._curExtension.ToString("F2") + "/" + claw._targetExtension.ToString("F2")
                    + " bodies=" + clawSyncs.Count + "/" + clawBodies.Count + (RunningHere ? "" : " posed=" + fresh));
            }
            catch { }
        }
    }
}
