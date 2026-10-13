using System;
using System.Collections.Generic;
using System.Collections;

using UnityEngine;

using StressLevelZero.Pool;

using Entanglement.Extensions;
using Entanglement.Data;
using Entanglement.Network;
using Entanglement.Representation;

using MelonLoader;
using UnhollowerBaseLib;

namespace Entanglement.Objects
{
    [RegisterTypeInIl2Cpp]
    public class PooleeSyncable : MonoBehaviour {
        public static CustomComponentCache<PooleeSyncable> _Cache = new CustomComponentCache<PooleeSyncable>();

        public static Dictionary<ushort, PooleeSyncable> _PooleeLookup = new Dictionary<ushort, PooleeSyncable>(new UnityComparer());

        public PooleeSyncable(IntPtr intPtr) : base(intPtr) { }


        public Poolee Poolee;

        public ushort id;

        public byte rbCount;

        public bool pathAddressed;

        // Its owner has said this life of it is over. Cleared when it is spawned again.
        public bool DeclaredDead { get; private set; }

        public string placeKey;

        public TransformSyncable[] transforms;

        int rescans;
        float nextRescan;
        const int MaxRescans = 20;

        static bool reportedCopies;
        static bool reportedBlanks;

        public void BindTransforms(TransformSyncable[] found) {
            if (id != 0 && rbCount > 0) {
                List<TransformSyncable> registered = new List<TransformSyncable>(rbCount);
                for (int i = 0; i < rbCount; i++) {
                    Syncable syncable;
                    if (!ObjectSync.TryGetSyncable((ushort)(id + i), out syncable) || syncable == null)
                        continue;
                    TransformSyncable sync = syncable as TransformSyncable;
                    if (Live(sync))
                        registered.Add(sync);
                }

                if (registered.Count > 0) {
                    int blanks = 0;
                    if (found != null) {
                        for (int i = 0; i < found.Length; i++) {
                            try { if (Live(found[i]) && found[i].objectId == 0) blanks++; }
                            catch { }
                        }
                    }

                    if (blanks > 0 && !reportedBlanks) {
                        reportedBlanks = true;
                        int perBone = -1;
                        try { perBone = registered[0].GetComponents<TransformSyncable>().Length; }
                        catch { }
                        EntangleLogger.Warn("[PooleeSync] " + gameObject.name + " had " + blanks + " syncable(s) with no id next to its "
                            + registered.Count + " real ones (" + perBone + " on its first bone). Using the real ones.");
                    }

                    found = registered.ToArray();
                }
            }

            if (found != null) {
                for (int i = 0; i < found.Length; i++) {
                    TransformSyncable real = null;
                    try {
                        if (found[i])
                            real = TransformSyncable.cache.Get(found[i].gameObject);
                    }
                    catch { }

                    if (real == null || ReferenceEquals(real, found[i]))
                        continue;

                    if (!reportedCopies) {
                        reportedCopies = true;
                        EntangleLogger.Debug("[PooleeSync] Body list held copies without our data (copy owner="
                            + found[i].staleOwner + ", real owner=" + real.staleOwner + "); using the real syncables.");
                    }
                    found[i] = real;
                }
            }

            transforms = found;

            bool traced = false;
            try { traced = GetComponentInChildren<StressLevelZero.AI.AIBrain>(true) || TurretSync.IsTurret(gameObject); }
            catch { }
            if (!traced || found == null)
                return;

            bool first = true;
            for (int i = 0; i < found.Length; i++) {
                if (!Live(found[i]))
                    continue;
                // Owner changes are traced on one body: they all change together.
                found[i].traceOwner = first;
                first = false;
                found[i].aiPart = true;
                found[i].npcRoot = this;
            }
            NpcTrace.Log("bound " + NpcTrace.Name(gameObject) + " #" + id + " bodies=" + found.Length + " owner=" + CurrentOwner());
        }

        /// <summary>Its owner's copy died: this one plays that death and stays dead until spawned again.</summary>
        public void NoteDeclaredDeath() {
            DeclaredDead = true;
        }

        void EnsureTransforms() {
            bool haveLive = false;
            if (transforms != null) {
                for (int i = 0; i < transforms.Length; i++) {
                    if (Live(transforms[i])) {
                        haveLive = true;
                        break;
                    }
                }
            }

            if (haveLive || rescans >= MaxRescans || Time.time < nextRescan)
                return;

            rescans++;
            nextRescan = Time.time + 0.5f;

            try { BindTransforms(GetComponentsInChildren<TransformSyncable>(true)); }
            catch { }
        }

        public void Awake() {
            Poolee = GetComponent<Poolee>();
            _Cache.Add(gameObject, this);
        }

        public void Start() {
            if (id != 0)
                _PooleeLookup[id] = this;
        }

        /// <summary>
        /// The one PooleeSyncable for this root, added if there is none. When the game clones a
        /// synced object (a pool making more copies) the clone brings a blank one of ours along,
        /// with no id and none of our data. That one is used, and any others go, so every NPC has
        /// exactly one. Two of them fought over its puppet and its hit list.
        /// </summary>
        public static PooleeSyncable Attach(GameObject go) {
            PooleeSyncable keep = null;
            PooleeSyncable[] all = null;
            try { all = go.GetComponents<PooleeSyncable>(); }
            catch { }
            for (int i = 0; all != null && i < all.Length; i++) {
                PooleeSyncable found = all[i];
                if (!Live(found) || found.detached)
                    continue;
                if (keep == null)
                    keep = found;
                else
                    found.Detach();
            }
            if (keep == null)
                keep = go.AddComponent<PooleeSyncable>();
            _Cache.Add(go, keep);
            return keep;
        }

        public void AssignId(ushort spawnId, byte bodies) {
            if (id != 0 && id != spawnId) {
                PooleeSyncable current;
                if (_PooleeLookup.TryGetValue(id, out current) && ReferenceEquals(current, this))
                    _PooleeLookup.Remove(id);
            }
            id = spawnId;
            rbCount = bodies;
            NewLife();
            if (spawnId != 0) {
                _PooleeLookup[spawnId] = this;
                Entanglement.Entities.NpcEntity.Register(this);
            }
            // Searched before it had an id: its behaviours were left off the hit list.
            if (searchedPuppet)
                RegisterBehaviours();
        }

        public void OnDestroy() {
            if (ReferenceEquals(_Cache.Get(gameObject), this))
                _Cache.Remove(gameObject);
            UnregisterBehaviours();
            Entanglement.Entities.NpcEntity.Unregister(this);

            // Only remove the entry if it still points at us, a duplicate id may have overwritten it
            if (_PooleeLookup.TryGetValue(id, out PooleeSyncable current) && current == this)
                _PooleeLookup.Remove(id);
        }

        public void OnSpawn(long ownerId, SimplifiedTransform simplifiedTransform, ushort endsBuild = 0) {
            NewLife();
            MelonCoroutines.Start(CoOnSpawn(ownerId, simplifiedTransform, endsBuild));
        }

        /// <summary>Host: the pool brought this NPC back. Its last life is forgotten.</summary>
        public void HostRespawned() {
            NewLife();
            WakeTick();
        }

        // Spawned, respawned or bound: its death, the config set on it and the corpse scan's notes
        // about it start over. Left over, they kept a level's dead NPCs alive on clients after a
        // respawn (no death or config went out a second time) and unkillable (hits on a corpse).
        void NewLife() {
            boundAt = Time.time;
            ForgetDeath();
            DeclaredDead = false;
            NpcConfigSync.ForgetCopy(this);
            try {
                EnsureSearched();
                if (Live(brain))
                    CorpseCleanup.ForgetDeath(brain);
            }
            catch { }
        }

        public void SetOwner(long ownerId) {
            if (transforms == null)
                return;
            foreach (TransformSyncable sync in transforms) {
                if (!Live(sync))
                    continue;
                sync.ForceOwner(ownerId, false);
            }
            // Fusion NetworkEntity stack — keep NpcEntity / PuppetMaster ownership in sync.
            Entanglement.Entities.NpcEntity.Register(this)?.RefreshPuppet();
        }

        private PuppetMasta.PuppetMaster puppetMaster;
        private StressLevelZero.AI.AIBrain brain;
        private PuppetMasta.BehaviourOmniwheel omni;
        private StressLevelZero.AI.EnemyTurret turret;
        readonly List<OmniProjector.Pinned> pinned = new List<OmniProjector.Pinned>();
        private bool searchedPuppet;
        private bool warnedNoSyncables;
        private float nextMuscleCheck;
        private bool cachedDefaults;
        private float defaultMuscleWeight = 1f;
        private float defaultMuscleSpring;
        private float defaultMuscleDamper;
        private bool defaultUpdateAnchors = true;

        // Only logged on a change, and only with other players present, so solo logs stay quiet.
        string lastBranch;

        void ReportBranch(string branch, bool owned, bool held) {
            if (branch == lastBranch)
                return;
            lastBranch = branch;

            if (PlayerRepresentation.RemoteCount == 0)
                return;

            try {
                EntangleLogger.Debug("[PooleeSync] " + gameObject.name + " #" + id + " -> " + branch
                    + " (owned=" + owned + " held=" + held + " host=" + Node.isServer
                    + " owner=" + CurrentOwner() + ")");
            }
            catch { }
        }

