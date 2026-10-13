using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using UnityEngine;

using Entanglement.Objects;

namespace Entanglement.Network
{
    public static class NetStats
    {
        public static float downKBps;
        public static float upKBps;

        static float nextSample;

        public static void Tick() {
            Node node = Node.activeNode;
            if (node == null)
                return;

            float now = Time.unscaledTime;

            WatchHitches(now);
            Report(now);

            if (now < nextSample)
                return;

            float elapsed = nextSample > 0f ? 1f + (now - nextSample) : 1f;
            nextSample = now + 1f;

            downKBps = node.recievedByteCount / 1024f / elapsed;
            upKBps = node.sentByteCount / 1024f / elapsed;
            node.recievedByteCount = 0;
            node.sentByteCount = 0;

            // The last full second, for the hitch log.
            Array.Copy(secondBuilt, lastSecondBuilt, 256);
            Array.Copy(secondRead, lastSecondRead, 256);
            Array.Clear(secondBuilt, 0, 256);
            Array.Clear(secondRead, 0, 256);
        }

        // ---- Counting ------------------------------------------------------------------------------

        static readonly int[] secondBuilt = new int[256];
        static readonly int[] secondRead = new int[256];
        static readonly int[] lastSecondBuilt = new int[256];
        static readonly int[] lastSecondRead = new int[256];

        static readonly int[] reportBuilt = new int[256];
        static readonly long[] reportBytes = new long[256];
        static readonly int[] reportRead = new int[256];

        public static void CountBuilt(byte type, int bytes) {
            secondBuilt[type]++;
            reportBuilt[type]++;
            reportBytes[type] += bytes;
        }

        public static void CountRead(byte type) {
            secondRead[type]++;
            reportRead[type]++;
        }

        static string[] typeNames;

        static string TypeName(int type) {
            if (typeNames == null) {
                typeNames = new string[256];
                try {
                    foreach (FieldInfo field in typeof(BuiltInMessageType).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                        if (field.FieldType != typeof(byte))
                            continue;
                        byte value = (byte)field.GetValue(null);
                        if (typeNames[value] == null)
                            typeNames[value] = field.Name;
                    }
                }
                catch { }
            }
            return typeNames[type] ?? ("#" + type);
        }

        // The busiest types, "Name x12" each, busiest first.
        static string Top(int[] counts, long[] bytes, int max) {
            List<int> types = new List<int>();
            for (int i = 0; i < 256; i++) {
                if (counts[i] > 0)
                    types.Add(i);
            }
            if (types.Count == 0)
                return "nothing";

            types.Sort((a, b) => counts[b].CompareTo(counts[a]));
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < types.Count && i < max; i++) {
                if (i > 0)
                    text.Append(", ");
                int type = types[i];
                text.Append(TypeName(type)).Append(" x").Append(counts[type]);
                if (bytes != null)
                    text.Append(" (").Append((bytes[type] / 1024f).ToString("F1")).Append("KB)");
            }
            if (types.Count > max)
                text.Append(", +").Append(types.Count - max).Append(" more");
            return text.ToString();
        }

        // ---- Where a frame went ---------------------------------------------------------------------

        // Our subsystems' time (Mod.SafeTick), the synced bodies' physics steps, and the game's heap,
        // for the frame being timed and the one before it, which is the one a hitch shows up after.
        static Dictionary<string, long> frameCost = new Dictionary<string, long>();
        static Dictionary<string, long> lastFrameCost = new Dictionary<string, long>();
        public static long bodyTicks;
        public static int bodySteps;
        static long lastBodyTicks;
        static int lastBodySteps;
        static int lastQuietSkips;
        static long heapUsed;
        static long lastHeapFreed;

        public static void AddCost(string name, long ticks) {
            long total;
            frameCost.TryGetValue(name, out total);
            frameCost[name] = total + ticks;
        }

        // Where the wall clock went, in the order Unity runs a frame: the physics steps (the game's
        // FixedUpdates and the simulation), then Update and LateUpdate (every script), then rendering and
        // waiting for the headset. Our OnFixedUpdate, OnUpdate and OnLateUpdate mark the borders.
        static long fixedStart, updateStart, lateEnd;
        static int fixedSteps;
        static long lastPhysics, lastScripts, lastRender;
        static int lastFixedSteps;

