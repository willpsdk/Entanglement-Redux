using System.Collections;
using System.Collections.Generic;

using MelonLoader;

using PuppetMasta;

using StressLevelZero.AI;
using StressLevelZero.Pool;

using UnityEngine;

namespace Entanglement.Objects
{
    public static class CorpseCleanup {
        public const int MaxCorpses = 15;

        const float Duration = 1.6f;

        const float FollowDuration = 1.4f;

        class Corpse {
            public int id;
            public AIBrain brain;
            public StressLevelZero.Zones.ZoneTracker[] trackers;
            public Transform root;
            public float diedAt;
            public bool fizzling;
            public bool omni;
            public bool followsHost;
            public int hostRun = -1;
        }

        static readonly List<Corpse> corpses = new List<Corpse>();
        static AnimationCurve flickerCurve;
        static int generation;
        static float nextScan;

        public static void Clear() {
            generation++;
            sceneBrains = null;
            corpses.Clear();
            aliveSince.Clear();
            leftAlone.Clear();
            deathSent.Clear();
            pooledBrain.Clear();
        }

        static readonly HashSet<int> deathSent = new HashSet<int>();

        /// <summary>A new life for this NPC: its last death, and how long it lived, are forgotten.</summary>
        public static void ForgetDeath(AIBrain brain) {
            try {
                int key = brain.GetInstanceID();
                deathSent.Remove(key);
                leftAlone.Remove(key);
                aliveSince.Remove(key);
            }
            catch { }
        }

        /// <summary>This death has gone out (or came in from someone else).</summary>
        public static void NoteDeathSent(AIBrain brain) {
            try { deathSent.Add(brain.GetInstanceID()); }
            catch { }
        }

        static void ReportDeath(AIBrain brain, bool pooledOnly = false) {
            if (!Entanglement.Network.SteamIntegration.hasLobby)
                return;
            try {
                int key = brain.GetInstanceID();
                if (deathSent.Contains(key))
                    return;

                PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(brain.transform);
                if (poolee == null) {
                    if (pooledOnly || brain.GetComponentInParent<Poolee>())
                        return;
                }
                else {
                    if (pooledOnly && poolee.pathAddressed)
                        return;
                    // Whoever runs it reports it; asked again next scan in case that becomes us.
                    if (poolee.CurrentOwner() != Entanglement.Network.SteamIntegration.currentUserId)
                        return;
                    // It died here while another player ran it, and lives on their screen.
                    if (poolee.StaleDeath)
                        return;
                }

                deathSent.Add(key);
                if (!SceneEventSync.MarkEvent(SceneEventSync.EventKey(key, Entanglement.Network.SceneEventType.NpcDeath)))
                    return;
                SceneEventSync.SendEvent(Entanglement.Network.SceneEventType.NpcDeath, brain.transform, poolee != null ? poolee.id : (ushort)0);
                NpcTrace.Log("death of " + brain.name + (poolee != null ? " #" + poolee.id : "") + " sent by the corpse scan");
            }
            catch { }
        }

        const float MinLifeToFizzle = 2f;
        const float PlacedSettleTime = 8f;
        static readonly Dictionary<int, float> aliveSince = new Dictionary<int, float>();
        static readonly HashSet<int> leftAlone = new HashSet<int>();
        static readonly Dictionary<int, bool> pooledBrain = new Dictionary<int, bool>();

        static bool Pooled(AIBrain brain, int id) {
            bool pooled;
            if (pooledBrain.TryGetValue(id, out pooled))
                return pooled;
            try { pooled = brain.GetComponentInParent<Poolee>() != null; }
            catch { return false; }
            pooledBrain[id] = pooled;
            return pooled;
        }

        public static void MarkAlive(AIBrain brain) {
            try {
                int id = brain.GetInstanceID();
                if (aliveSince.ContainsKey(id))
                    return;
                // Placed and the level is still starting up: not yet.
                if (!Pooled(brain, id) && SceneEventSync.SinceReady < PlacedSettleTime)
                    return;
                aliveSince[id] = Time.time;
            }
            catch { }
        }

        static bool LivedHere(AIBrain brain) {
            float since;
            return aliveSince.TryGetValue(brain.GetInstanceID(), out since) && Time.time - since >= MinLifeToFizzle;
        }