        public long CurrentOwner() {
            if (transforms == null)
                return 0L;
            for (int i = 0; i < transforms.Length; i++) {
                if (Live(transforms[i]))
                    return transforms[i].staleOwner;
            }
            return 0L;
        }

        // The owner is us or someone still in the lobby.
        bool OwnerPresent() {
            long owner = CurrentOwner();
            if (owner == 0L)
                return false;
            if (owner == SteamIntegration.currentUserId)
                return true;
            return PlayerRepresentation.representations.ContainsKey(owner);
        }

        void SendOwner(long owner, bool fighting = false) {
            if (Node.activeNode == null || id == 0)
                return;
            bool host = Node.isServer;
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.NpcOwner,
                new NpcOwnerMessageData {
                    spawnId = id,
                    owner = owner,
                    fighting = fighting,
                    seq = host ? NpcOwnership.Next(id) : NpcOwnership.Current(id),
                    proposal = !host,
                });
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        void RestateTo(long user) {
            if (Node.activeNode == null || id == 0)
                return;
            ushort seq = NpcOwnership.Current(id);
            if (seq == 0)
                seq = NpcOwnership.Next(id);
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.NpcOwner,
                new NpcOwnerMessageData { spawnId = id, owner = CurrentOwner(), fighting = false, seq = seq, proposal = false });
            if (message != null)
                Node.activeNode.SendMessage(user, NetworkChannel.Reliable, message.GetBytes());
        }

        public static void ApplyOwnerMessage(ushort spawnId, long owner, long sender, bool fighting, ushort seq, bool proposal) {
            // Its copy is still being built here; this is applied once it is.
            if (NpcPending.HoldOwner(spawnId, owner, sender, fighting, seq, proposal))
                return;

            PooleeSyncable poolee;
            if (!_PooleeLookup.TryGetValue(spawnId, out poolee) || !Live(poolee))
                return;

            if (Node.isServer) {
                // Grants only come from here.
                if (!proposal)
                    return;

                long current = poolee.CurrentOwner();
                bool fromOwner = sender == current && seq == NpcOwnership.Current(spawnId);
                // A player who hit it asking to run it, unless someone else is holding it.
                bool claim = owner == sender && !poolee.HeldByOther(sender);
                if (!fromOwner && !claim) {
                    poolee.RestateTo(sender);
                    NpcTrace.Log("#" + spawnId + ": " + sender + " asked to hand it to " + owner + " but was out of date (grant "
                        + seq + ", now " + NpcOwnership.Current(spawnId) + "); told them it is " + current + "'s");
                    return;
                }

                poolee.candidateOwner = 0L;
                poolee.SetOwner(owner);
                if (owner == SteamIntegration.currentUserId && fighting)
                    poolee.agroOnTakeover = true;
                poolee.SendOwner(owner, fighting);
                return;
            }

            // A player takes grants from the host only, and never one older than it has.
            if (proposal || sender != SteamIntegration.lobbyOwnerId)
                return;
            if (!NpcOwnership.Accept(spawnId, seq)) {
                NpcTrace.Log("#" + spawnId + ": ignored an old owner grant (" + seq + ", have " + NpcOwnership.Current(spawnId) + ")");
                return;
            }

            poolee.candidateOwner = 0L;
            poolee.SetOwner(owner);

            if (owner == SteamIntegration.currentUserId && fighting)
                poolee.agroOnTakeover = true;
        }

        float nextClaim;

        public void ClaimFight() {
            if (!SteamIntegration.hasLobby || Node.activeNode == null || id == 0)
                return;
            if (Time.time < nextClaim)
                return;

            try {
                EnsureSearched();
                // Dead by its puppet counts: taking over a corpse switched its brain on.
                if (!Live(brain) || NpcStateSync.IsDead(brain))
                    return;
                if (CurrentOwner() == SteamIntegration.currentUserId)
                    return;
                // In someone's hands: it stays theirs. The hit still lands (ReportHit).
                if (HeldByOther(SteamIntegration.currentUserId))
                    return;
            }
            catch { return; }

            nextClaim = Time.time + 1f;
            long me = SteamIntegration.currentUserId;

            if (Node.isServer) {
                candidateOwner = 0L;
                SetOwner(me);
                SendOwner(me, true);
                agroOnTakeover = true;
            }
            else
                SendOwner(me, true);

            EntangleLogger.Debug("[PooleeSync] " + gameObject.name + " #" + id + " was hit by you, asking to run it");
        }

        bool wasHeld;

        void OnReleased() {
            if (Node.isServer || CurrentOwner() != SteamIntegration.currentUserId)
                return;

            try {
                if (!Live(brain) || brain.isDead)
                    return;
                PuppetMasta.BehaviourBaseNav nav = brain.behaviour;
                if (nav != null && nav.sensors != null) {
                    PuppetMasta.BehaviourBaseNav.MentalState state = nav.mentalState;
                    bool fighting = state == PuppetMasta.BehaviourBaseNav.MentalState.Agroed
                        || state == PuppetMasta.BehaviourBaseNav.MentalState.Engaged;
                    if (fighting && BossClawSync.OwnerOf(nav.sensors.target) == SteamIntegration.currentUserId)
                        return;
                }
            }
            catch { }

            long host = SteamIntegration.lobbyOwnerId;
            SetOwner(host);
            SendOwner(host, false);
        }

        const float HandoffHold = 0.75f;
        // Each handoff means the new machine takes over mid-fight; between two players the NPC
        // would otherwise flip back and forth and spend its time catching up.
        const float HandoffTenure = 3f;
        const float EngageRange = 8f;

        const float MaxRangedReach = 40f;

        float EngageReach() {
            float reach = EngageRange;
            try {
                PuppetMasta.BehaviourBaseNav nav = Live(brain) ? brain.behaviour : null;
                if (nav == null)
                    return reach;
                if (Live(omni) && omni.gun != null && nav.gunRange > reach)
                    reach = nav.gunRange;
                if (nav.enableThrowAttack && nav.throwMaxRange > reach)
                    reach = nav.throwMaxRange;
            }
            catch { }
            return Mathf.Min(reach, MaxRangedReach);
        }

        long candidateOwner;
        float candidateSince;

        void ConsiderHandoff() {
            long wanted = EngagedPlayer();
            if (wanted == 0L || Time.time - lastOwnerChange < HandoffTenure) {
                candidateOwner = 0L;
                return;
            }

            if (wanted != candidateOwner) {
                candidateOwner = wanted;
                candidateSince = Time.time;
                return;
            }

            if (Time.time - candidateSince < HandoffHold)
                return;

            candidateOwner = 0L;

            // This player had it moments ago and never ran it (NpcOwnerWatch took it back).
            if (wanted == noHandoffTo && Time.time < noHandoffUntil)
                return;

            if (wanted == PlayerRepresentation.TesterRepId) {
                if (Time.time >= nextDummyReport) {
                    nextDummyReport = Time.time + 10f;
                    string npc = gameObject.name;
                    EntangleLogger.Debug("[PooleeSync] " + npc + " #" + id + " went for the dummy; a real client would get this NPC now");
                }
                return;
            }

            SetOwner(wanted);
            SendOwner(wanted, true);

            try { EntangleLogger.Debug("[PooleeSync] " + gameObject.name + " #" + id + " -> handed to " + wanted + ", it is fighting them"); }
            catch { }
        }

        // The remote player this NPC is going for, or 0.
        long EngagedPlayer() {
            if (!Live(brain) || DeadHere())
                return 0L;

            StressLevelZero.AI.TriggerRefProxy target;
            try {
                PuppetMasta.BehaviourBaseNav behaviour = brain.behaviour;
                if (behaviour == null)
                    return 0L;
                PuppetMasta.BehaviourBaseNav.MentalState state = behaviour.mentalState;
                if (state != PuppetMasta.BehaviourBaseNav.MentalState.Agroed
                    && state != PuppetMasta.BehaviourBaseNav.MentalState.Engaged)
                    return 0L;
                PuppetMasta.SubBehaviourSensors sensors = behaviour.sensors;
                if (sensors == null)
                    return 0L;
                target = sensors.target;
            }
            catch { return 0L; }

            if (target == null)
                return 0L;

            try {
                PlayerRepresentation dummy = PlayerRepresentation.debugRepresentation;
                if (dummy != null && dummy.EnemyProxy != null && dummy.EnemyProxy == target && WithinEngageRange(target))
                    return PlayerRepresentation.TesterRepId;
            }
            catch { }

            long player = BossClawSync.OwnerOf(target);
            if (player == 0L || player == SteamIntegration.currentUserId)
                return 0L;
            if (!PlayerRepresentation.representations.ContainsKey(player))
                return 0L;

            if (!WithinEngageRange(target))
                return 0L;

            return player;
        }

        bool WithinEngageRange(StressLevelZero.AI.TriggerRefProxy target) {
            try {
                float reach = EngageReach();
                for (int i = 0; i < transforms.Length; i++) {
                    if (!Live(transforms[i]))
                        continue;
                    Vector3 self = transforms[i].transform.position;
                    return (target.transform.position - self).sqrMagnitude <= reach * reach;
                }
            }
            catch { }
            return false;
        }

