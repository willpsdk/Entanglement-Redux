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
        static string lastNameText;
        static string lastStatusText;

        public static void CreateUI(MenuCategory category) {
            profileCategory = category.CreateSubCategory("Profile", new Color(0.85f, 0.9f, 1f));

            profileCategory.CreateFunctionElement("Refresh Status", Color.white, Refresh);

            profileCategory.CreateFunctionElement("Suicide", Color.red, () => {
                if (!SteamIntegration.hasLobby) {
                    Notifications.SendNotification("You need to be in a server to do that.", 3f);
                    return;
                }
                PlayerDeathManager.Suicide();
            });

            profileCategory.CreateFunctionElement($"Version {EntanglementMod.VersionString}", Color.grey, () => { });

            Refresh();
        }

        public static void Refresh() {
            if (profileCategory == null) return;

            // BoneMenu displayText has no public setter — drop and recreate the status lines
            if (!string.IsNullOrEmpty(lastNameText))
                profileCategory.RemoveElement(lastNameText);
            if (!string.IsNullOrEmpty(lastStatusText))
                profileCategory.RemoveElement(lastStatusText);

            lastNameText = $"You: {SteamIntegration.currentUserName}";
            lastStatusText = StatusText();
            profileCategory.CreateFunctionElement(lastNameText, Color.white, Refresh);
            profileCategory.CreateFunctionElement(lastStatusText, Color.white, Refresh);
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