        public static void Register(AIBrain brain) {
            if (brain == null)
                return;

            if (!LivedHere(brain)) {
                if (leftAlone.Add(brain.GetInstanceID()))
                    { Transform body = Body(brain); NpcTrace.Log("corpse " + (body ? body.name : "?") + " left alone, it was never alive here"); }
                return;
            }

            // A later respawn has to be seen alive again before it can be cleaned up.
            aliveSince.Remove(brain.GetInstanceID());

            Transform root = Body(brain);
            if (!root)
                return;

            int id = root.GetInstanceID();
            for (int i = 0; i < corpses.Count; i++) {
                if (corpses[i].id == id)
                    return;
            }

            bool omni = false;
            try {
                BehaviourOmniwheel wheel = root.GetComponentInChildren<BehaviourOmniwheel>(true);
                omni = wheel;
                if (omni && flickerCurve == null && wheel.deathFlicker != null)
                    flickerCurve = wheel.deathFlicker;
            }
            catch { }

            Corpse corpse = new Corpse();
            corpse.id = id;
            corpse.brain = brain;
            corpse.root = root;

            try {
                corpse.trackers = root.GetComponentsInChildren<StressLevelZero.Zones.ZoneTracker>(false);
                for (int i = 0; i < corpse.trackers.Length; i++) {
                    if (corpse.trackers[i])
                        corpse.trackers[i].enabled = false;
                }
            }
            catch { corpse.trackers = null; }
            corpse.diedAt = Time.time;
            corpse.omni = omni;
            corpses.Add(corpse);
            NpcTrace.Log("corpse " + root.name + " registered, " + corpses.Count + " bodies now");
            Trim();
        }

        // In a lobby the synced NPCs are checked from their list every scan, and the whole scene
        // only now and then (for ones nobody syncs): searching the scene stalled a frame.
        const float ScanEvery = 2f;
        const float SceneScanEvery = 30f;
        static float nextSceneScan;

        // Every AIBrain in the scene. Searching the scene took up to ~20ms in a big level, and the host
        // did it here and in SceneNpcSync: one search now serves both, reused while young enough.
        static AIBrain[] sceneBrains;
        static float sceneBrainsAt = -999f;

        public static AIBrain[] SceneBrains(float maxAge) {
            float now = Time.realtimeSinceStartup;
            if (sceneBrains == null || now - sceneBrainsAt > maxAge) {
                try { sceneBrains = UnityEngine.Object.FindObjectsOfType<AIBrain>(); }
                catch { sceneBrains = null; }
                sceneBrainsAt = now;
            }
            return sceneBrains;
        }

        public static void Tick() {
            if (Time.unscaledTime >= nextScan) {
                nextScan = Time.unscaledTime + ScanEvery;
                bool scene = !Entanglement.Network.SteamIntegration.hasLobby || Time.unscaledTime >= nextSceneScan;
                if (scene)
                    nextSceneScan = Time.unscaledTime + SceneScanEvery;
                ScanDead(scene);
            }

            if (corpses.Count == 0)
                return;

            Prune();
            Trim();
        }

        static void ScanDead(bool scene) {
            if (scene) {
                try {
                    AIBrain[] brains = SceneBrains(3f);
                    for (int i = 0; brains != null && i < brains.Length; i++)
                        Check(brains[i]);
                }
                catch { }
                return;
            }

            foreach (PooleeSyncable poolee in PooleeSyncable._PooleeLookup.Values) {
                try {
                    if (ObjectSync.Live(poolee) && poolee.gameObject.activeInHierarchy)
                        Check(poolee.Brain);
                }
                catch { }
            }
        }

        static void Check(AIBrain brain) {
            if (!brain)
                return;
            try {
                if (brain.isDead) {
                    ReportDeath(brain);
                    Register(brain);
                }
                else if (NpcStateSync.IsDead(brain)) {
                    ReportDeath(brain, true);
                    Register(brain);
                }
                else {
                    MarkAlive(brain);
                    deathSent.Remove(brain.GetInstanceID());
                }
            }
            catch { }
        }

        // transform.root is the scene, so every Ford collapsed into one entry and only one fizzled.
        static Transform Body(AIBrain brain) {
            try {
                Poolee poolee = brain.GetComponentInParent<Poolee>();
                if (poolee)
                    return poolee.transform;

                if (brain.puppetMaster)
                    return brain.puppetMaster.transform;

                return brain.transform;
            }
            catch {
                return null;
            }
        }

