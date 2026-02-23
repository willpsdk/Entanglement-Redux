using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Network;

using ModThatIsNotMod.BoneMenu;

using UnityEngine;

using Discord;

using MelonLoader;

namespace Entanglement.UI
{
    public static class VoiceUI
    {
        public static void CreateUI(MenuCategory category) {
            MenuCategory voiceCategory = category.CreateSubCategory("Voice Menu", Color.white);

            voiceCategory.CreateEnumElement("Voice Activity", Color.yellow, SteamIntegration.voiceStatus, (value) => { SteamIntegration.UpdateVoice((VoiceStatus)value); });

            voiceCategory.CreateBoolElement("Muted", Color.red, SteamIntegration.voiceManager.IsSelfMute(), (value) => { SteamIntegration.voiceManager.SetSelfMute(value); });

            voiceCategory.CreateBoolElement("Deafened", Color.red, SteamIntegration.voiceManager.IsSelfDeaf(), (value) => { SteamIntegration.voiceManager.SetSelfDeaf(value); });
        }
    }
}
