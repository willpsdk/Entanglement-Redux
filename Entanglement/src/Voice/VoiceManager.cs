using System;
using System.Collections.Generic;

using UnityEngine;

using UnhollowerBaseLib;

using Steamworks;

using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.Voice
{
    public enum VoiceMode : byte {
        Proximity = 0,
        Global = 1,
    }

    public static class VoiceManager
    {
        public static bool micEnabled = true;
        public static VoiceMode mode = VoiceMode.Proximity;
        public static int proximityRange = 30; // Meters until a voice fades out completely
        public static int outputVolume = 100;  // Percent
        const float baseVoiceGain = 2f;

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
            public AudioLowPassFilter lowPass;
            public Transform head;
            public AudioClip clip;
            public int clipSamples;
            public long written;
            public long played;
            public int lastTimeSamples;
            public float lastReceiveTime;

            // Where the room and the speaker's facing want the sound, and where it is now.
            public float targetVolume = 1f;
            public float targetCutoff = 22000f;
            public float volume = 1f;
            public float cutoff = 22000f;
            public float nextOcclusionCheck;
            public float occlusion; // 0 = open line, 1+ = walls in the way
        }

        static readonly Dictionary<long, VoicePlayer> players = new Dictionary<long, VoicePlayer>();
        static readonly HashSet<long> mutedPlayers = new HashSet<long>();

#if DEBUG
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
            bool debugActive = false;
#if DEBUG
            debugActive = debugVoiceOnRep && PlayerRepresentation.debugRepresentation != null;
#endif

            if (!SteamIntegration.hasLobby && !debugActive) {
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
                        // Only broadcast when actually connected - solo debug capture has nobody to send to
                        if (SteamIntegration.hasLobby)
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

            try { UpdateAcoustics(); }
            catch (Exception e) { EntangleLogger.Warn("[Voice] Acoustics: " + e.Message); }

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
            if (count <= 0 || count > receiveScratch.Length)
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

            int sampleCount = (int)bytesWritten / 2;
            if (sampleBuffer.Length < sampleCount)
                sampleBuffer = new float[sampleCount];

            float gain = outputVolume / 100f * baseVoiceGain;
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
            // A voice comes from the mouth, a little in front of and below the middle of the head.
            go.transform.localPosition = new Vector3(0f, -0.07f, 0.09f);

            AudioSource source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.reverbZoneMix = 1f;

            AudioLowPassFilter lowPass = go.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 22000f;
            lowPass.lowpassResonanceQ = 1f;

            int clipSamples = (int)sampleRate; // One second of ring buffer
            AudioClip clip = AudioClip.Create($"VoiceClip {speakerId}", clipSamples, 1, (int)sampleRate, false);
            source.clip = clip;

            player = new VoicePlayer { source = source, lowPass = lowPass, head = head, clip = clip, clipSamples = clipSamples };
            players[speakerId] = player;

            ApplySettingsTo(player);
            return player;
        }

        static PlayerRepresentation ResolveRep(long speakerId) {
#if DEBUG
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

            player.source.volume = 1f;

            if (mode == VoiceMode.Global) {
                player.source.spatialBlend = 0f;
                player.source.spatialize = false;
                if (player.lowPass)
                    player.lowPass.enabled = false;
            }
            else {
                player.source.spatialBlend = 1f;
                player.source.spatialize = false;
                player.source.minDistance = ReferenceDistance;
                player.source.maxDistance = Mathf.Max(5f, proximityRange);
                player.source.rolloffMode = AudioRolloffMode.Custom;
                player.source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, BuildRolloff(player.source.maxDistance));
                if (player.lowPass)
                    player.lowPass.enabled = true;
            }
        }

        const float ReferenceDistance = 1.2f;   // Normal speaking loudness is set at about arm's length
        const float OcclusionInterval = 0.1f;
        const float AcousticSmoothing = 10f;     // How fast volume and tone follow changes

        static AudioListener listener;

        static readonly Il2CppStructArray<RaycastHit> occlusionHits = new Il2CppStructArray<RaycastHit>(16);

        static AnimationCurve BuildRolloff(float maxDistance) {
            AnimationCurve curve = new AnimationCurve();
            const int steps = 24;
            for (int i = 0; i <= steps; i++) {
                float t = i / (float)steps;
                float distance = Mathf.Max(0.01f, t * maxDistance);
                float gain = Mathf.Min(1f, ReferenceDistance / distance);
                float tail = Mathf.InverseLerp(1f, 0.75f, t);
                curve.AddKey(t, gain * Mathf.Clamp01(tail));
            }
            return curve;
        }

        static Transform ListenerTransform() {
            try {
                if (!listener || !listener.isActiveAndEnabled)
                    listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            }
            catch { listener = null; }

            if (listener)
                return listener.transform;
            return Camera.main ? Camera.main.transform : null;
        }

        static void UpdateAcoustics() {
            if (mode != VoiceMode.Proximity || players.Count == 0)
                return;

            Transform ear = ListenerTransform();
            if (!ear)
                return;

            float smooth = 1f - Mathf.Exp(-AcousticSmoothing * Time.unscaledDeltaTime);

            foreach (VoicePlayer player in players.Values) {
                if (!player.source || !player.lowPass)
                    continue;

                Vector3 mouth = player.source.transform.position;
                Vector3 toEar = ear.position - mouth;
                float distance = toEar.magnitude;

                if (Time.unscaledTime >= player.nextOcclusionCheck) {
                    player.nextOcclusionCheck = Time.unscaledTime + OcclusionInterval;
                    player.occlusion = Occlusion(mouth, ear.position, distance);
                }

                float volume = 1f;
                float cutoff = 22000f;

                // Walls: roughly 9dB and most of the top end per wall, up to three.
                if (player.occlusion > 0f) {
                    volume *= Mathf.Pow(0.35f, Mathf.Min(player.occlusion, 3f));
                    cutoff = Mathf.Min(cutoff, Mathf.Lerp(2000f, 700f, Mathf.Clamp01(player.occlusion - 1f)));
                }

                // Facing: full in front, about 4dB quieter and duller from directly behind.
                if (player.head && distance > 0.01f) {
                    float facing = Vector3.Dot(player.head.forward, toEar / distance);
                    float behind = Mathf.InverseLerp(0.3f, -1f, facing);
                    volume *= Mathf.Lerp(1f, 0.6f, behind);
                    cutoff = Mathf.Min(cutoff, Mathf.Lerp(22000f, 5000f, behind));
                }

                // Distance: far voices lose their top end.
                float far = Mathf.Clamp01(distance / Mathf.Max(5f, proximityRange));
                cutoff = Mathf.Min(cutoff, Mathf.Lerp(22000f, 7000f, far));

                player.targetVolume = volume;
                player.targetCutoff = cutoff;
                player.volume = Mathf.Lerp(player.volume, player.targetVolume, smooth);
                player.cutoff = Mathf.Lerp(player.cutoff, player.targetCutoff, smooth);

                player.source.volume = player.volume;
                player.lowPass.cutoffFrequency = player.cutoff;
            }
        }

        static float Occlusion(Vector3 mouth, Vector3 ear, float distance) {
            if (distance < 0.3f)
                return 0f;

            Vector3 dir = (ear - mouth) / distance;
            int count;
            try { count = Physics.RaycastNonAlloc(mouth, dir, occlusionHits, distance, ~0, QueryTriggerInteraction.Ignore); }
            catch { return 0f; }

            float walls = 0f;
            for (int i = 0; i < count; i++) {
                Collider col = occlusionHits[i].collider;
                if (!col)
                    continue;

                try {
                    Transform root = col.transform.root;
                    if (root && (root.name.StartsWith("PlayerRep.") || root.name.StartsWith("Ragdoll.")))
                        continue;
                    if (Entanglement.Data.PlayerScripts.playerRig && col.transform.IsChildOf(Entanglement.Data.PlayerScripts.playerRig.transform))
                        continue;

                    Rigidbody rb = col.attachedRigidbody;
                    if (rb == null)
                        walls += 1f;
                    else if (rb.GetComponent<Joint>())
                        walls += 0.5f;
                }
                catch { }
            }
            return walls;
        }

        public static void Reset() {
            foreach (VoicePlayer player in players.Values) {
                if (player.source)
                    GameObject.Destroy(player.source.gameObject);
            }

            players.Clear();

            // The listener belongs to the scene; look it up again in the next one.
            listener = null;
        }
    }
}