        float nextDummyReport;

        // Set when another machine hands this NPC to us because it is fighting us.
        bool agroOnTakeover;

        void AgroOnTakeover() {
            if (!agroOnTakeover)
                return;
            agroOnTakeover = false;

            try {
                if (!Live(brain) || NpcStateSync.IsDead(brain))
                    return;
                PuppetMasta.BehaviourBaseNav nav = brain.behaviour;
                StressLevelZero.AI.TriggerRefProxy me = PlayerScripts.LocalProxy();
                if (nav == null || me == null)
                    return;
                // Handed over mid-fight while our copy still rested: on its feet first.
                if (FrozenHere())
                    WakePuppet(true);
                nav.SetAgro(me);
                EntangleLogger.Debug("[PooleeSync] " + gameObject.name + " #" + id + " was handed to us, it is fighting you now");
            }
            catch (Exception e) { EntangleLogger.Debug("[PooleeSync] Takeover agro failed: " + e.Message); }
        }
        float nextAttackReport;
        static int dummyReports;
        static int youReports;
        static int targetChangeReports;
        string lastTargetWho;

        void ReportAttackState() {
            if (!Live(brain))
                return;

            try {
                PuppetMasta.BehaviourBaseNav nav = brain.behaviour;
                if (nav == null)
                    return;
                StressLevelZero.AI.TriggerRefProxy target = nav.sensors != null ? nav.sensors.target : null;
                if (target == null)
                    return;

                string who;
                PlayerRepresentation dummy = PlayerRepresentation.debugRepresentation;
                if (dummy != null && dummy.EnemyProxy != null && dummy.EnemyProxy == target)
                    who = "DUMMY";
                else if (BossClawSync.OwnerOf(target) == SteamIntegration.currentUserId)
                    who = "YOU";
                else
                    who = target.name;

                // Every switch of target, so it's clear whether the dummy is ever the one it wants.
                if (who != lastTargetWho) {
                    lastTargetWho = who;
                    if (targetChangeReports < 150) {
                        targetChangeReports++;
                        EntangleLogger.Debug("[Attack] " + gameObject.name + " now targeting " + who);
                    }
                }

                if (Time.time < nextAttackReport)
                    return;
                if (who == "DUMMY") {
                    if (dummyReports >= 100)
                        return;
                    dummyReports++;
                }
                else if (who == "YOU") {
                    if (youReports >= 20)
                        return;
                    youReports++;
                }
                else
                    return;
                nextAttackReport = Time.time + 1f;

                Vector3 self = nav.transform.position;
                for (int i = 0; i < transforms.Length; i++) {
                    if (Live(transforms[i])) {
                        self = transforms[i].transform.position;
                        break;
                    }
                }
                float toChest = target.chestTran ? Vector3.Distance(self, target.chestTran.position) : -1f;
                float toHead = target.targetHead ? Vector3.Distance(self, target.targetHead.position) : -1f;

                string legs = "";
                PuppetMasta.BehaviourPowerLegs power = nav.TryCast<PuppetMasta.BehaviourPowerLegs>();
                if (power != null) {
                    legs = " engaged=" + power.engagedMode + " agro=" + power.agroState + " style=" + power.combatProficiency
                        + " range=" + power.meleeRange.ToString("F1") + "/" + power.desiredMeleeDistance.ToString("F1")
                        + " cooldown=" + (power._cooldownMelee - Time.time).ToString("F1")
                        + " attackUntil=" + (power._meleeActiveUntilTime - Time.time).ToString("F1");
                }

                EntangleLogger.Debug("[Attack] " + gameObject.name + " vs " + who
                    + ": mental=" + nav.mentalState + " loco=" + nav.locoState
                    + " meleeActive=" + nav.meleeAttackActive
                    + " toChest=" + toChest.ToString("F2") + " toHead=" + toHead.ToString("F2") + legs
                    + " hands=" + (target.lfHand ? "yes" : "no") + "/" + (target.lfHandRb ? "rb" : "none"));
            }
            catch (Exception e) { EntangleLogger.Debug("[Attack] report failed: " + e.Message); }
        }

        const float NpcBoneSendInterval = 1f / 20f;

        const float DeadBoneSendInterval = 1f / 10f;

        void TagPuppetBones() {
            if (!Live(puppetMaster) || transforms == null)
                return;

            bool dead = false;
            try { dead = Live(brain) && brain.isDead; }
            catch { }

            float interval = dead ? DeadBoneSendInterval : NpcBoneSendInterval;
            for (int i = 0; i < transforms.Length; i++) {
                TransformSyncable sync = transforms[i];
                if (!Live(sync))
                    continue;
                sync.isPuppetBone = !dead;
                sync.aiDead = dead;
                sync.followKinematic = true;
                if (sync.minSendInterval < interval)
                    sync.minSendInterval = interval;
            }
        }

        bool warnedOrphan;

        public void FixedUpdate() {
            // Handed back at a level change (Detach); nothing more to do here.
            if (detached)
                return;

            try {
                if (PlayerRepresentation.RemoteCount > 0 && Time.time >= nextHealthCheck) {
                    EnsureSearched();
                    SyncHealth(CurrentOwner() == SteamIntegration.currentUserId);
                }
            }
            catch { }

            try {
                TickPuppet();
            }
            catch (ObjectCollectedException) {
                puppetMaster = null;
                brain = null;
                omni = null;
                turret = null;
                searchedPuppet = false;
                cachedDefaults = false;
            }
            catch (Exception) { }
        }

        bool weightsRemoved;
        bool brainDisabledByUs;
        bool aiOffByUs;

        // Owner: leave the puppet alone, only undo what we did to it.
        void SimulateLocally() {
            // Pinned while it followed someone (a Ford's hologram): its pieces move by physics again.
            if (pinned.Count > 0)
                OmniProjector.Unpin(pinned);
            bool wasFollowing = weightsRemoved || aiOffByUs;
            if (weightsRemoved) {
                RestoreWeights();
                weightsRemoved = false;
            }
            SetAiRunning(true);
            if (wasFollowing)
                StandIfUpright();
            if (brainDisabledByUs) {
                if (Live(brain) && NpcStateSync.IsDead(brain))
                    return;
                brainDisabledByUs = false;
                if (Live(brain) && !brain.enabled)
                    brain.enabled = true;
            }
        }

        // Not ours: its own AI off, its muscles off, and its bones steered to the owner's pose with
        // forces. Until the owner's pose comes they hold the pose they have, so a copy never folds
        // up while its owner stands still. A copy the game froze (resting, out of sight) stays
        // frozen until the owner's bones come, as the owner's own copy does.
        void FollowRemote() {
            if (PoseFlowing())
                WakePuppet(true);
            if (!weightsRemoved) {
                cachedDefaults = false;
                CacheDefaults();
                RemoveWeights();
                weightsRemoved = true;
            }
            SetAiRunning(false);
            DisableBrain();
            HoldBones();
        }

        void HoldBones() {
            if (transforms == null)
                return;
            for (int i = 0; i < transforms.Length; i++) {
                TransformSyncable sync = transforms[i];
                if (Live(sync))
                    sync.HoldHere();
            }
        }

        PuppetMasta.BehaviourBaseNav Nav() {
            try { return Live(brain) ? brain.behaviour : null; }
            catch { return null; }
        }

        /// <summary>Mid-swing at something. Anything else touching it is only a bump.</summary>
        public bool MeleeSwinging() {
            try {
                PuppetMasta.BehaviourBaseNav nav = Nav();
                return nav == null || nav.meleeAttackActive;
            }
            catch { return true; }
        }

        // The NPC's AI is its behaviour's own Update (BehaviourBase.Update -> OnUpdate). Switching
        // off the AIBrain only unhooks its death and spawn callbacks, so every follower copy kept
        // thinking: it pushed its hips toward where it wanted to walk, took stun damage from the
        // bumps of following the pose, swung at whoever stood near it and killed itself after
        // twelve get-ups. The behaviour's enabled flag is the game's own off switch: its update,
        // its contact handler and its hit handler all return early while it is off.
        void SetAiRunning(bool run) {
            PuppetMasta.BehaviourBaseNav nav = Nav();
            if (nav == null)
                return;
            try {
                if (run) {
                    if (aiOffByUs) {
                        aiOffByUs = false;
                        if (!nav.enabled)
                            nav.enabled = true;
                    }
                }
                // A corpse plays out its death as usual.
                else if (nav.enabled && !DeadHere()) {
                    nav.enabled = false;
                    aiOffByUs = true;
                }
            }
            catch { }
        }

        // Taking over a copy that followed someone else's: its AI stopped where it was when it
        // began following. If that was on the ground but the body now stands, it would play a
        // get-up from standing; it carries on standing instead. On the ground it gets up as usual.
        void StandIfUpright() {
            try {
                PuppetMasta.BehaviourBaseNav nav = Nav();
                if (nav == null || DeadHere())
                    return;
                PuppetMasta.BehaviourBaseNav.LocoState loco = nav.locoState;
                if (loco != PuppetMasta.BehaviourBaseNav.LocoState.Fallen && loco != PuppetMasta.BehaviourBaseNav.LocoState.GetUp)
                    return;
                PuppetMasta.SubBehaviourSensors sensors = nav.sensors;
                if (sensors == null || sensors.HipsUpWorld().y < 0.6f)
                    return;
                nav.SwitchLocoState(PuppetMasta.BehaviourBaseNav.LocoState.Standing, 0f, true);
            }
            catch { }
        }