        static void Trim() {
            Prune();
            SortOldestFirst();

            int waiting = 0;
            for (int i = 0; i < corpses.Count; i++) {
                if (!corpses[i].fizzling && !HostRuns(corpses[i]))
                    waiting++;
            }
            int extra = waiting - MaxCorpses;

            // Oldest first: the list is sorted, and anything past its lifetime or over the cap goes.
            for (int i = 0; i < corpses.Count; i++) {
                Corpse corpse = corpses[i];
                if (corpse.fizzling || HostRuns(corpse))
                    continue;

                if (extra <= 0)
                    break;

                extra--;
                StartFizzle(corpse, "more than " + MaxCorpses + " bodies");
            }
        }

        static void Prune() {
            for (int i = corpses.Count - 1; i >= 0; i--) {
                Corpse corpse = corpses[i];
                if (Alive(corpse))
                    continue;

                try {
                    if (!corpse.fizzling && corpse.root && !corpse.root.gameObject.activeInHierarchy)
                        NpcTrace.Log("corpse " + corpse.root.name + " was taken by the game before its fizzle, dead "
                            + (Time.time - corpse.diedAt).ToString("F0") + "s");
                }
                catch { }

                RestoreTrackers(corpse);
                corpses.RemoveAt(i);
            }
        }

        static void RestoreTrackers(Corpse corpse) {
            if (corpse == null || corpse.trackers == null)
                return;
            for (int i = 0; i < corpse.trackers.Length; i++) {
                try {
                    if (corpse.trackers[i])
                        corpse.trackers[i].enabled = true;
                }
                catch { }
            }
            corpse.trackers = null;
        }

        static bool Alive(Corpse corpse) {
            if (corpse == null || !corpse.root)
                return false;

            if (!corpse.fizzling && !StillDead(corpse))
                return false;

            try {
                return corpse.root.gameObject.activeInHierarchy;
            }
            catch {
                return false;
            }
        }

        static void SortOldestFirst() {
            for (int i = 0; i < corpses.Count; i++) {
                for (int j = i + 1; j < corpses.Count; j++) {
                    if (corpses[j].diedAt < corpses[i].diedAt) {
                        Corpse tmp = corpses[i];
                        corpses[i] = corpses[j];
                        corpses[j] = tmp;
                    }
                }
            }
        }

        class Saved {
            public Renderer[] renderers;
            public bool[] rendererEnabled;
            public Material[][] materials;
            public Collider[] colliders;
            public bool[] colliderEnabled;
        }

        static Saved Capture(Transform root) {
            Saved saved = new Saved();
            try {
                saved.renderers = root.GetComponentsInChildren<Renderer>(true);
                saved.rendererEnabled = new bool[saved.renderers.Length];
                saved.materials = new Material[saved.renderers.Length][];
                for (int i = 0; i < saved.renderers.Length; i++) {
                    Renderer renderer = saved.renderers[i];
                    if (!renderer)
                        continue;
                    saved.rendererEnabled[i] = renderer.enabled;
                    saved.materials[i] = renderer.sharedMaterials;
                }

                saved.colliders = root.GetComponentsInChildren<Collider>(true);
                saved.colliderEnabled = new bool[saved.colliders.Length];
                for (int i = 0; i < saved.colliders.Length; i++) {
                    if (saved.colliders[i])
                        saved.colliderEnabled[i] = saved.colliders[i].enabled;
                }
            }
            catch { }
            return saved;
        }