        /// <summary>Physics steps Unity ran before this frame's Update.</summary>
        public static int FixedStepsThisFrame => lastFixedSteps;

        /// <summary>Each physics step, before any of ours.</summary>
        public static void MarkFixed() {
            if (fixedSteps++ == 0)
                fixedStart = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        /// <summary>Last thing each frame.</summary>
        public static void MarkLateEnd() => lateEnd = System.Diagnostics.Stopwatch.GetTimestamp();

        static void SplitPhases(long now) {
            long physicsFrom = fixedSteps > 0 ? fixedStart : now;
            lastScripts = updateStart > 0 && lateEnd > updateStart ? lateEnd - updateStart : 0;
            lastRender = lateEnd > 0 && physicsFrom > lateEnd ? physicsFrom - lateEnd : 0;
            lastPhysics = fixedSteps > 0 ? now - fixedStart : 0;
            lastFixedSteps = fixedSteps;
            fixedSteps = 0;
            updateStart = now;
        }

        /// <summary>First thing each frame.</summary>
        public static void BeginFrame() {
            SplitPhases(System.Diagnostics.Stopwatch.GetTimestamp());

            Dictionary<string, long> swap = lastFrameCost;
            lastFrameCost = frameCost;
            frameCost = swap;
            frameCost.Clear();

            lastBodyTicks = bodyTicks;
            lastBodySteps = bodySteps;
            lastQuietSkips = TransformSyncable.quietSkips;
            TransformSyncable.quietSkips = 0;
            bodyTicks = 0;
            bodySteps = 0;

            try {
                long used = UnhollowerBaseLib.IL2CPP.il2cpp_gc_get_used_size();
                lastHeapFreed = heapUsed > used ? heapUsed - used : 0;
                heapUsed = used;
            }
            catch { lastHeapFreed = 0; }
        }

        static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        static string FrameCosts() {
            StringBuilder text = new StringBuilder();
            List<KeyValuePair<string, long>> costs = new List<KeyValuePair<string, long>>(lastFrameCost);
            costs.Sort((a, b) => b.Value.CompareTo(a.Value));
            text.Append("Slowest of ours:");
            for (int i = 0; i < costs.Count && i < 3; i++)
                text.Append(i == 0 ? " " : ", ").Append(costs[i].Key).Append(' ').Append(Ms(costs[i].Value).ToString("F1")).Append("ms");
            text.Append(". Synced bodies ").Append(Ms(lastBodyTicks).ToString("F1")).Append("ms over ").Append(lastBodySteps).Append(" steps (").Append(lastQuietSkips).Append(" of them skipped at rest)");
            text.Append(". Phases: physics ").Append(Ms(lastPhysics).ToString("F0")).Append("ms (").Append(lastFixedSteps)
                .Append(" steps of ").Append((Time.fixedDeltaTime * 1000f).ToString("F1")).Append("ms, cap ")
                .Append((Time.maximumDeltaTime * 1000f).ToString("F0")).Append("ms), scripts ").Append(Ms(lastScripts).ToString("F0"))
                .Append("ms, render/wait ").Append(Ms(lastRender).ToString("F0")).Append("ms");
            if (lastHeapFreed > 1024 * 1024)
                text.Append(". The game's GC ran (freed ").Append(lastHeapFreed / (1024 * 1024)).Append("MB)");
            return text.ToString();
        }

        // ---- Hitches -------------------------------------------------------------------------------

        const float HitchSeconds = 0.1f;
        static float nextHitchLog;
        static int hitchLogs;

        static void WatchHitches(float now) {
            if (!SteamIntegration.hasLobby || SceneLoader.loading)
                return;

            float frame = Time.unscaledDeltaTime;
            if (frame < HitchSeconds || now < nextHitchLog)
                return;
            // A long session would otherwise flood the log: after the first 200, ordinary hitches are
            // logged every 30s, and the bad ones (half a second or more) still every 2s.
            hitchLogs++;
            nextHitchLog = now + (hitchLogs > 200 && frame < 0.5f ? 30f : 2f);

            EntangleLogger.Log("[Perf] A frame took " + (frame * 1000f).ToString("F0") + "ms. " + FrameCosts()
                + ". Sent in the second before: " + Top(Sum(lastSecondBuilt, secondBuilt), null, 5)
                + ". Received: " + Top(Sum(lastSecondRead, secondRead), null, 5) + ". " + StateLine());
        }

        static readonly int[] summed = new int[256];

        static int[] Sum(int[] a, int[] b) {
            for (int i = 0; i < 256; i++)
                summed[i] = a[i] + b[i];
            return summed;
        }

        // ---- Test reports --------------------------------------------------------------------------

        static float nextTraffic;
        static float nextState;

        static void Report(float now) {
            if (!EntangleLogger.verbose) {
                // Start the window fresh when the test is switched on.
                nextTraffic = now + 10f;
                nextState = now + 30f;
                Array.Clear(reportBuilt, 0, 256);
                Array.Clear(reportBytes, 0, 256);
                Array.Clear(reportRead, 0, 256);
                return;
            }

            if (now >= nextTraffic) {
                nextTraffic = now + 10f;
                EntangleLogger.Debug("[Net] Last 10s sent: " + Top(reportBuilt, reportBytes, 8) + ". Received: " + Top(reportRead, null, 8));
                Array.Clear(reportBuilt, 0, 256);
                Array.Clear(reportBytes, 0, 256);
                Array.Clear(reportRead, 0, 256);
            }

            if (now >= nextState) {
                nextState = now + 30f;
                EntangleLogger.Debug("[State] " + StateLine());
            }
        }

        public static string StateLine() {
            try {
                int queued = 0;
                for (int i = 0; i < ObjectSync.queuedSyncs.Count; i++) {
                    if (ObjectSync.queuedSyncs[i] != null)
                        queued++;
                }
                int here, followed;
                PooleeSyncable.CountNpcs(out here, out followed);
                int awake = 0, driven = 0;
                foreach (Syncable syncable in ObjectSync.syncedObjects.Values) {
                    TransformSyncable body = syncable as TransformSyncable;
                    if (body == null || !body.rb || body.rb.isKinematic || body.rb.IsSleeping())
                        continue;
                    awake++;
                    if (!body.IsOwner())
                        driven++;
                }
                return "Synced bodies " + ObjectSync.syncedObjects.Count + " (awake " + awake + ", " + driven + " of them followed)" + ", spawned objects " + PooleeSyncable._PooleeLookup.Count
                    + ", NPCs run here " + here + " / followed " + followed + ", ids waiting " + queued
                    + ", scene events retrying " + SceneEventSync.RetriesPending + ", " + (1f / Mathf.Max(0.001f, Time.smoothDeltaTime)).ToString("F0") + " fps";
            }
            catch (Exception e) { return "state unavailable: " + e.Message; }
        }
    }