        /// <summary>Another player's death of this NPC is about to be played out here.</summary>
        public void BeforeRemoteDeath() {
            remoteDeath = true;
            staleDeath = false;
            // The death plays out through the behaviour (death sounds, senses off).
            SetAiRunning(true);
        }

        // Died here while someone else ran it, with no death from them: their copy is alive.
        bool remoteDeath;
        bool staleDeath;
        bool seenAlive;
        long lastRemoteOwner;

        void ForgetDeath() {
            remoteDeath = false;
            staleDeath = false;
            seenAlive = false;
        }

        /// <summary>Dead here but alive where it is run; that death is not ours to report.</summary>
        public bool StaleDeath => staleDeath;

        // Called each tick. A follower copy can still die on its own (a lethal gib from our own
        // shot); taking it over must not then tell everyone it died.
        void WatchDeath(bool owned) {
            bool dead = DeadHere();
            if (!dead) {
                staleDeath = false;
                seenAlive = true;
                return;
            }
            // Placed or spawned dead (the level's corpses) is not a death we missed.
            if (!owned && seenAlive && !remoteDeath && !staleDeath) {
                staleDeath = true;
                NpcTrace.Log("#" + id + " " + NpcTrace.Name(gameObject) + " died here while " + CurrentOwner() + " runs it; their copy decides");
            }
        }

        // Ours, but our copy is a stale corpse: hand it back to a machine where it lives.
        bool ReturnStale() {
            if (!staleDeath)
                return false;
            long back = Node.isServer ? lastRemoteOwner : SteamIntegration.lobbyOwnerId;
            if (back == 0L || back == SteamIntegration.currentUserId || !PlayerRepresentation.representations.ContainsKey(back)) {
                // Nowhere alive to send it: the death stands.
                staleDeath = false;
                return false;
            }
            SetOwner(back);
            SendOwner(back);
            NpcTrace.Log("#" + id + " was handed to us dead; gave it back to " + back + ", whose copy is alive");
            return true;
        }

        void DisableBrain() {
            if (Live(brain) && brain.enabled) {
                brain.enabled = false;
                brainDisabledByUs = true;
            }
        }

        void TickPuppet() {
            if (Time.time < nextMuscleCheck)
                return;
            nextMuscleCheck = Time.time + 0.25f;

            EnsureTransforms();

            if (transforms == null || transforms.Length == 0)
                return;

            EnsureSearched();
            TagPuppetBones();

            try {
                if (Live(brain) && !brain.isDead)
                    CorpseCleanup.MarkAlive(brain);
            }
            catch { }

            if (EntangleLogger.verbose && CurrentOwner() == SteamIntegration.currentUserId)
                ReportAttackState();

            if (PlayerRepresentation.RemoteCount == 0) {
                SimulateLocally();
                if (Live(turret))
                    TurretSync.TickAuthority(this, turret, true, false);
                // Solo with the test dummy: the handoff check only reports.
                else if (PlayerRepresentation.debugRepresentation != null)
                    ConsiderHandoff();
                return;
            }

            bool haveSyncables = false;
            for (int i = 0; i < transforms.Length; i++) {
                if (Live(transforms[i])) {
                    haveSyncables = true;
                    break;
                }
            }

            // Nothing to infer ownership from, so use the usual rule: host simulates, clients follow.
            if (!haveSyncables) {
                if (!warnedNoSyncables) {
                    warnedNoSyncables = true;
                    EntangleLogger.Warn("[PooleeSync] " + gameObject.name + " has no live syncables; "
                        + "falling back to host-simulates so it does not freeze.");
                }

                if (Node.isServer) {
                    ReportBranch("no syncables, host runs AI", false, false);
                    SimulateLocally();
                }
                else {
                    ReportBranch("no syncables, limp on client", false, false);
                    FollowRemote();
                }
                return;
            }

            bool owned = false;
            bool held = false;
            for (int i = 0; i < transforms.Length; i++) {
                TransformSyncable sync = transforms[i];
                if (!Live(sync)) {
                    transforms[i] = null;
                    continue;
                }
                if (sync.IsOwner())
                    owned = true;
                if (sync.ownerQueue != null && sync.ownerQueue.Count > 0)
                    held = true;
            }

            if (wasHeld && !held) {
                OnReleased();
                owned = CurrentOwner() == SteamIntegration.currentUserId;
            }
            wasHeld = held;

            if (!owned) {
                long runner = CurrentOwner();
                if (runner != 0L && runner != SteamIntegration.currentUserId)
                    lastRemoteOwner = runner;
            }
            WatchDeath(owned);
            if (owned && !held && ReturnStale())
                owned = false;

            if (!owned && !held && Node.isServer && !OwnerPresent()) {
                long was = CurrentOwner();
                SetOwner(SteamIntegration.currentUserId);
                SendOwner(SteamIntegration.currentUserId);
                owned = true;
                if (!warnedOrphan) {
                    warnedOrphan = true;
                    EntangleLogger.Warn("[PooleeSync] " + gameObject.name + " #" + id + " was owned by "
                        + was + ", who is not in the lobby, so the host took it");
                }
            }

            // Ford projector: a non-owner pins the hologram pieces instead of zeroing weights.
            if (Live(omni)) {
                bool follow = !owned;
                ReportBranch(follow ? "omni pinned" : "omni driving", owned, held);
                if (follow) {
                    OmniProjector.Pin(omni, pinned);
                    OmniProjector.KeepUpright(omni);
                    DisableBrain();
                    HoldBones();
                }
                else {
                    SimulateLocally();
                    AgroOnTakeover();
                    ConsiderHandoff();
                }
                return;
            }

            // A turret's AI runs on one machine: that of a player it can see (TurretSync).
            if (Live(turret)) {
                TurretSync.TickAuthority(this, turret, owned, held);
                return;
            }

            if (!Live(puppetMaster) && !Live(brain))
                return;

            if (held) {
                ReportBranch(owned ? "held, running here" : "held by someone else, following", owned, held);
                if (owned) {
                    SimulateLocally();
                    if (!DeclaredDead) {
                        WakePuppet(true);
                        AgroOnTakeover();
                    }
                }
                else
                    FollowRemote();
                return;
            }

            ReportBranch(owned ? "owned, AI on" : "not owned, following its owner", owned, held);

            if (owned) {
                SimulateLocally();
                if (!DeclaredDead) {
                    AgroOnTakeover();
                    ConsiderHandoff();
                }
            }
            else
                FollowRemote();
        }

        // When this copy got its id (spawned, reused or bound), for PoseArrived.
        float boundAt = -1f;

        public bool PoseArrived() {
            if (transforms == null)
                return false;
            for (int i = 0; i < transforms.Length; i++) {
                TransformSyncable sync = transforms[i];
                if (Live(sync) && sync.lastRemotePoseAt >= 0f && sync.lastRemotePoseAt >= boundAt)
                    return true;
            }
            return false;
        }

        const float PoseFlowWindow = 1f;

        /// <summary>A bone of this copy's owner arrived within the last second.</summary>
        public bool PoseFlowing() {
            if (transforms == null)
                return false;
            float now = Time.time;
            for (int i = 0; i < transforms.Length; i++) {
                TransformSyncable sync = transforms[i];
                if (Live(sync) && sync.lastRemotePoseAt >= boundAt && now - sync.lastRemotePoseAt < PoseFlowWindow)
                    return true;
            }
            return false;
        }

        /// <summary>Alive but frozen in place by the game (resting), on this machine.</summary>
        public bool FrozenHere() {
            try { return Live(brain) && NpcStateSync.IsFrozen(brain); }
            catch { return false; }
        }

        bool DeadHere() {
            try { return Live(brain) && NpcStateSync.IsDead(brain); }
            catch { return false; }
        }

        void WakePuppet(bool active) {
            if (!Live(puppetMaster) || !Live(brain))
                return;
            try {
                if (brain.isDead)
                    return;
                if (puppetMaster.state == PuppetMasta.PuppetMaster.State.Frozen)
                    puppetMaster.state = PuppetMasta.PuppetMaster.State.Alive;
                if (active && puppetMaster.mode != PuppetMasta.PuppetMaster.Mode.Active)
                    puppetMaster.mode = PuppetMasta.PuppetMaster.Mode.Active;
            }
            catch { }
        }

        void EnsureSearched() {
            if (searchedPuppet)
                return;
            searchedPuppet = true;
            try { puppetMaster = GetComponentInChildren<PuppetMasta.PuppetMaster>(true); }
            catch (ObjectCollectedException) { puppetMaster = null; }
            try { brain = GetComponentInChildren<StressLevelZero.AI.AIBrain>(true); }
            catch (ObjectCollectedException) { brain = null; }
            try { omni = GetComponentInChildren<PuppetMasta.BehaviourOmniwheel>(true); }
            catch (ObjectCollectedException) { omni = null; }
            try { turret = GetComponentInChildren<StressLevelZero.AI.EnemyTurret>(true); }
            catch (ObjectCollectedException) { turret = null; }
            RegisterBehaviours();
        }

