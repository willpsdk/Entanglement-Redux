using System.Reflection;

using UnityEngine;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Network;
using Entanglement.Managers;

namespace Entanglement.UI {
    // Thin facade kept for existing call sites (Mod.CreateUI / UpdateUI).
    // Real page tree lives in EntanglementMenu (Fusion-style shell).
    public static class EntanglementUI {
        static MenuElement suicideElement;
        static bool lastInServer;

        public static void CreateUI() {
            EntanglementMenu.Create();

            // Suicide stays on the root so it's one click while in a lobby
            EntanglementMenu.Root.CreateFunctionElement("Suicide", Color.red, Suicide);
            suicideElement = EntanglementMenu.Root.elements[EntanglementMenu.Root.elements.Count - 1];
        }

        public static void UpdateUI() {
            bool inServer = SteamIntegration.hasLobby;
            if (inServer == lastInServer)
                return;
            lastInServer = inServer;

            MoveSuicideButton(inServer);
        }

        static void MoveSuicideButton(bool toTop) {
            MenuCategory root = EntanglementMenu.Root;
            if (root == null || suicideElement == null)
                return;

            root.elements.Remove(suicideElement);
            if (toTop)
                root.elements.Insert(0, suicideElement);
            else
                root.elements.Add(suicideElement);

            if (GetActiveCategory() == root)
                MenuManager.OpenCategory(root);
        }

        static MenuCategory GetActiveCategory() {
            return typeof(MenuManager)
                .GetField("activeCategory", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as MenuCategory;
        }

        static void Suicide() {
            if (!SteamIntegration.hasLobby) {
                Notifications.SendNotification("You need to be in a server to do that.", 3f);
                return;
            }

            PlayerDeathManager.Suicide();
        }

        public static void OpenRootMenu() => EntanglementMenu.OpenRoot();
    }
}