        static void Restore(Saved saved) {
            if (saved == null)
                return;
            try {
                if (saved.renderers != null) {
                    for (int i = 0; i < saved.renderers.Length; i++) {
                        Renderer renderer = saved.renderers[i];
                        if (!renderer)
                            continue;
                        try {
                            if (saved.materials[i] != null)
                                renderer.sharedMaterials = saved.materials[i];
                            renderer.enabled = saved.rendererEnabled[i];
                        }
                        catch { }
                    }
                }
                if (saved.colliders != null) {
                    for (int i = 0; i < saved.colliders.Length; i++) {
                        try {
                            if (saved.colliders[i])
                                saved.colliders[i].enabled = saved.colliderEnabled[i];
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        static bool HostRuns(Corpse corpse) {
            if (!Entanglement.Network.SteamIntegration.hasLobby || Entanglement.Network.Node.isServer)
                return false;
            if (corpse.hostRun < 0) {
                corpse.hostRun = 0;
                try {
                    PooleeSyncable poolee = corpse.root ? SceneEventSync.FindPooleeSyncable(corpse.root) : null;
                    if (poolee != null && (poolee.id != 0 || poolee.pathAddressed))
                        corpse.hostRun = 1;
                }
                catch { }
            }
            return corpse.hostRun == 1;
        }

        // Host: its copies fizzle with it.
        static void Announce(Corpse corpse) {
            if (!Entanglement.Network.SteamIntegration.hasLobby || !Entanglement.Network.Node.isServer || !corpse.root)
                return;
            try {
                PooleeSyncable poolee = SceneEventSync.FindPooleeSyncable(corpse.root);
                if (poolee == null || (poolee.id == 0 && !poolee.pathAddressed))
                    return;
                SceneEventSync.SendEvent(Entanglement.Network.SceneEventType.NpcFizzle, poolee.transform, poolee.id, false);
            }
            catch { }
        }

        static bool StillDead(Corpse corpse) {
            if (corpse.followsHost)
                return true;
            try { return corpse.brain && NpcStateSync.IsDead(corpse.brain); }
            catch { return false; }
        }

        static void StartFizzle(Corpse corpse, string why) {
            if (corpse.fizzling)
                return;
            corpse.fizzling = true;
            NpcTrace.Log("corpse " + (corpse.root ? corpse.root.name : "?") + " fizzling, dead "
                + (Time.time - corpse.diedAt).ToString("F0") + "s (" + why + ")");
            Announce(corpse);
            MelonCoroutines.Start(Flicker(corpse, generation));
        }

        public static bool FizzleOldestOf(Pool pool) {
            if (!pool)
                return false;
            Prune();
            SortOldestFirst();
            for (int i = 0; i < corpses.Count; i++) {
                Corpse corpse = corpses[i];
                if (corpse.fizzling || !corpse.root)
                    continue;
                try {
                    Poolee poolee = corpse.root.GetComponent<Poolee>();
                    if (!poolee || poolee.pool != pool)
                        continue;
                }
                catch { continue; }
                StartFizzle(corpse, "its pool needs room");
                return true;
            }
            return false;
        }

        public static bool FizzleNow(Transform root, bool fromHost = false) {
            if (!root)
                return false;
            AIBrain brain;
            try { brain = root.GetComponentInChildren<AIBrain>(true); }
            catch { return false; }
            if (!brain)
                return false;
            // The host already took this body away. Play the flicker even if our copy is
            // still standing: waiting for IsDead left clients with a pop-out instead.
            if (!fromHost && !NpcStateSync.IsDead(brain))
                return false;

            Transform body = Body(brain);
            if (!body)
                return false;

            // Already a body here (Register forgets when it was alive, so this comes first).
            int id = body.GetInstanceID();
            for (int i = 0; i < corpses.Count; i++) {
                if (corpses[i].id != id)
                    continue;
                corpses[i].followsHost = fromHost;
                StartFizzle(corpses[i], "taken away by another player's game");
                return true;
            }

            // Never a corpse the level placed: those are left to whatever the game does with them.
            if (!fromHost && !LivedHere(brain))
                return false;

            Corpse corpse = new Corpse { id = id, brain = brain, root = body, diedAt = Time.time, followsHost = fromHost };
            try {
                corpse.omni = body.GetComponentInChildren<BehaviourOmniwheel>(true);
                corpse.trackers = body.GetComponentsInChildren<StressLevelZero.Zones.ZoneTracker>(false);
                for (int i = 0; i < corpse.trackers.Length; i++) {
                    if (corpse.trackers[i])
                        corpse.trackers[i].enabled = false;
                }
            }
            catch { }
            corpses.Add(corpse);
            StartFizzle(corpse, "taken away by another player's game");
            return true;
        }

        /// <summary>True while this body is running its flicker here.</summary>
        public static bool IsFizzling(Transform root) {
            if (!root)
                return false;
            for (int i = 0; i < corpses.Count; i++) {
                try {
                    if (corpses[i].fizzling && corpses[i].root == root)
                        return true;
                }
                catch { }
            }
            return false;
        }

        static bool StillOut(Transform root) {
            try { return root && root.gameObject.activeInHierarchy; }
            catch { return false; }
        }

        static Material[] Tinted(Renderer renderer, Material[][] tinted, int index) {
            Material[] mats = tinted[index];
            if (mats == null) {
                mats = renderer.materials;
                tinted[index] = mats;
            }
            return mats;
        }

        static void ReleaseTinted(Material[][] tinted, Saved saved) {
            if (tinted == null)
                return;
            for (int i = 0; i < tinted.Length; i++) {
                Material[] mats = tinted[i];
                if (mats == null)
                    continue;
                Material[] originals = saved != null && saved.materials != null && i < saved.materials.Length ? saved.materials[i] : null;
                for (int m = 0; m < mats.Length; m++) {
                    try {
                        Material mat = mats[m];
                        if (!mat || IsOriginal(mat, originals))
                            continue;
                        Object.Destroy(mat);
                    }
                    catch { }
                }
                tinted[i] = null;
            }
        }

        static bool IsOriginal(Material mat, Material[] originals) {
            if (originals == null)
                return false;
            for (int i = 0; i < originals.Length; i++) {
                if (originals[i] == mat)
                    return true;
            }
            return false;
        }

        static IEnumerator Flicker(Corpse corpse, int gen) {
            Transform root = corpse.root;
            CacheCurve();

            if (!root || !StillDead(corpse)) {
                corpses.Remove(corpse);
                yield break;
            }

            Saved saved = Capture(root);

            try {
                if (saved.colliders != null) {
                    for (int i = 0; i < saved.colliders.Length; i++) {
                        if (saved.colliders[i])
                            saved.colliders[i].enabled = false;
                    }
                }
            }
            catch { }

            Renderer[] renderers = saved.renderers;
            Material[][] tinted = new Material[renderers != null ? renderers.Length : 0][];

            float duration = corpse.followsHost ? FollowDuration : Duration;
            float time = 0f;
            while (time < duration) {
                if (gen != generation || !root || !StillDead(corpse) || !StillOut(root)) {
                    Restore(saved);
                    ReleaseTinted(tinted, saved);
                    RestoreTrackers(corpse);
                    corpses.Remove(corpse);
                    yield break;
                }

                time += Time.deltaTime;
                float u = time / duration;
                if (u > 1f)
                    u = 1f;

                float on = Sample(time, u);
                if (u > 0.92f)
                    on = 0f;

                bool visible = on > 0.45f;
                Color red = new Color(1f, 0.05f, 0.02f, on);
                float glow = 3.2f * on;

                if (renderers != null) {
                    for (int i = 0; i < renderers.Length; i++) {
                        Renderer renderer = renderers[i];
                        if (!renderer)
                            continue;

                        try {
                            renderer.enabled = visible;
                            if (!visible)
                                continue;

                            Material[] mats = Tinted(renderer, tinted, i);
                            if (mats == null)
                                continue;

                            for (int m = 0; m < mats.Length; m++) {
                                Material mat = mats[m];
                                if (!mat)
                                    continue;

                                mat.color = red;
                                mat.SetColor("_Color", red);
                                mat.SetColor("_EmissionColor", new Color(glow, glow * 0.04f, glow * 0.02f));
                                mat.EnableKeyword("_EMISSION");
                            }
                        }
                        catch { }
                    }
                }

                yield return null;
            }

            if (!root) {
                ReleaseTinted(tinted, saved);
                corpses.Remove(corpse);
                yield break;
            }

            RestoreTrackers(corpse);

            Restore(saved);
            ReleaseTinted(tinted, saved);

            if (!StillOut(root)) {
                corpses.Remove(corpse);
                Trim();
                yield break;
            }

            try {
                Poolee poolee = root.GetComponent<Poolee>();
                if (!poolee)
                    poolee = root.GetComponentInChildren<Poolee>(true);

                if (poolee)
                    PooleeSyncable.DespawnPoolee(poolee);
                else
                    root.gameObject.SetActive(false);
            }
            catch {
                try {
                    if (root)
                        root.gameObject.SetActive(false);
                }
                catch { }
            }

            corpses.Remove(corpse);
            Trim();
        }

        static void CacheCurve() {
            if (flickerCurve != null)
                return;

            try {
                BehaviourOmniwheel wheel = Object.FindObjectOfType<BehaviourOmniwheel>();
                if (wheel && wheel.deathFlicker != null)
                    flickerCurve = wheel.deathFlicker;
            }
            catch { }
        }

        static float Sample(float time, float u) {
            if (flickerCurve == null)
                return Mathf.Sin(u * (8f + u * 22f) * 3.14159265f) > 0f ? 1f : 0.1f;

            float end = 1f;
            try {
                Keyframe[] keys = flickerCurve.keys;
                if (keys != null && keys.Length > 0)
                    end = keys[keys.Length - 1].time;
            }
            catch { }

            float sample;
            try { sample = end > 1.5f ? flickerCurve.Evaluate(time) : flickerCurve.Evaluate(u); }
            catch { return 1f - u; }

            if (sample < 0f)
                sample = -sample;
            if (sample > 1f)
                sample = 1f;
            return sample * (1f - u);
        }
    }
}