    /// <summary>
    /// The game lets a slow frame make up to maximumDeltaTime of physics steps (0.1s). At the 144Hz
    /// physics a VR headset sets, that is 14 steps, and a heavy level (a thousand synced bodies) makes
    /// each step 4-6ms: the catch-up steps make the next frame slower still, and the game spirals
    /// down to a few fps (Oct 9 logs: 14-15 steps in every 100ms frame, 60-85ms of it physics). While
    /// in a lobby the catch-up is capped lower, so a slow frame costs a little game speed instead.
    /// </summary>
    public static class PhysicsCatchUp
    {
        const float LobbyCap = 0.04f;
        static float original = -1f;
        static bool applied;

        public static void Tick() {
            try {
                bool want = SteamIntegration.hasLobby;
                if (want == applied)
                    return;
                if (want) {
                    if (original < 0f)
                        original = Time.maximumDeltaTime;
                    // Never above the game's own, and never below a few steps of its fixed step.
                    float cap = Mathf.Max(LobbyCap, Time.fixedDeltaTime * 3f);
                    if (cap < Time.maximumDeltaTime)
                        Time.maximumDeltaTime = cap;
                    EntangleLogger.Debug("[Perf] Physics catch-up capped at " + (Time.maximumDeltaTime * 1000f).ToString("F0")
                        + "ms a frame (was " + (original * 1000f).ToString("F0") + "ms)");
                }
                else if (original > 0f) {
                    Time.maximumDeltaTime = original;
                }
                applied = want;
            }
            catch { }
        }
    }
}
