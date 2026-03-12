using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Entanglement.Network;

using UnityEngine;

namespace Entanglement.Managers
{
    public static class HostPerformanceProfiler
    {
        public static bool isEnabled = false;

        private struct SectionStat
        {
            public int calls;
            public double totalMs;
            public double maxMs;
            public int spikeCount;
        }

        private const float REPORT_INTERVAL = 1f;
        private const double SPIKE_THRESHOLD_MS = 3.0;

        private static readonly Dictionary<string, SectionStat> sectionStats = new Dictionary<string, SectionStat>();
        private static float reportTimer = 0f;
        private static int frameCount = 0;

        private static uint sentBytes;
        private static uint receivedBytes;
        private static int receivedPackets;
        private static int voicePackets;
        private static uint voicePayloadBytes;

        private static bool IsActiveHost()
        {
            return isEnabled && SteamIntegration.hasLobby && SteamIntegration.isHost && Node.activeNode is Server;
        }

        public static void SetEnabled(bool enabled)
        {
            isEnabled = enabled;
            if (!isEnabled)
                Reset();
        }

        public static long BeginSample()
        {
            if (!IsActiveHost())
                return 0;

            return Stopwatch.GetTimestamp();
        }

        public static void EndSample(string sectionName, long startTimestamp)
        {
            if (startTimestamp == 0 || !IsActiveHost())
                return;

            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;

            if (!sectionStats.TryGetValue(sectionName, out SectionStat stat))
                stat = new SectionStat();

            stat.calls++;
            stat.totalMs += elapsedMs;
            stat.maxMs = Math.Max(stat.maxMs, elapsedMs);
            if (elapsedMs >= SPIKE_THRESHOLD_MS)
                stat.spikeCount++;

            sectionStats[sectionName] = stat;
        }

        public static void AddSentBytes(uint count)
        {
            if (!IsActiveHost())
                return;

            sentBytes += count;
        }

        public static void AddReceivedBytes(uint count)
        {
            if (!IsActiveHost())
                return;

            receivedBytes += count;
        }

        public static void AddReceivedPacket(int packetCount)
        {
            if (!IsActiveHost())
                return;

            receivedPackets += packetCount;
        }

        public static void AddVoiceSend(uint payloadSize)
        {
            if (!IsActiveHost())
                return;

            voicePackets++;
            voicePayloadBytes += payloadSize;
        }

        public static void Tick()
        {
            if (!isEnabled)
                return;

            if (!IsActiveHost())
            {
                Reset();
                return;
            }

            frameCount++;
            reportTimer += Time.unscaledDeltaTime;
            if (reportTimer < REPORT_INTERVAL)
                return;

            double windowSeconds = reportTimer;
            reportTimer = 0f;

            string sectionSummary = "none";
            if (sectionStats.Count > 0)
            {
                sectionSummary = string.Join(" | ", sectionStats
                    .OrderByDescending(x => x.Value.maxMs)
                    .Take(6)
                    .Select(x =>
                    {
                        double avg = x.Value.calls > 0 ? x.Value.totalMs / x.Value.calls : 0;
                        return $"{x.Key}:avg={avg:0.00}ms max={x.Value.maxMs:0.00}ms spikes={x.Value.spikeCount}";
                    }));
            }

            EntangleLogger.Log(
                $"[HostPerf] {windowSeconds:0.00}s frames={frameCount} up={sentBytes}B down={receivedBytes}B rxPackets={receivedPackets} voiceTx={voicePackets}/{voicePayloadBytes}B sections[{sectionSummary}]",
                ConsoleColor.DarkCyan);

            frameCount = 0;
            sentBytes = 0;
            receivedBytes = 0;
            receivedPackets = 0;
            voicePackets = 0;
            voicePayloadBytes = 0;
            sectionStats.Clear();
        }

        private static void Reset()
        {
            frameCount = 0;
            reportTimer = 0f;
            sentBytes = 0;
            receivedBytes = 0;
            receivedPackets = 0;
            voicePackets = 0;
            voicePayloadBytes = 0;
            sectionStats.Clear();
        }
    }
}
