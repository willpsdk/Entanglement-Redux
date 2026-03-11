using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using Steamworks;
using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Representation;

namespace Entanglement.Managers
{
    /// <summary>
    /// Manages voice chat with proximity detection and audio settings.
    /// Voice only transmits to players within proximity range.
    /// </summary>
    public static class VoiceChatManager
    {
        public enum VoiceChatMode
        {
            Disabled,
            Global,      // Everyone hears you
            Proximity    // Only nearby players hear you
        }

        // Voice chat settings
        public static VoiceChatMode voiceChatMode = VoiceChatMode.Disabled;
        public static float proximityRange = 50f;  // 50 meters default
        public static float microphoneVolume = 1f; // 0.0 - 1.0
        public static float outputVolume = 1f;     // 0.0 - 1.0
        
        // Selected devices
        public static int selectedMicrophoneIndex = -1;
        public static int selectedSpeakerIndex = -1;

        // Available devices
        private static List<string> availableMicrophones = new List<string>();
        private static List<string> availableSpeakers = new List<string>();

        // Voice data
        private static Dictionary<ulong, AudioClip> playerVoiceClips = new Dictionary<ulong, AudioClip>();
        private static Dictionary<ulong, AudioSource> playerAudioSources = new Dictionary<ulong, AudioSource>();
        private static Dictionary<ulong, bool> mutedPlayers = new Dictionary<ulong, bool>(); // Track muted players
        private static float voiceRecordingTimer = 0f;
        private const float VOICE_UPDATE_INTERVAL = 0.1f; // Update voice every 100ms
        private const uint VOICE_SAMPLE_RATE = 11025;

        // FIX: Track talking state for animations
        private static bool wasLocalPlayerTalking = false;
        private static float talkingThreshold = 0.01f; // Minimum volume to consider as talking

        public static void Initialize()
        {
            EntangleLogger.Log("Initializing VoiceChatManager");
            
            // Get available microphones
            availableMicrophones.Clear();
            availableMicrophones.AddRange(Microphone.devices);
            
            if (availableMicrophones.Count == 0)
            {
                EntangleLogger.Log("No microphones found! Voice chat will be disabled.");
            }
            else
            {
                EntangleLogger.Log($"Found {availableMicrophones.Count} microphones:");
                for (int i = 0; i < availableMicrophones.Count; i++)
                {
                    EntangleLogger.Log($"  [{i}] {availableMicrophones[i]}");
                }
                selectedMicrophoneIndex = 0; // Default to first device
            }

            // Get available speakers (audio outputs)
            availableSpeakers.Clear();
            availableSpeakers.Add("Default Speaker");
            // More speaker detection could be added here if needed
        }

        public static void SetVoiceChatMode(VoiceChatMode mode)
        {
            voiceChatMode = mode;
            
            if (mode == VoiceChatMode.Disabled)
            {
                SteamUser.StopVoiceRecording();
                EntangleLogger.Log("Voice chat disabled");
            }
            else
            {
                if (selectedMicrophoneIndex >= 0 && selectedMicrophoneIndex < availableMicrophones.Count)
                {
                    string deviceName = availableMicrophones[selectedMicrophoneIndex];
                    EntangleLogger.Log($"Starting voice chat in {mode} mode using: {deviceName}");
                    SteamUser.StartVoiceRecording();
                }
                else
                {
                    EntangleLogger.Error("No valid microphone selected!");
                }
            }
        }

        public static void SetProximityRange(float range)
        {
            proximityRange = Mathf.Clamp(range, 10f, 500f);
            EntangleLogger.Log($"Voice proximity range set to: {proximityRange}m");
        }

        public static void SetMicrophoneVolume(float volume)
        {
            microphoneVolume = Mathf.Clamp01(volume);
            EntangleLogger.Log($"Microphone volume set to: {microphoneVolume * 100f}%");
        }

        public static void SetOutputVolume(float volume)
        {
            outputVolume = Mathf.Clamp01(volume);
            EntangleLogger.Log($"Output volume set to: {outputVolume * 100f}%");
            
            // Update all active audio sources
            foreach (var audioSource in playerAudioSources.Values)
            {
                if (audioSource != null)
                    audioSource.volume = outputVolume;
            }
        }

        public static void SetMicrophone(int index)
        {
            if (index >= 0 && index < availableMicrophones.Count)
            {
                selectedMicrophoneIndex = index;
                EntangleLogger.Log($"Selected microphone: {availableMicrophones[index]}");
                
                // Restart recording if enabled
                if (voiceChatMode != VoiceChatMode.Disabled)
                {
                    SteamUser.StopVoiceRecording();
                    SteamUser.StartVoiceRecording();
                }
            }
        }

        public static List<string> GetAvailableMicrophones()
        {
            return new List<string>(availableMicrophones);
        }

        public static List<string> GetAvailableSpeakers()
        {
            return new List<string>(availableSpeakers);
        }