        /// <summary>Its brain, or null (turrets and props have none).</summary>
        public StressLevelZero.AI.AIBrain Brain {
            get {
                EnsureSearched();
                return Live(brain) ? brain : null;
            }
        }

        // ---- hits and contacts (Patching.NpcHitHook, NpcCollisionHook) ------------------------------

        // Every behaviour of every synced NPC, by native pointer: the hooks get only that.
        static readonly Dictionary<IntPtr, PooleeSyncable> byBehaviour = new Dictionary<IntPtr, PooleeSyncable>();
        readonly List<IntPtr> behaviours = new List<IntPtr>();

        void RegisterBehaviours() {
            UnregisterBehaviours();
            // A blank copy or one being let go must not take an NPC's hits from its real one.
            if (id == 0 || detached)
                return;
            try {
                if (Live(puppetMaster)) {
                    var all = puppetMaster.behaviours;
                    for (int i = 0; all != null && i < all.Length; i++) {
                        if (all[i] != null)
                            Track(all[i].Pointer);
                    }
                }
                PuppetMasta.BehaviourBaseNav nav = Nav();
                if (nav != null)
                    Track(nav.Pointer);
            }
            catch { }
        }

        void Track(IntPtr behaviour) {
            if (behaviour == IntPtr.Zero || behaviours.Contains(behaviour))
                return;
            behaviours.Add(behaviour);
            byBehaviour[behaviour] = this;
        }

        void UnregisterBehaviours() {
            for (int i = 0; i < behaviours.Count; i++) {
                PooleeSyncable current;
                if (byBehaviour.TryGetValue(behaviours[i], out current) && ReferenceEquals(current, this))
                    byBehaviour.Remove(behaviours[i]);
            }
            behaviours.Clear();
        }

        /// <summary>The synced NPC a behaviour belongs to, or null.</summary>
        public static PooleeSyncable OfBehaviour(IntPtr behaviour) {
            PooleeSyncable npc;
            if (byBehaviour.TryGetValue(behaviour, out npc) && !ReferenceEquals(npc, null) && !npc.detached && npc.id != 0)
                return npc;
            return Resolve(behaviour);
        }

        // Behaviours with no synced NPC behind them, and when to look again.
        static readonly Dictionary<IntPtr, float> unsyncedBehaviours = new Dictionary<IntPtr, float>();

        // Not on the list: found from the behaviour's own object instead, so a hit on a synced NPC
        // is never taken for a hit on an unsynced one.
        static PooleeSyncable Resolve(IntPtr behaviour) {
            if (behaviour == IntPtr.Zero)
                return null;
            float now = Time.unscaledTime;
            float retry;
            if (unsyncedBehaviours.TryGetValue(behaviour, out retry) && now < retry)
                return null;

            PooleeSyncable npc = null;
            try { npc = SceneEventSync.FindPooleeSyncable(new PuppetMasta.BehaviourBase(behaviour).transform); }
            catch { npc = null; }
            if (!Live(npc) || npc.detached || npc.id == 0) {
                unsyncedBehaviours[behaviour] = now + 2f;
                return null;
            }
            unsyncedBehaviours.Remove(behaviour);
            npc.Track(behaviour);
            return npc;
        }

        /// <summary>For the solo hit check: why a hit on this NPC would not reach it, or null.</summary>
        public string HitRoutingProblem() {
            try {
                if (id == 0)
                    return "it has no id";
                PooleeSyncable[] all = GetComponents<PooleeSyncable>();
                int live = 0;
                for (int i = 0; all != null && i < all.Length; i++) {
                    if (Live(all[i]) && !all[i].detached)
                        live++;
                }
                if (live != 1)
                    return live + " sync components on it";
                EnsureSearched();
                if (Live(puppetMaster)) {
                    var list = puppetMaster.behaviours;
                    for (int i = 0; list != null && i < list.Length; i++) {
                        if (list[i] != null && !ReferenceEquals(OfBehaviour(list[i].Pointer), this))
                            return "its behaviour " + list[i].name + " is not routed to it";
                    }
                }
                PuppetMasta.BehaviourBaseNav nav = Nav();
                if (nav != null && !ReferenceEquals(OfBehaviour(nav.Pointer), this))
                    return "its brain's behaviour is not routed to it";
                return null;
            }
            catch (Exception e) { return "it could not be checked (" + e.Message + ")"; }
        }

        /// <summary>This machine runs it: its owner, or nobody to share it with.</summary>
        public bool RunsHere {
            get {
                if (PlayerRepresentation.representations.Count == 0)
                    return true;
                long owner = CurrentOwner();
                return owner == 0L || owner == SteamIntegration.currentUserId;
            }
        }


        /// <summary>Fusion-style NetworkEntity extender for this poolee (creates if missing).</summary>
        public Entanglement.Entities.NpcEntity Entity => Entanglement.Entities.NpcEntity.Of(this);
        void CacheDefaults() {
            if (cachedDefaults || !Live(puppetMaster))
                return;
            cachedDefaults = true;
            try {
                defaultMuscleWeight = puppetMaster.muscleWeight;
                if (defaultMuscleWeight < 0.1f)
                    defaultMuscleWeight = 1f;
                defaultMuscleSpring = puppetMaster.muscleSpring;
                defaultMuscleDamper = puppetMaster.muscleDamper;
                defaultUpdateAnchors = puppetMaster.updateJointAnchors;
            }
            catch {
                cachedDefaults = false;
            }
        }

        void RestoreWeights() {
            if (!Live(puppetMaster))
                return;
            try {
                puppetMaster.muscleSpring = defaultMuscleSpring;
                puppetMaster.muscleDamper = defaultMuscleDamper;
                puppetMaster.updateJointAnchors = defaultUpdateAnchors;
            }
            catch { }
        }

        void RemoveWeights() {
            if (!Live(puppetMaster))
                return;
            try {
                // Keep muscleWeight - Fusion zeros spring/damper, not weight (weight=0 looks frozen)
                puppetMaster.muscleSpring = 0f;
                puppetMaster.muscleDamper = 0f;
                puppetMaster.updateJointAnchors = false;
            }
            catch { }
        }

        public IEnumerator CoOnSpawn(long ownerId, SimplifiedTransform simplifiedTransform, ushort endsBuild = 0) {
            try {
                // A pooled NPC is often the last corpse that went back in. Skipping the host's
                // OnSpawn left that ragdoll standing on the client (or a frozen leftover). Always
                // run the pool spawn the way the host did.
                bool restart = NeedsRestart() || IsNpcRoot();

                gameObject.SetActive(false);
                yield return null;
                simplifiedTransform.Apply(transform);
                gameObject.SetActive(true);

                if (restart)
                    RestartLikeHost();

                SetOwner(ownerId);
            }
            finally {
                // A SpawnClient that reused this copy held messages for it until now.
                if (endsBuild != 0)
                    NpcPending.EndBuild(endsBuild);
            }
        }

        bool NeedsRestart() {
            try {
                if (!gameObject.activeSelf)
                    return true;
                EnsureSearched();
                if (Live(brain) && brain.isDead)
                    return true;
                // Frozen is a resting NPC, not a dead one: it stands as the host's does.
                if (Live(puppetMaster) && puppetMaster.isDead)
                    return true;
            }
            catch { }
            return false;
        }

        public void RestartLikeHost() {
            if (DeclaredDead)
                return;
            Poolee poolee = Live(Poolee) ? Poolee : GetComponent<Poolee>();
            if (!Live(poolee))
                return;

            // Our own pool hook sees this as a local spawn otherwise.
            bool wasOverride = SpawnManager.SpawnOverride;
            SpawnManager.SpawnOverride = true;
            try {
                if (Patching.GameCalls.PooleeOnSpawn(poolee, transform.localScale))
                    NpcTrace.Log("restarted #" + id + " " + NpcTrace.Name(gameObject) + " the way the host's pool did");
                else
                    EntangleLogger.Debug("[PooleeSync] Could not restart " + gameObject.name + " #" + id);
            }
            catch (Exception e) {
                EntangleLogger.Debug("[PooleeSync] Could not restart " + gameObject.name + " #" + id + ": " + e.Message);
            }
            finally {
                SpawnManager.SpawnOverride = wasOverride;
            }

            if (weightsRemoved)
                RemoveWeights();
            WakeTick();
        }

        public static Rigidbody[] SyncBodies(GameObject root) {
            if (!root)
                return new Rigidbody[0];
            return OmniProjector.SyncBodies(root);
        }

        static bool Live(UnityEngine.Object obj) => ObjectSync.Live(obj);

        // ---- NPC bodies ---------------------------------------------------------------------------

        /// <summary>The synced NPC (or turret) a body belongs to, or null.</summary>
        public static PooleeSyncable SyncedNpcOf(Transform t) {
            PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(t);
            if (!Live(poolee))
                return null;
            return poolee.IsNpcRoot() ? poolee : null;
        }

        // Asked on every contact, so it is worked out once per object.
        int npcKind = -1;

