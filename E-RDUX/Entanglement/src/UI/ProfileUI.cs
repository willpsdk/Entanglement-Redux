using UnityEngine;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Network;
using Entanglement.Managers;

namespace Entanglement.UI
{
    // Fusion Profile tab: who you are + quick session actions
    public static class ProfileUI
    {
        static MenuCategory profileCategory;
        static MenuElement nameElement;
        static MenuElement statusElement;

        public static void CreateUI(MenuCategory category) {
            profileCategory = category.CreateSubCategory("Profile", new Color(0.85f, 0.9f, 1f));

            profileCategory.CreateFunctionElement($"You: {SteamIntegration.currentUserName}", Color.white, Refresh);
            nameElement = profileCategory.elements[profileCategory.elements.Count - 1];

            profileCategory.CreateFunctionElement(StatusText(), Color.white, Refresh);
            statusElement = profileCategory.elements[profileCategory.elements.Count - 1];

            profileCategory.CreateFunctionElement("Refresh Status", Color.white, Refresh);

            profileCategory.CreateFunctionElement("Suicide", Color.red, () => {
                if (!SteamIntegration.hasLobby) {
                    Notifications.SendNotification("You need to be in a server to do that.", 3f);
                    return;
                }
                PlayerDeathManager.Suicide();
            });

            profileCategory.CreateFunctionElement($"Version {EntanglementMod.VersionString}", Color.grey, () => { });
        }

        public static void Refresh() {
            if (profileCategory == null) return;

            if (nameElement != null)
                nameElement.displayText = $"You: {SteamIntegration.currentUserName}";
            if (statusElement != null)
                statusElement.displayText = StatusText();
        }

        public static void Open() {
            Refresh();
            if (profileCategory != null)
                MenuManager.OpenCategory(profileCategory);
        }

        static string StatusText() {
            if (!SteamIntegration.hasLobby)
                return "Status: Solo";
            if (SteamIntegration.isHost)
                return $"Status: Hosting ({MemberCount()} players)";
            return "Status: In lobby as guest";
        }

        static int MemberCount() {
            if (!SteamIntegration.hasLobby) return 0;
            return Steamworks.SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobby);
        }
    }
}
