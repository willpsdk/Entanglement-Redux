using System;
using System.Collections.Generic;

using UnityEngine;

using Steamworks;

using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.Voice
{
    public enum VoiceMode : byte {
        Proximity = 0,
        Global = 1,
    }

    // Steam voice: the mic is captured and compressed by Steam, shipped over the existing
    // P2P channels, and decompressed into a looping ring-buffer AudioClip per speaker
    public static class VoiceManager
    {
        public static bool micEnabled = true;
        public static VoiceMode mode = VoiceMode.Proximity;
        public static int proximityRange = 12; // Meters until a voice fades out completely
        public static int outputVolume = 100;  // Percent

        static bool recording;
        static uint sampleRate;

        // Reused buffers, this path runs every frame while in a lobby
        static readonly byte[] compressedBuffer = new byte[8192];
        static readonly byte[] receiveScratch = new byte[8192];
        static readonly byte[] decompressBuffer = new byte[65536];
        static float[] sampleBuffer = new float[32768];
        static readonly Dictionary<int, float[]> chunkPool = new Dictionary<int, float[]>();

        // Public so nametags can light up while someone talks
        public static float localVoiceTime = -10f;
        const float speakingWindow = 0.3f;

        public static bool IsLocalSpeaking => micEnabled && Time.time - localVoiceTime < speakingWindow;

        public static bool IsSpeaking(long userId) {
            return players.TryGetValue(userId, out VoicePlayer player) && Time.time - player.lastReceiveTime < speakingWindow;
        }

        class VoicePlayer {
            public AudioSource source;
            public AudioClip clip;
            public int clipSamples;
            public long written;
            public long played;
            public int lastTimeSamples;
            public float lastReceiveTime;
        }

        static readonly Dictionary<long, VoicePlayer> players = new Dictionary<long, VoicePlayer>();
        static readonly HashSet<long> mutedPlayers = new HashSet<long>();

#if DEBUG
        // Debug loopback: feed your own captured voice back out of the spawned dummy rep after a
        // delay, so solo you can confirm voice works and that proximity fades it with distance.
        public static bool debugVoiceOnRep = false;
        const long debugRepVoiceId = -1337;
        const float debugVoiceDelaySeconds = 10f;

        struct DelayedVoicePacket { public byte[] data; public int count; public float playAt; }
        static readonly Queue<DelayedVoicePacket> debugVoiceQueue = new Queue<DelayedVoicePacket>();
#endif

        public static bool IsMuted(long userId) => mutedPlayers.Contains(userId);

        public static void SetMuted(long userId, bool muted) {
            if (muted) {
                mutedPlayers.Add(userId);

                // Cut whatever is already buffered and skip past it for a clean unmute later
                if (players.TryGetValue(userId, out VoicePlayer player) && player.source) {
                    player.source.Stop();
                    player.played = player.written;
                }
            }
            else
                mutedPlayers.Remove(userId);
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby) {
                if (recording) {
                    SteamUser.StopVoiceRecording();
                    recording = false;
                }

                if (players.Count > 0)
                    Reset();

                return;
            }

            if (sampleRate == 0)
                sampleRate = SteamUser.GetVoiceOptimalSampleRate();

            if (micEnabled && !recording) {
                SteamUser.StartVoiceRecording();
                recording = true;
            }
            else if (!micEnabled && recording) {
                SteamUser.StopVoiceRecording();
                recording = false;
            }

            if (recording) {
                EVoiceResult result = SteamUser.GetAvailableVoice(out uint available);

                if (result == EVoiceResult.k_EVoiceResultOK && available > 0) {
                    result = SteamUser.GetVoice(true, compressedBuffer, (uint)compressedBuffer.Length, out uint written);

                    if (result == EVoiceResult.k_EVoiceResultOK && written > 0) {
                        VoiceDataMessageHandler.SendVoice(compressedBuffer, (int)written);
                        localVoiceTime = Time.time;
#if DEBUG
                        QueueDebugVoice(compressedBuffer, (int)written);
#endif
                    }
                }
            }

#if DEBUG
            ProcessDebugVoice();
#endif

            // Track loop position per speaker and stop starved sources before they replay old audio
            foreach (VoicePlayer player in players.Values) {
                if (!player.source || !player.source.isPlaying)
                    continue;

                int timeSamples = player.source.timeSamples;
                if (timeSamples < player.lastTimeSamples)
                    player.played += player.clipSamples - player.lastTimeSamples + timeSamples;
                else
                    player.played += timeSamples - player.lastTimeSamples;
                player.lastTimeSamples = timeSamples;

                if (player.played >= player.written)
                    player.source.Stop();
            }
        }

        public static void ReceiveVoice(long speakerId, byte[] data, int offset, int count) {
            if (!SteamIntegration.hasLobby || count <= 0 || count > receiveScratch.Length)
                return;

            if (mutedPlayers.Contains(speakerId))
                return;

            if (sampleRate == 0)
                sampleRate = SteamUser.GetVoiceOptimalSampleRate();

            // DecompressVoice has no offset parameter, the payload gets copied out of the packet
            Buffer.BlockCopy(data, offset, receiveScratch, 0, count);

            EVoiceResult result = SteamUser.DecompressVoice(receiveScratch, (uint)count, decompressBuffer, (uint)decompressBuffer.Length, out uint bytesWritten, sampleRate);
            if (result != EVoiceResult.k_EVoiceResultOK || bytesWritten == 0)
                return;

            VoicePlayer player = GetPlayer(speakerId);
            if (player == null)
                return;

            // 16 bit signed PCM to floats. Volume is applied here as gain rather than on the
            // AudioSource, because AudioSource.volume is capped at 1.0 - so anything past 100%
            // did nothing. Clamp after so a boost hard-clips instead of wrapping into noise.
            int sampleCount = (int)bytesWritten / 2;
            if (sampleBuffer.Length < sampleCount)
                sampleBuffer = new float[sampleCount];

            float gain = outputVolume / 100f;
            for (int i = 0; i < sampleCount; i++) {
                short sample = (short)(decompressBuffer[i * 2] | (decompressBuffer[i * 2 + 1] << 8));
                sampleBuffer[i] = Mathf.Clamp(sample / 32768f * gain, -1f, 1f);
            }

            player.lastReceiveTime = Time.time;
            WriteSamples(player, sampleCount);
        }

        static void WriteSamples(VoicePlayer player, int count) {
            int writePos = (int)(player.written % player.clipSamples);

            // SetData doesn't wrap, writes crossing the ring end are split in two
            int first = Math.Min(count, player.clipSamples - writePos);
            float[] chunk = GetChunk(first);
            Array.Copy(sampleBuffer, 0, chunk, 0, first);
            player.clip.SetData(chunk, writePos);

            int remain = count - first;
            if (remain > 0) {
                float[] tail = GetChunk(remain);
                Array.Copy(sampleBuffer, first, tail, 0, remain);
                player.clip.SetData(tail, 0);
            }

            player.written += count;

            // Start once a small cushion builds so playback doesn't immediately starve
            if (!player.source.isPlaying && player.written - player.played >= sampleRate / 10) {
                int startPos = (int)(player.played % player.clipSamples);
                player.source.timeSamples = startPos;
                player.lastTimeSamples = startPos;
                player.source.Play();
            }
        }

        static float[] GetChunk(int size) {
            if (!chunkPool.TryGetValue(size, out float[] chunk)) {
                chunk = new float[size];
                chunkPool[size] = chunk;
            }

            return chunk;
        }

        static VoicePlayer GetPlayer(long speakerId) {
            players.TryGetValue(speakerId, out VoicePlayer player);

            // Reps are recreated on level changes and take the audio source with them
            if (player != null && player.source)
                return player;

            PlayerRepresentation rep = ResolveRep(speakerId);
            if (rep == null || rep.repRoot == null)
                return null;

            GameObject go = new GameObject($"Voice {speakerId}");
            Transform head = rep.repTransforms[0] ? rep.repTransforms[0] : rep.repRoot;
            go.transform.SetParent(head, false);

            AudioSource source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0f;

            int clipSamples = (int)sampleRate; // One second of ring buffer
            AudioClip clip = AudioClip.Create($"VoiceClip {speakerId}", clipSamples, 1, (int)sampleRate, false);
            source.clip = clip;

            player = new VoicePlayer { source = source, clip = clip, clipSamples = clipSamples };
            players[speakerId] = player;

            ApplySettingsTo(player);
            return player;
        }

        static PlayerRepresentation ResolveRep(long speakerId) {
#if DEBUG
            // The debug loopback id isn't a real player, it points at the spawned dummy so the
            // voice comes out of its head
            if (speakerId == debugRepVoiceId)
                return PlayerRepresentation.debugRepresentation;
#endif
            PlayerRepresentation.representations.TryGetValue(speakerId, out PlayerRepresentation rep);
            return rep;
        }

#if DEBUG
        static void QueueDebugVoice(byte[] compressed, int written) {
            if (!debugVoiceOnRep || PlayerRepresentation.debugRepresentation == null || written <= 0)
                return;

            byte[] copy = new byte[written];
            Buffer.BlockCopy(compressed, 0, copy, 0, written);
            debugVoiceQueue.Enqueue(new DelayedVoicePacket { data = copy, count = written, playAt = Time.time + debugVoiceDelaySeconds });
        }

        static void ProcessDebugVoice() {
            if (!debugVoiceOnRep || PlayerRepresentation.debugRepresentation == null) {
                if (debugVoiceQueue.Count > 0)
                    debugVoiceQueue.Clear();
                return;
            }

            // Anything that's waited out its 10s gets played back through the dummy's own voice
            // source, the exact same path a remote speaker's audio takes
            while (debugVoiceQueue.Count > 0 && Time.time >= debugVoiceQueue.Peek().playAt) {
                DelayedVoicePacket packet = debugVoiceQueue.Dequeue();
                ReceiveVoice(debugRepVoiceId, packet.data, 0, packet.count);
            }
        }
#endif

        // Mode, range and volume apply live to every speaker
        public static void ApplySettings() {
            foreach (VoicePlayer player in players.Values)
                ApplySettingsTo(player);
        }

        static void ApplySettingsTo(VoicePlayer player) {
            if (!player.source)
                return;

            // Kept at full - loudness (including boost past 100%) is applied as sample gain in
            // ReceiveVoice, since the AudioSource can't go above 1.0
            player.source.volume = 1f;

            if (mode == VoiceMode.Global) {
                player.source.spatialBlend = 0f;
            }
            else {
                player.source.spatialBlend = 1f;
                player.source.minDistance = 1f;
                player.source.maxDistance = Mathf.Max(2f, proximityRange);
            }
        }

        public static void Reset() {
            foreach (VoicePlayer player in players.Values) {
                if (player.source)
                    GameObject.Destroy(player.source.gameObject);
            }

            players.Clear();
        }
    }
}
