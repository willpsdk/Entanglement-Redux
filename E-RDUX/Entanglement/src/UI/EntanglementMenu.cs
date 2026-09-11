using UnityEngine;

using ModThatIsNotMod.BoneMenu;

namespace Entanglement.UI
{
    // Fusion-style menu shell for Entanglement.
    // Fusion opens one multiplayer panel with tabs (Location, Matchmaking, Notifications…).
    // On BONEWORKS we map that to BoneMenu categories under a single root, opened from the
    // wrist circle. Expand page helpers here as we grow — keep radial entries thin.
    public static class EntanglementMenu
    {
        public static MenuCategory Root { get; private set; }
        public static MenuCategory Downloads => DownloadsUI.downloadsCategory;

        public static void Create() {
            // Same product entry Fusion uses: one named hub, not a scatter of top-level mods
            Root = MenuManager.CreateCategory("Entanglement", Color.white);

            // Page order mirrors Fusion's mental model:
            //   Lobby/Location → Matchmaking → Downloads (notifications) → Settings → Voice → Modes
            ServerUI.CreateUI(Root);       // Lobby / host controls / players
            LobbiesUI.CreateUI(Root);      // Matchmaking / public lobbies
            DownloadsUI.CreateUI(Root);    // Consent inbox (Fusion Notifications pattern)
            ClientUI.CreateUI(Root);
            BanlistUI.CreateUI(Root);
            VoiceUI.CreateUI(Root);
            SyncUI.CreateUI(Root);         // File sync / download settings
            GamemodeUI.CreateUI(Root);
            StatsUI.CreateUI(Root);

#if DEBUG
            DebugUI.CreateUI(Root);
#endif
        }

        public static void OpenRoot() {
            if (Root != null)
                MenuManager.OpenCategory(Root);
        }

        public static void OpenDownloads() {
            DownloadsUI.Open();
        }
    }
}