        // FIX: Per-player mute functionality
        public static void MutePlayer(ulong playerId)
        {
            mutedPlayers[playerId] = true;

            // Disable audio source if exists
            if (playerAudioSources.TryGetValue(playerId, out AudioSource audioSource))
            {
                if (audioSource != null)
                    audioSource.enabled = false;
            }

            EntangleLogger.Log($"Muted player: {playerId}");
        }

        public static void UnmutePlayer(ulong playerId)
        {
            mutedPlayers[playerId] = false;

            // Re-enable audio source if exists
            if (playerAudioSources.TryGetValue(playerId, out AudioSource audioSource))
            {
                if (audioSource != null)
                    audioSource.enabled = true;
            }

            EntangleLogger.Log($"Unmuted player: {playerId}");
        }

        public static bool IsPlayerMuted(ulong playerId)
        {
            return mutedPlayers.ContainsKey(playerId) && mutedPlayers[playerId];
        }

        public static List<KeyValuePair<ulong, string>> GetMutedPlayersList()
        {
            List<KeyValuePair<ulong, string>> mutedList = new List<KeyValuePair<ulong, string>>();

            foreach (var rep in PlayerRepresentation.representations)
            {
                if (rep.Value != null && IsPlayerMuted(rep.Key))
                {
                    mutedList.Add(new KeyValuePair<ulong, string>(rep.Key, rep.Value.playerName));
                }
            }

            return mutedList;
        }

        public static Dictionary<ulong, string> GetAllPlayersForVoiceMenu()
        {
            Dictionary<ulong, string> players = new Dictionary<ulong, string>();

            foreach (var rep in PlayerRepresentation.representations)
            {
                if (rep.Value != null)
                {
                    players[rep.Key] = rep.Value.playerName;
                }
            }

            return players;
        }

        public static void Tick()
        {
            if (voiceChatMode == VoiceChatMode.Disabled)
                return;

            voiceRecordingTimer += Time.deltaTime;
            if (voiceRecordingTimer < VOICE_UPDATE_INTERVAL)
                return;

            voiceRecordingTimer = 0f;

            // FIX: Check if local player is talking and broadcast it
            DetectAndBroadcastLocalPlayerTalking();

            // Get local player position
            Vector3 localPlayerPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

            // Check which players are in range
            foreach (var rep in PlayerRepresentation.representations.Values)
            {
                if (rep == null || rep.repRoot == null)
                    continue;

                ulong playerId = rep.playerId;
                float distanceToPlayer = Vector3.Distance(localPlayerPos, rep.repRoot.position);

                bool shouldHear = false;

                if (voiceChatMode == VoiceChatMode.Global)
                {
                    shouldHear = true;
                }
                else if (voiceChatMode == VoiceChatMode.Proximity)
                {
                    shouldHear = distanceToPlayer <= proximityRange;
                }

                // FIX: Don't hear if player is muted
                if (IsPlayerMuted(playerId))
                {
                    shouldHear = false;
                }

                // Create audio source if needed
                if (shouldHear && !playerAudioSources.ContainsKey(playerId))
                {
                    CreateAudioSource(playerId, rep.repRoot);
                }

                // Update audio source state
                if (playerAudioSources.TryGetValue(playerId, out AudioSource audioSource))
                {
                    if (audioSource != null)
                    {
                        audioSource.enabled = shouldHear;
                        audioSource.volume = outputVolume;
                    }
                }
            }
        }

        private static void CreateAudioSource(ulong playerId, Transform playerRoot)
        {
            try
            {
                GameObject audioObj = new GameObject($"VoiceChat_{playerId}");
                audioObj.transform.SetParent(playerRoot);
                audioObj.transform.localPosition = Vector3.up * 1.5f; // Head position

                AudioSource audioSource = audioObj.AddComponent<AudioSource>();
                audioSource.volume = outputVolume;
                audioSource.spatialBlend = 1f; // 3D audio
                audioSource.maxDistance = proximityRange * 2f;

                playerAudioSources[playerId] = audioSource;
                EntangleLogger.Verbose($"Created voice audio source for player: {playerId}");
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"Error creating audio source for {playerId}: {e.Message}");
            }
        }

        // FIX: Detect local player talking and broadcast it
        private static void DetectAndBroadcastLocalPlayerTalking()
        {
            try
            {
                if (Node.activeNode == null)
                    return;

                // Check if local player is currently recording voice
                uint writeBytes = 0;
                byte[] voiceData = new byte[4096];
                Steamworks.EVoiceResult result = Steamworks.SteamUser.GetVoice(false, voiceData, (uint)voiceData.Length, out writeBytes);

                // If we have voice data, player is talking
                bool isCurrentlyTalking = writeBytes > 0 && result == Steamworks.EVoiceResult.k_EVoiceResultOK;

                if (isCurrentlyTalking != wasLocalPlayerTalking)
                {
                    wasLocalPlayerTalking = isCurrentlyTalking;

                    var talkingData = new TalkingSyncData
                    {
                        userId = SteamIntegration.currentUser.m_SteamID,
                        isTalking = isCurrentlyTalking
                    };

                    if (Node.activeNode != null)
                    {
                        NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TalkingSync, talkingData);
                        Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, message.GetBytes());
                        EntangleLogger.Verbose($"Broadcast talking state: {isCurrentlyTalking}");
                    }
                }

