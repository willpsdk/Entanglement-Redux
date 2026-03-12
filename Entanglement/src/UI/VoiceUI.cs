using System;
using System.Collections.Generic;
using Entanglement.Network;
using Entanglement.Managers;
using Entanglement.Representation;
using ModThatIsNotMod.BoneMenu;
using UnityEngine;

namespace Entanglement.UI
{
    public static class VoiceUI
    {
        private static MenuCategory voiceCategory;
        private static MenuCategory playersMuteCategory;
        private static MenuCategory microphoneCategory;

        public static void CreateUI(MenuCategory category)
        {
            voiceCategory = category.CreateSubCategory("Voice Menu", Color.white);

            // Voice Mode Selection
            voiceCategory.CreateEnumElement("Voice Mode", Color.yellow, global::Entanglement.Managers.VoiceChatManager.voiceChatMode, (value) =>
            {
                if (value is global::Entanglement.Managers.VoiceChatManager.VoiceChatMode mode)
                {
                    global::Entanglement.Managers.VoiceChatManager.SetVoiceChatMode(mode);
                }
            });

            // Microphone Selection
            voiceCategory.CreateFunctionElement("Select Microphone", Color.cyan, ShowMicrophoneMenu);

            // Microphone Volume Slider
            voiceCategory.CreateFloatElement("Mic Volume", Color.yellow, global::Entanglement.Managers.VoiceChatManager.microphoneVolume, (value) =>
            {
                global::Entanglement.Managers.VoiceChatManager.SetMicrophoneVolume(value);
            }, 0.1f, 0f, 1f);

            // Output Volume Slider
            voiceCategory.CreateFloatElement("Output Volume", Color.yellow, global::Entanglement.Managers.VoiceChatManager.outputVolume, (value) =>
            {
                global::Entanglement.Managers.VoiceChatManager.SetOutputVolume(value);
            }, 0.1f, 0f, 1f);

            // Proximity Range (only shown in proximity mode)
            if (global::Entanglement.Managers.VoiceChatManager.voiceChatMode == global::Entanglement.Managers.VoiceChatManager.VoiceChatMode.Proximity)
            {
                voiceCategory.CreateFloatElement("Proximity Range (m)", Color.yellow, global::Entanglement.Managers.VoiceChatManager.proximityRange, (value) =>
                {
                    global::Entanglement.Managers.VoiceChatManager.SetProximityRange(value);
                }, 5f, 10f, 500f);
            }

            // Player Mute/Unmute Menu
            playersMuteCategory = voiceCategory.CreateSubCategory("Player Mute List", Color.red);
            playersMuteCategory.CreateFunctionElement("Refresh", Color.white, RefreshMuteList);
            RefreshMuteList();
        }

        private static void ShowMicrophoneMenu()
        {
            try
            {
                List<string> microphones = global::Entanglement.Managers.VoiceChatManager.GetAvailableMicrophones();

                if (microphones.Count == 0)
                {
                    EntangleLogger.Error("No microphones found!");
                    return;
                }

                if (microphoneCategory == null)
                {
                    microphoneCategory = voiceCategory.CreateSubCategory("Select Microphone", Color.white);
                }

                List<string> existingElements = new List<string>();
                foreach (MenuElement element in microphoneCategory.elements)
                    existingElements.Add(element.displayText);

                foreach (string elementName in existingElements)
                    microphoneCategory.RemoveElement(elementName);

                for (int i = 0; i < microphones.Count; i++)
                {
                    string micName = microphones[i];
                    int index = i; // Capture for closure

                    microphoneCategory.CreateFunctionElement(micName, Color.white, () =>
                    {
                        global::Entanglement.Managers.VoiceChatManager.SetMicrophone(index);
                        EntangleLogger.Log($"Selected microphone: {micName}", ConsoleColor.Green);
                        MenuManager.OpenCategory(voiceCategory);
                    });
                }

                MenuManager.OpenCategory(microphoneCategory);
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error showing microphone menu: {ex.Message}");
            }
        }

        private static void RefreshMuteList()
        {
            try
            {
                // Clear existing mute entries
                List<string> elementsToRemove = new List<string>();
                foreach (MenuElement element in playersMuteCategory.elements)
                {
                    if (element.displayText != "Refresh")
                        elementsToRemove.Add(element.displayText);
                }

                foreach (string element in elementsToRemove)
                    playersMuteCategory.RemoveElement(element);

                // Get all players
                Dictionary<ulong, string> allPlayers = global::Entanglement.Managers.VoiceChatManager.GetAllPlayersForVoiceMenu();

                if (allPlayers.Count == 0)
                {
                    playersMuteCategory.CreateFunctionElement("No players in game", Color.gray, () => { });
                    return;
                }

                // Add each player with mute toggle
                foreach (var player in allPlayers)
                {
                    ulong playerId = player.Key;
                    string playerName = player.Value;
                    bool isMuted = global::Entanglement.Managers.VoiceChatManager.IsPlayerMuted(playerId);

                    Color playerColor = isMuted ? Color.red : Color.green;

                    playersMuteCategory.CreateFunctionElement(
                        $"{playerName} {(isMuted ? "[MUTED]" : "")}",
                        playerColor,
                        () => TogglePlayerMute(playerId, playerName)
                    );
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error refreshing mute list: {ex.Message}");
            }
        }

        private static void TogglePlayerMute(ulong playerId, string playerName)
        {
            try
            {
                if (global::Entanglement.Managers.VoiceChatManager.IsPlayerMuted(playerId))
                {
                    global::Entanglement.Managers.VoiceChatManager.UnmutePlayer(playerId);
                    EntangleLogger.Log($"Unmuted {playerName}", ConsoleColor.Green);
                }
                else
                {
                    global::Entanglement.Managers.VoiceChatManager.MutePlayer(playerId);
                    EntangleLogger.Log($"Muted {playerName}", ConsoleColor.Red);
                }

                // Refresh the mute list to show updated status
                RefreshMuteList();
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error toggling mute for {playerName}: {ex.Message}");
            }
        }
    }
}

