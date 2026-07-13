using System.Reflection;

using UnityEngine;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Network;
using Entanglement.Managers;
using Entanglement.Representation;

namespace Entanglement.UI {
    public static class EntanglementUI {
        static MenuCategory rootCategory;
        static MenuElement suicideElement;
        static bool lastInServer;

        public static void CreateUI() {
            rootCategory = MenuManager.CreateCategory("Entanglement Redux", Color.white);

            ServerUI.CreateUI(rootCategory);
            ClientUI.CreateUI(rootCategory);
            BanlistUI.CreateUI(rootCategory);
            LobbiesUI.CreateUI(rootCategory);
            VoiceUI.CreateUI(rootCategory);
            SyncUI.CreateUI(rootCategory);
            GamemodeUI.CreateUI(rootCategory);

            // Net Stats near the bottom, so the actually-useful buttons aren't buried under it
            StatsUI.CreateUI(rootCategory);

#if DEBUG
            DebugUI.CreateUI(rootCategory);
#endif

            // Created last so it starts at the bottom out of a server. UpdateUI lifts it to the
            // top the moment you join one and drops it back down when you leave.
            rootCategory.CreateFunctionElement("Suicide", Color.red, Suicide);
            suicideElement = rootCategory.elements[rootCategory.elements.Count - 1];
        }

        // Polled every frame from Mod.OnUpdate. Only does anything the frame your server state
        // actually flips, so it's a cheap no-op the rest of the time.
        public static void UpdateUI() {
            bool inServer = SteamIntegration.hasLobby;
            if (inServer == lastInServer)
                return;
            lastInServer = inServer;

            MoveSuicideButton(inServer);
        }

        // BoneMenu has no insert-at-index, so we pull the element out of the list and drop it back
        // in where we want it - top while you're in a server, bottom while you're not.
        static void MoveSuicideButton(bool toTop) {
            if (rootCategory == null || suicideElement == null)
                return;

            rootCategory.elements.Remove(suicideElement);
            if (toTop)
                rootCategory.elements.Insert(0, suicideElement);
            else
                rootCategory.elements.Add(suicideElement);

            // If they're staring at the root menu right now, redraw it so the move shows straight
            // away instead of waiting for them to back out and reopen it.
            if (GetActiveCategory() == rootCategory)
                MenuManager.OpenCategory(rootCategory);
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
    }
}