        bool IsNpcRoot() {
            if (npcKind < 0) {
                try { npcKind = GetComponentInChildren<StressLevelZero.AI.AIBrain>(true) || TurretSync.IsTurret(gameObject) ? 1 : 0; }
                catch { return false; }
            }
            return npcKind == 1;
        }

        // Pooled objects without a PooleeSyncable, by Poolee instance: holds an NPC or not.
        static readonly Dictionary<int, bool> npcPoolees = new Dictionary<int, bool>();

        public static void OnSceneCleanup() {
            npcPoolees.Clear();
            DetachAll();
            byBehaviour.Clear();
            unsyncedBehaviours.Clear();
            Patching.HitSource.OnSceneCleanup();
            Patching.NpcMeleeHook.OnSceneCleanup();
        }

        static void DetachAll() {
            List<PooleeSyncable> all = new List<PooleeSyncable>();
            try {
                foreach (PooleeSyncable poolee in _PooleeLookup.Values)
                    all.Add(poolee);
                foreach (PooleeSyncable poolee in _Cache.m_Cache.Values) {
                    bool listed = false;
                    for (int i = 0; i < all.Count && !listed; i++)
                        listed = ReferenceEquals(all[i], poolee);
                    if (!listed)
                        all.Add(poolee);
                }
            }
            catch { }

            _PooleeLookup.Clear();

            for (int i = 0; i < all.Count; i++) {
                try {
                    if (Live(all[i]))
                        all[i].Detach();
                }
                catch { }
            }
        }

        // Set when the level is going away; Destroy only happens at the end of the frame.
        bool detached;

        void Detach() {
            try { SimulateLocally(); }
            catch { }
            UnregisterBehaviours();
            detached = true;
            transforms = null;
            id = 0;
            Destroy(this);
        }

        /// <summary>Living NPCs this machine runs, and ones it follows, for the state log.</summary>
        public static void CountNpcs(out int here, out int followed) {
            here = 0;
            followed = 0;
            long me = SteamIntegration.currentUserId;
            foreach (PooleeSyncable poolee in _PooleeLookup.Values) {
                try {
                    if (!Live(poolee))
                        continue;
                    poolee.EnsureSearched();
                    if (!Live(poolee.brain) || poolee.brain.isDead)
                        continue;
                    if (poolee.CurrentOwner() == me)
                        here++;
                    else
                        followed++;
                }
                catch { }
            }
        }

        public static bool IsNpcBody(Transform t) {
            if (!t)
                return false;
            try {
                if (SyncedNpcOf(t) != null)
                    return true;
                if (t.GetComponentInParent<PuppetMasta.PuppetMaster>() || t.GetComponentInParent<PuppetMasta.BehaviourOmniwheel>())
                    return true;
                for (Transform current = t; current; current = current.parent) {
                    Poolee poolee = Poolee.Cache.Get(current.gameObject);
                    if (!poolee)
                        continue;
                    int key = poolee.GetInstanceID();
                    bool npc;
                    if (!npcPoolees.TryGetValue(key, out npc)) {
                        npc = poolee.GetComponentInChildren<StressLevelZero.AI.AIBrain>(true);
                        npcPoolees[key] = npc;
                    }
                    return npc;
                }
            }
            catch { }
            return false;
        }

        public void Hold(bool grab) {
            EnsureTransforms();
            if (transforms == null)
                return;
            for (int i = 0; i < transforms.Length; i++) {
                TransformSyncable sync = transforms[i];
                if (!Live(sync) || sync.objectId == 0)
                    continue;
                if (grab)
                    sync.SendEnqueue();
                else
                    sync.SendDequeue();
            }
            WakeTick();
        }

        const float HealthInterval = 0.05f;
        float nextHealthCheck;
        float sharedHp = -1f;  // owner: the health last sent out
        float knownHp = -1f;   // follower: the health last heard from the owner

        PuppetMasta.SubBehaviourHealth Health() {
            try {
                PuppetMasta.BehaviourBaseNav nav = Live(brain) ? brain.behaviour : null;
                return nav != null ? nav.health : null;
            }
            catch { return null; }
        }

        void SyncHealth(bool owned) {
            if (Time.time < nextHealthCheck || id == 0)
                return;
            nextHealthCheck = Time.time + HealthInterval;

            try {
                if (!Live(brain) || brain.isDead)
                    return;
                PuppetMasta.SubBehaviourHealth health = Health();
                if (health == null)
                    return;

                // Only the machine that runs it changes its health; everyone else is told.
                if (!owned) {
                    sharedHp = -1f;
                    return;
                }

                // Every loss at once; slow recovery in steps.
                float hp = health.cur_hp;
                bool hurt = sharedHp >= 0f && hp < sharedHp - 0.001f;
                if (sharedHp < 0f || hurt || Math.Abs(hp - sharedHp) >= 0.25f) {
                    sharedHp = hp;
                    SendHealth(hp);
                }
            }
            catch { }
        }

        void SendHealth(float value) {
            if (Node.activeNode == null)
                return;
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.NpcHealth,
                new NpcHealthMessageData { spawnId = id, value = value });
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        /// <summary>Follower: the owner's health for it.</summary>
        public void ApplyHealth(float value) {
            try {
                EnsureSearched();
                if (!Live(brain) || brain.isDead || CurrentOwner() == SteamIntegration.currentUserId)
                    return;
                PuppetMasta.SubBehaviourHealth health = Health();
                if (health == null)
                    return;

                float drop = knownHp >= 0f ? knownHp - value : 0f;
                health.cur_hp = value;
                knownHp = value;
                if (drop > 0.001f && value > 0f && Time.realtimeSinceStartup - ownHitAt > OwnHitEcho)
                    Pain(drop);
            }
            catch { }
        }

        const float HitGap = 0.15f;       // one report per contact, not one per finger
        const float BluntGap = 0.3f;
        float nextBluntForward;
        const float MaxHitPush = 25f;     // N*s; a hard swing, not a launch
        float nextHitReport;

        /// <summary>
        /// Anyone other than <paramref name="except"/> holding it, as far as this machine knows.
        /// </summary>
        public bool HeldByOther(long except) {
            if (transforms == null)
                return false;
            for (int i = 0; i < transforms.Length; i++) {
                TransformSyncable sync = transforms[i];
                if (!Live(sync) || sync.ownerQueue == null)
                    continue;
                for (int q = 0; q < sync.ownerQueue.Count; q++) {
                    if (sync.ownerQueue[q] != except)
                        return true;
                }
            }
            return false;
        }

