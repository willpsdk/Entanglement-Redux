using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Network;
using Entanglement.Managers;

namespace Entanglement.UI {
    // Call sites for Mod.cs — page tree lives in EntanglementMenu
    public static class EntanglementUI {
        public static void CreateUI() {
            EntanglementMenu.Create();
        }

        public static void UpdateUI() {
            // Placeholder for live status polish (Profile refresh-on-open covers the common case)
        }

        public static void OpenRootMenu() => EntanglementMenu.OpenRoot();
    }
}
