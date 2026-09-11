using UnityEngine;

using ModThatIsNotMod.BoneMenu;

namespace Entanglement.UI
{
    // Fusion Settings tab: nests client, voice, file sync, safety (banlist), net stats
    public static class SettingsUI
    {
        public static MenuCategory settingsCategory;

        public static void CreateUI(MenuCategory category) {
            settingsCategory = category.CreateSubCategory("Settings", new Color(0.7f, 0.75f, 0.85f));

            ClientUI.CreateUI(settingsCategory);
            VoiceUI.CreateUI(settingsCategory);
            SyncUI.CreateUI(settingsCategory);
            BanlistUI.CreateUI(settingsCategory);
            StatsUI.CreateUI(settingsCategory);
        }

        public static void Open() {
            if (settingsCategory != null)
                MenuManager.OpenCategory(settingsCategory);
        }
    }
}
