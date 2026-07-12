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

        class VoicePlayer {
            public AudioSource source;
            public AudioClip clip;
            public int clipSamples;
            public long written;
            public long played;
            public int lastTimeSamples;
        }

        static readonly Dictionary<long, VoicePlayer> players = new Dictionary<long, VoicePlayer>();
        static readonly HashSet<long> mutedPlayers = new HashSet<long>();

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

                    if (result == EVoiceResult.k_EVoiceResultOK && written > 0)
                        VoiceDataMessageHandler.SendVoice(compressedBuffer, (int)written);
                }
            }

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

            // 16 bit signed PCM to floats
            int sampleCount = (int)bytesWritten / 2;
            if (sampleBuffer.Length < sampleCount)
                sampleBuffer = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++) {
                short sample = (short)(decompressBuffer[i * 2] | (decompressBuffer[i * 2 + 1] << 8));
                sampleBuffer[i] = sample / 32768f;
            }

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

            if (!PlayerRepresentation.representations.TryGetValue(speakerId, out PlayerRepresentation rep) || rep.repRoot == null)
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

        // Mode, range and volume apply live to every speaker
        public static void ApplySettings() {
            foreach (VoicePlayer player in players.Values)
                ApplySettingsTo(player);
        }

        static void ApplySettingsTo(VoicePlayer player) {
            if (!player.source)
                return;

            player.source.volume = outputVolume / 100f;

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