        int MuscleOf(Rigidbody body) {
            try {
                if (!Live(puppetMaster) || !body)
                    return -1;
                var muscles = puppetMaster.muscles;
                for (int i = 0; muscles != null && i < muscles.Length; i++) {
                    if (muscles[i] != null && muscles[i].rigidbody == body)
                        return i;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>Attacker: something we hold or threw struck an NPC copy. Its contact goes to whoever runs it.</summary>
        public static bool ReportLocalHit(Collision c, float push, Vector3 from) {
            if (!SteamIntegration.hasLobby || c == null)
                return false;
            try {
                Rigidbody bone = c.rigidbody;
                if (!bone)
                    return false;
                PooleeSyncable npc = SyncedNpcOf(bone.transform);
                return npc != null && npc.ReportHit(c, bone, push, from);
            }
            catch { return false; }
        }

        // The impulse below which the game's contact handler ignores a contact.
        const float MinImpact = 1f;

        /// <summary>Attacker: the local player hit our copy of this NPC, which someone else runs.</summary>
        public bool ReportHit(Collision c, Rigidbody bone, float push, Vector3 from) {
            if (id == 0 || Node.activeNode == null || RunsHere)
                return false;
            if (Time.time < nextHitReport)
                return false;

            EnsureSearched();
            // The local copy of a nullbody is often still the pool's last corpse. That used to
            // drop the punch before it was sent, so clients could hit omnis (never flagged dead)
            // and nothing else. The owner decides whether the real NPC is dead.
            if (!Live(brain))
                return false;
            PuppetMasta.BehaviourBaseNav nav = brain.behaviour;
            if (nav == null)
                return false;

            // The same impulse the owner's game would take from this contact (the size of the
            // contact's impulse, which is the same from either side).
            float damage = 0f;
            try { damage = nav.ProcessImpulse(c, bone); }
            catch { damage = 0f; }
            if (!(damage >= MinImpact))
                return false;

            Vector3 origin = bone.worldCenterOfMass;
            Vector3 normal = Vector3.up;
            try {
                if (c.contactCount > 0) {
                    ContactPoint contact = c.GetContact(0);
                    origin = contact.point;
                    normal = contact.normal;
                }
            }
            catch { }

            Vector3 direction = bone.worldCenterOfMass - from;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : -normal;
            Vector3 force = direction * Mathf.Clamp(push, 0f, MaxHitPush);

            nextHitReport = Time.time + HitGap;
            ownHitAt = Time.realtimeSinceStartup;
            Pain(damage);

            SendHit(new NpcHitMessageData {
                spawnId = id,
                attacker = SteamIntegration.currentUserId,
                kind = NpcHitKind.Impact,
                muscle = (short)MuscleOf(bone),
                damage = damage,
                attackType = (byte)StressLevelZero.Combat.AttackType.Blunt,
                origin = origin,
                normal = normal,
                force = force,
            });
            NpcTrace.Log("#" + id + " struck by us (impulse " + damage.ToString("F2") + "), sent to " + CurrentOwner());
            return true;
        }

        void SendHit(NpcHitMessageData hit) {
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.NpcHit, hit);
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }

        /// <summary>Attacker: our bullet, fist or blade hit this copy, which someone else runs.
        /// <paramref name="hit"/> is the game's MuscleHit, as NpcHitHook got it.</summary>
        public unsafe void ForwardAttack(IntPtr hit) {
            if (id == 0 || Node.activeNode == null)
                return;
            // A fist or prop resting against (or jittering on) a copy that is steered by forces kept
            // meeting it again every few physics steps, and each contact was a punch. A blunt hit is
            // sent once per BluntGap; bullets, blades and explosions are not held back.
            if (*(int*)((byte*)hit + Patching.HitLayout.attack + Patching.HitLayout.attackType) == (int)StressLevelZero.Combat.AttackType.Blunt) {
                if (Time.time < nextBluntForward)
                    return;
                nextBluntForward = Time.time + BluntGap;
            }
            byte* raw = (byte*)hit;
            byte* attack = raw + Patching.HitLayout.attack;
            float damage = *(float*)(attack + Patching.HitLayout.damage);
            Vector3 force = *(Vector3*)(attack + Patching.HitLayout.force);
            if (!(damage > 0f) && FastMath.SqrMag(force) < 0.0001f)
                return;

            int type = *(int*)(attack + Patching.HitLayout.attackType);
            ownHitAt = Time.realtimeSinceStartup;
            Pain(damage);

            SendHit(new NpcHitMessageData {
                spawnId = id,
                attacker = SteamIntegration.currentUserId,
                kind = NpcHitKind.Attack,
                muscle = (short)*(int*)(raw + Patching.HitLayout.muscleIndex),
                damage = damage,
                attackType = (byte)type,
                origin = *(Vector3*)(attack + Patching.HitLayout.origin),
                normal = *(Vector3*)(attack + Patching.HitLayout.normal),
                force = force,
            });
            NpcTrace.Log("#" + id + " hit by us (" + (StressLevelZero.Combat.AttackType)type + ", " + damage.ToString("F2") + "), sent to " + CurrentOwner());
        }

        /// <summary>Owner: another player's hit landed on their copy of this NPC. It plays out here
        /// through the same game code as one of ours.</summary>
        public void ApplyHit(NpcHitMessageData hit) {
            if (CurrentOwner() != SteamIntegration.currentUserId)
                return;
            try {
                EnsureSearched();
                if (!Live(brain) || brain.isDead || NpcStateSync.IsDead(brain)) {
                    // Dead here, so the attacker's copy missed that: it dies there too, rather
                    // than standing about taking hits that can't land.
                    TellDead(hit.attacker);
                    return;
                }

                Rigidbody bone = null;
                Collider collider = null;
                try {
                    var muscles = Live(puppetMaster) ? puppetMaster.muscles : null;
                    if (muscles != null && hit.muscle >= 0 && hit.muscle < muscles.Length && muscles[hit.muscle] != null) {
                        bone = muscles[hit.muscle].rigidbody;
                        collider = bone ? bone.GetComponent<Collider>() : null;
                    }
                }
                catch { }

                // So it turns on whoever hit it, as it would on us.
                StressLevelZero.AI.TriggerRefProxy proxy = null;
                try {
                    PlayerRepresentation rep;
                    if (PlayerRepresentation.representations.TryGetValue(hit.attacker, out rep) && rep != null)
                        proxy = rep.EnemyProxy;
                }
                catch { }

                PuppetMasta.SubBehaviourHealth health = Health();
                float before = health != null ? health.cur_hp : 0f;

                if (hit.kind == NpcHitKind.Impact)
                    ApplyImpact(hit, bone, proxy);
                else
                    ReplayAttack(hit, collider, proxy);

                float after = health != null ? health.cur_hp : 0f;
                NpcTrace.Log("#" + id + " took " + (before - after).ToString("F3") + " from " + hit.attacker + "'s "
                    + (hit.kind == NpcHitKind.Impact ? "strike" : ((StressLevelZero.Combat.AttackType)hit.attackType).ToString()));
            }
            catch (System.Exception e) { EntangleLogger.Debug("[NpcHit] #" + id + ": " + e.Message); }
        }

        float nextDeadReply;

        void TellDead(long attacker) {
            if (Time.time < nextDeadReply || attacker == 0L || attacker == SteamIntegration.currentUserId)
                return;
            nextDeadReply = Time.time + 1f;
            if (NpcStateSync.SendDeath(attacker, id))
                NpcTrace.Log("#" + id + " was hit by " + attacker + " but is dead here; told them");
        }

        // The game's own OnMuscleHit on each of its behaviours, the way MuscleCollisionBroadcaster.Hit
        // hands it out: damage, the push, the flinch and the grudge against the attacker. Killing
        // blows die the usual way, which AIBrainDeathPatch tells everyone.
        unsafe void ReplayAttack(NpcHitMessageData hit, Collider collider, StressLevelZero.AI.TriggerRefProxy proxy) {
            if (!Patching.HitLayout.Ok || behaviours.Count == 0)
                return;

            int size = Patching.HitLayout.hitSize;
            byte* buffer = stackalloc byte[size];
            for (int i = 0; i < size; i++)
                buffer[i] = 0;

            *(int*)(buffer + Patching.HitLayout.muscleIndex) = hit.muscle >= 0 ? hit.muscle : 0;
            byte* attack = buffer + Patching.HitLayout.attack;
            *(float*)(attack + Patching.HitLayout.damage) = hit.damage;
            *(Vector3*)(attack + Patching.HitLayout.normal) = hit.normal;
            *(Vector3*)(attack + Patching.HitLayout.origin) = hit.origin;
            *(Vector3*)(attack + Patching.HitLayout.force) = hit.force;
            *(int*)(attack + Patching.HitLayout.attackType) = hit.attackType;
            *(IntPtr*)(attack + Patching.HitLayout.collider) = collider ? collider.Pointer : IntPtr.Zero;
            *(IntPtr*)(attack + Patching.HitLayout.proxy) = proxy != null ? proxy.Pointer : IntPtr.Zero;

            IntPtr[] targets = behaviours.ToArray();
            for (int i = 0; i < targets.Length; i++)
                Patching.NpcHitHook.Run(targets[i], (IntPtr)buffer);
        }

        // What the behaviour's contact handler does with a hard contact: the push, then stun and
        // health loss for that body part (SubBehaviourHealth.Stun, which kills at zero).
        void ApplyImpact(NpcHitMessageData hit, Rigidbody bone, StressLevelZero.AI.TriggerRefProxy proxy) {
            PuppetMasta.BehaviourBaseNav nav = Nav();
            PuppetMasta.SubBehaviourHealth health = Health();
            if (nav == null || health == null || hit.muscle < 0)
                return;

            if (bone && FastMath.SqrMag(hit.force) > 0.0001f)
                bone.AddForceAtPosition(hit.force, hit.origin, ForceMode.Impulse);

            if (Time.time < nav._blockCollisionsUntil || health.cur_hp <= 0f)
                return;
            try {
                if (Live(puppetMaster) && puppetMaster.isFrozen)
                    puppetMaster.Resurrect();
            }
            catch { }

            float stunned = health.Stun(hit.muscle, hit.damage);
            if (stunned > 0f)
                Pain(stunned);
            try {
                if (proxy != null && nav.sensors != null)
                    nav.sensors.AddThreat(proxy, hit.damage);
            }
            catch { }
        }

        const float PainGap = 0.35f;
        const float OwnHitEcho = 1f;
        float lastPain = -10f;
        float ownHitAt = -10f;

        void Pain(float amount) {
            if (amount <= 0.001f)
                return;
            float now = Time.realtimeSinceStartup;
            if (now - lastPain < PainGap)
                return;
            lastPain = now;

            try {
                PuppetMasta.BehaviourBaseNav nav = Live(brain) ? brain.behaviour : null;
                if (nav == null)
                    return;
                try {
                    PuppetMasta.SubBehaviourSfx sfx = nav.sfx;
                    if (sfx != null)
                        sfx.Pain(amount);
                }
                catch { }
                try {
                    PuppetMasta.BehaviourPowerLegs legs = nav.TryCast<PuppetMasta.BehaviourPowerLegs>();
                    PuppetMasta.SubBehaviourFaceanim face = legs != null ? legs.faceAnim : null;
                    if (face != null)
                        face.Pain(amount);
                }
                catch { }
            }
            catch { }
        }

        float lastBoneMove = -10f;
        float snapUntil = -10f;

        /// <summary>Owner: one of the bones moved, so send them all.</summary>
        public void NoteBoneMoved() {
            lastBoneMove = Time.time;
        }

        public bool PoseMoving => Time.time - lastBoneMove < 0.15f;

        /// <summary>Follower: a bone is too far off to steer, so every bone jumps this step.</summary>
        public void RequestSnap() {
            snapUntil = Time.fixedTime + Time.fixedDeltaTime * 2.5f;
        }

        public bool SnapPending => Time.fixedTime <= snapUntil;

        /// <summary>An owner change: react on the next physics step, not up to 0.25s later.</summary>
        public void WakeTick() {
            nextMuscleCheck = 0f;
            lastOwnerChange = Time.time;
        }

        float lastOwnerChange = -10f;

        /// <summary>When it last changed hands.</summary>
        public float LastOwnerChange => lastOwnerChange;

        /// <summary>Gives it to another player to run, and tells everyone.</summary>
        public void HandTo(long player) {
            SetOwner(player);
            SendOwner(player, true);
        }
        float nextReassert;
        long wrongSender;
        float wrongSince;
        float wrongLast = -10f;

        internal bool OwnerSilentFor(float seconds) {
            if (transforms == null || id == 0)
                return false;
            try {
                if (!gameObject.activeInHierarchy)
                    return false;
                EnsureSearched();
                if (!Live(brain) || brain.isDead)
                    return false;

                long owner = CurrentOwner();
                if (owner == 0L || owner == SteamIntegration.currentUserId)
                    return false;
                // Gone from the lobby: TickPuppet's orphan check takes it.
                if (!PlayerRepresentation.representations.ContainsKey(owner))
                    return false;

                float now = Time.time;
                if (now - lastOwnerChange < seconds)
                    return false;

                bool any = false;
                float latest = -1f;
                for (int i = 0; i < transforms.Length; i++) {
                    TransformSyncable sync = transforms[i];
                    if (!Live(sync))
                        continue;
                    // Held: whoever holds it runs it, and a still hand sends nothing.
                    if (sync.ownerQueue != null && sync.ownerQueue.Count > 0)
                        return false;
                    any = true;
                    if (sync.netReceiveTime > latest)
                        latest = sync.netReceiveTime;
                }
                return any && now - latest > seconds;
            }
            catch { return false; }
        }

        long noHandoffTo;
        float noHandoffUntil;
        const float NoHandoffSeconds = 10f;

        /// <summary>Host: nobody is running it, so the host does.</summary>
        internal void ReclaimForHost() {
            long was = CurrentOwner();
            long me = SteamIntegration.currentUserId;
            noHandoffTo = was;
            noHandoffUntil = Time.time + NoHandoffSeconds;
            candidateOwner = 0L;
            SetOwner(me);
            SendOwner(me);
            try { EntangleLogger.Debug("[PooleeSync] " + gameObject.name + " #" + id + " was owned by " + was + " but nothing came from them, so the host runs it again"); }
            catch { }
        }

        internal bool ReassertOwner(long sender, float settleSeconds, float sustainSeconds, float cooldown) {
            float now = Time.time;

            if (sender != wrongSender || now - wrongLast > 0.5f) {
                wrongSender = sender;
                wrongSince = now;
            }
            wrongLast = now;
            if (now - wrongSince < sustainSeconds)
                return false;

            if (now - lastOwnerChange < settleSeconds || now < nextReassert)
                return false;

            long owner = CurrentOwner();
            if (owner == 0L || owner == sender)
                return false;

            // Holding it, as far as the host knows: that is theirs to run.
            for (int i = 0; transforms != null && i < transforms.Length; i++) {
                TransformSyncable sync = transforms[i];
                if (Live(sync) && sync.ownerQueue != null && sync.ownerQueue.Contains(sender))
                    return false;
            }

            nextReassert = now + cooldown;

            SendOwner(owner);
            try { EntangleLogger.Debug("[PooleeSync] " + gameObject.name + " #" + id + " was being run by " + sender + " as well; told everyone it is " + owner + "'s"); }
            catch { }
            return true;
        }

        public static void DespawnPoolee(Poolee poolee, bool playVFX = false) {
            if (!Live(poolee))
                return;
            try { Patching.GameCalls.PooleeDespawn(poolee, playVFX, Color.white); }
            catch { }
            // Despawn only switches off what is active in the hierarchy; whatever is left goes too.
            try {
                if (Live(poolee) && poolee.gameObject.activeSelf)
                    poolee.gameObject.SetActive(false);
            }
            catch { }
        }
    }

    public static class OmniProjector {
        public static Rigidbody[] SyncBodies(GameObject root) {
            Rigidbody[] all = root.GetComponentsInChildren<Rigidbody>(true);
            if (all == null || all.Length == 0)
                return all ?? new Rigidbody[0];

            PuppetMasta.BehaviourOmniwheel behaviour = null;
            try { behaviour = root.GetComponentInChildren<PuppetMasta.BehaviourOmniwheel>(true); }
            catch { behaviour = null; }
            if (behaviour)
                return WithoutProjection(all, behaviour);

            // Sync living NPC bones. Owner runs AI; remotes RemoveWeights and follow.
            return all;
        }

        static Rigidbody[] WithoutProjection(Rigidbody[] all, PuppetMasta.BehaviourOmniwheel behaviour) {
            var anchors = new List<Transform>(5);
            Add(anchors, behaviour.hipsProjTransform);
            Add(anchors, behaviour.chestProjTransform);
            Add(anchors, behaviour.headProjTransform);
            Add(anchors, behaviour.gunPropTransform);
            if (behaviour.projectionAnimator)
                Add(anchors, behaviour.projectionAnimator.transform);

            var kept = new List<Rigidbody>(all.Length);
            for (int i = 0; i < all.Length; i++) {
                Rigidbody rb = all[i];
                if (!rb)
                    continue;
                if (IsProjection(rb, anchors))
                    continue;
                kept.Add(rb);
            }
            return kept.ToArray();
        }

        /// <summary>A hologram piece we pinned, and whether it used gravity before.</summary>
        public struct Pinned {
            public Rigidbody body;
            public bool gravity;
        }

        static bool paramsLogged;

        /// <summary>
        /// The copy's wheel is steered to the owner's pose by forces and nothing balances it as its own AI
        /// would, so the projector's balance check read it as tipping over and played the falling
        /// animation whenever it moved (the shooting still worked). The balance timers are held at zero
        /// on copies. The animator's parameters are listed once in the debug log, to name the exact one
        /// if this is not the whole of it.
        /// </summary>
        public static void KeepUpright(PuppetMasta.BehaviourOmniwheel behaviour) {
            if (!behaviour)
                return;
            try {
                behaviour._unbalancedTimer = 0f;
                behaviour._chargeJump = false;
            }
            catch { }

            if (paramsLogged || !EntangleLogger.verbose)
                return;
            paramsLogged = true;
            try {
                Animator anim = behaviour.projectionAnimator;
                if (!anim)
                    return;
                var parameters = anim.parameters;
                System.Text.StringBuilder names = new System.Text.StringBuilder();
                for (int i = 0; parameters != null && i < parameters.Length; i++)
                    names.Append(i > 0 ? ", " : "").Append(parameters[i].name).Append(' ').Append(parameters[i].type);
                EntangleLogger.Debug("[Omni] projection animator parameters: " + names);
            }
            catch { }
        }

        public static void Pin(PuppetMasta.BehaviourOmniwheel behaviour, List<Pinned> pinned) {
            if (!behaviour)
                return;
            if (!behaviour.enabled)
                behaviour.enabled = true;
            if (behaviour.projectionAnimator && !behaviour.projectionAnimator.enabled)
                behaviour.projectionAnimator.enabled = true;

            PinTree(behaviour.hipsProjTransform, pinned);
            PinTree(behaviour.chestProjTransform, pinned);
            PinTree(behaviour.headProjTransform, pinned);
            PinTree(behaviour.gunPropTransform, pinned);
        }

        /// <summary>Taken over: the pieces we pinned move by physics again, as they did before.</summary>
        public static void Unpin(List<Pinned> pinned) {
            for (int i = 0; i < pinned.Count; i++) {
                try {
                    Rigidbody rb = pinned[i].body;
                    if (!rb)
                        continue;
                    rb.isKinematic = false;
                    rb.useGravity = pinned[i].gravity;
                }
                catch { }
            }
            pinned.Clear();
        }

        static void PinTree(Transform root, List<Pinned> pinned) {
            if (!root)
                return;

            Rigidbody self = root.GetComponent<Rigidbody>();
            if (self)
                Freeze(self, pinned);

            Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
            if (bodies == null)
                return;
            for (int i = 0; i < bodies.Length; i++)
                Freeze(bodies[i], pinned);
        }

        static void Freeze(Rigidbody rb, List<Pinned> pinned) {
            if (!rb || rb.isKinematic)
                return;
            pinned.Add(new Pinned { body = rb, gravity = rb.useGravity });
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        static void Add(List<Transform> list, Transform value) {
            if (!value)
                return;
            for (int i = 0; i < list.Count; i++) {
                if (list[i] == value)
                    return;
            }
            list.Add(value);
        }

        static bool IsProjection(Rigidbody rb, List<Transform> anchors) {
            string name = rb.gameObject.name;
            if (!string.IsNullOrEmpty(name) && name.IndexOf("Proj", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            Transform current = rb.transform;
            while (current) {
                for (int i = 0; i < anchors.Count; i++) {
                    if (current == anchors[i])
                        return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
