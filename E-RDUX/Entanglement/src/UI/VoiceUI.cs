using System.Collections.Generic;

using UnityEngine;

using Steamworks;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Network;
using Entanglement.Voice;

namespace Entanglement.UI {
    public static class VoiceUI {
        static MenuCategory muteCategory;
        const string refreshText = "Refresh";

        public static void CreateUI(MenuCategory category) {
            MenuCategory voiceCategory = category.CreateSubCategory("Voice Settings", Color.cyan);

            voiceCategory.CreateBoolElement("Voice Chat", Color.white, true, (value) => {
                VoiceManager.micEnabled = value;
            });

            voiceCategory.CreateEnumElement("Mode", Color.white, VoiceMode.Proximity, (value) => {
                if (!(value is VoiceMode)) return;

                VoiceManager.mode = (VoiceMode)value;
                VoiceManager.ApplySettings();
            });

            voiceCategory.CreateIntElement("Proximity Range", Color.white, 12, (value) => {
                VoiceManager.proximityRange = value;
                VoiceManager.ApplySettings();
            },
            2, 2, 100, true);

            voiceCategory.CreateIntElement("Volume %", Color.white, 100, (value) => {
                VoiceManager.outputVolume = value;
                VoiceManager.ApplySettings();
            },
            10, 0, 200, true);

            muteCategory = voiceCategory.CreateSubCategory("Mute Players", Color.red);
            muteCategory.CreateFunctionElement(refreshText, Color.white, RefreshMuteList);

            // The mod uses Steam's voice, so the mic is whatever Steam is set to record from -
            // there's nothing to pick in here. This just tells you where to change it.
            voiceCategory.CreateFunctionElement("How to change mic", Color.yellow, () => {
                Notifications.SendNotification("Voice uses your Steam mic.\nChange it in Steam: Settings > Voice > Voice Input Device.\nIn VR, open the Steam overlay (not SteamVR) to reach Steam Settings.", 10f);
            });
        }

        static void ClearMuteList() {
            List<string> elementsToRemove = new List<string>();
            foreach (MenuElement element in muteCategory.elements) {
                if (element.displayText != refreshText) elementsToRemove.Add(element.displayText);
            }

            foreach (string element in elementsToRemove) muteCategory.RemoveElement(element);
        }

        static void RefreshMuteList() {
            ClearMuteList();

            if (SteamIntegration.hasLobby) {
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobby);

                for (int m = 0; m < memberCount; m++) {
                    long userId = (long)SteamMatchmaking.GetLobbyMemberByIndex(SteamIntegration.lobby, m).m_SteamID;

                    if (userId == SteamIntegration.currentUserId)
                        continue;

                    muteCategory.CreateBoolElement($"Mute {SteamIntegration.GetUserName(userId)}", Color.white, VoiceManager.IsMuted(userId), (value) => {
                        VoiceManager.SetMuted(userId, value);
                    });
                }
            }

            MenuManager.OpenCategory(muteCategory);
        }
    }
}
