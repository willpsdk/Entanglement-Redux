using UnityEngine;

using ModThatIsNotMod.BoneMenu;

namespace Entanglement.UI
{
    // Full Fusion-style hub for Entanglement on BONEWORKS BoneMenu.
    // Fusion: Profile / Location / Matchmaking / Notifications / Settings
    // Here:   Profile / Lobby / Matchmaking / Players / Downloads / Settings
    public static class EntanglementMenu
    {
        public static MenuCategory Root { get; private set; }
        public static MenuCategory Downloads => DownloadsUI.downloadsCategory;

        public static void Create() {
            Root = MenuManager.CreateCategory("Entanglement", Color.white);

            ProfileUI.CreateUI(Root);      // who you are + suicide + status
            ServerUI.CreateUI(Root);       // Lobby (+ Players sibling)
            LobbiesUI.CreateUI(Root);      // Matchmaking / browse / join
            DownloadsUI.CreateUI(Root);    // consent inbox (Fusion Notifications)
            SettingsUI.CreateUI(Root);     // Client, Voice, File Sync, Banlist, Stats

#if DEBUG
            DebugUI.CreateUI(Root);
#endif
        }

        public static void OpenRoot() {
            if (Root != null)
                MenuManager.OpenCategory(Root);
        }

        public static void OpenDownloads() => DownloadsUI.Open();
        public static void OpenLobby() {
            // BoneMenu has no GetSubCategory helper; reopen root so Lobby is one click down
            OpenRoot();
        }
        public static void OpenMatchmaking() => LobbiesUI.Open();
        public static void OpenPlayers() => ServerUI.OpenPlayers();
        public static void OpenSettings() => SettingsUI.Open();
        public static void OpenProfile() => ProfileUI.Open();
    }
}
