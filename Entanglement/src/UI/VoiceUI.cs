using System;
using Entanglement.Network;
using ModThatIsNotMod.BoneMenu;
using UnityEngine;

namespace Entanglement.UI
{
    public static class VoiceUI
    {
        public static void CreateUI(MenuCategory category) {
            MenuCategory voiceCategory = category.CreateSubCategory("Voice Menu", Color.white);

            voiceCategory.CreateEnumElement("Voice Activity", Color.yellow, SteamIntegration.voiceStatus, (value) => { 
                SteamIntegration.UpdateVoice((VoiceStatus)value); 
            });

            // Note: Steam handles SelfMute natively via SteamIntegration.UpdateVoice.
            // Deafening via Steam P2P requires deeper audio buffer manipulation, 
            // so we've removed the specific Deafen UI toggle for this migration to prevent errors.
        }
    }
}