                if (writeBytes > 0 && result == Steamworks.EVoiceResult.k_EVoiceResultOK)
                {
                    byte[] payload = new byte[writeBytes];
                    Buffer.BlockCopy(voiceData, 0, payload, 0, (int)writeBytes);

                    VoiceDataMessageData voiceDataMessage = new VoiceDataMessageData
                    {
                        userId = SteamIntegration.currentUser.m_SteamID,
                        compressedVoiceData = payload,
                    };

                    NetworkMessage voiceMessage = NetworkMessage.CreateMessage(BuiltInMessageType.VoiceData, voiceDataMessage);
                    Node.activeNode.BroadcastMessage(NetworkChannel.Unreliable, voiceMessage.GetBytes());
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Verbose($"Error detecting talking state: {ex.Message}");
            }
        }

        public static void ReceiveVoicePacket(ulong playerId, byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0)
                return;

            if (IsPlayerMuted(playerId))
                return;

            try
            {
                AudioSource audioSource = null;
                if (!playerAudioSources.TryGetValue(playerId, out audioSource) || audioSource == null)
                {
                    if (!PlayerRepresentation.representations.TryGetValue(playerId, out PlayerRepresentation rep) || rep == null || rep.repRoot == null)
                        return;

                    CreateAudioSource(playerId, rep.repRoot);
                    if (!playerAudioSources.TryGetValue(playerId, out audioSource) || audioSource == null)
                        return;
                }

                byte[] decompressed = new byte[48000];
                uint written;
                EVoiceResult decompressResult = SteamUser.DecompressVoice(compressedData, (uint)compressedData.Length, decompressed, (uint)decompressed.Length, out written, VOICE_SAMPLE_RATE);

                if (decompressResult != EVoiceResult.k_EVoiceResultOK || written < 2)
                    return;

                int sampleCount = (int)written / 2;
                float[] samples = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    short pcm = BitConverter.ToInt16(decompressed, i * 2);
                    samples[i] = pcm / 32768f;
                }

                AudioClip clip = AudioClip.Create($"VoicePacket_{playerId}_{Time.frameCount}", sampleCount, 1, (int)VOICE_SAMPLE_RATE, false);
                clip.SetData(samples, 0);
                audioSource.volume = outputVolume;
                audioSource.PlayOneShot(clip, outputVolume);
                UnityEngine.Object.Destroy(clip, 1f);
            }
            catch (Exception ex)
            {
                EntangleLogger.Verbose($"Error receiving voice packet from {playerId}: {ex.Message}");
            }
        }

        public static void RemovePlayerVoice(ulong playerId)
        {
            if (playerVoiceClips.TryGetValue(playerId, out AudioClip clip))
            {
                UnityEngine.Object.Destroy(clip);
                playerVoiceClips.Remove(playerId);
            }

            if (playerAudioSources.TryGetValue(playerId, out AudioSource audioSource))
            {
                if (audioSource != null)
                    UnityEngine.Object.Destroy(audioSource.gameObject);
                playerAudioSources.Remove(playerId);
            }

            // FIX: Clean up mute status for disconnected player
            mutedPlayers.Remove(playerId);
        }

        public static void CleanupVoiceData()
        {
            // FIX: Cleanup on level change to prevent memory leaks and audio issues
            try
            {
                foreach (var audioSource in playerAudioSources.Values)
                {
                    if (audioSource != null && audioSource.gameObject != null)
                        UnityEngine.Object.Destroy(audioSource.gameObject);
                }
                playerAudioSources.Clear();

                foreach (var clip in playerVoiceClips.Values)
                {
                    if (clip != null)
                        UnityEngine.Object.Destroy(clip);
                }
                playerVoiceClips.Clear();

                voiceRecordingTimer = 0f;
                wasLocalPlayerTalking = false;
                EntangleLogger.Verbose("[VoiceChat] Voice data cleaned up");
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error cleaning up voice data: {ex.Message}");
            }
        }

        public static void CleanupAll()
        {
            foreach (var audioSource in playerAudioSources.Values)
            {
                if (audioSource != null && audioSource.gameObject != null)
                    UnityEngine.Object.Destroy(audioSource.gameObject);
            }
            playerAudioSources.Clear();

            foreach (var clip in playerVoiceClips.Values)
            {
                if (clip != null)
                    UnityEngine.Object.Destroy(clip);
            }
            playerVoiceClips.Clear();

            // FIX: Clean up mute list
            mutedPlayers.Clear();

            try
            {
                SteamUser.StopVoiceRecording();
            }
            catch (Exception ex)
            {
                EntangleLogger.Verbose($"Error stopping voice recording: {ex.Message}");
            }
        }
    }
}
