using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

using Entanglement.Network;

using ModThatIsNotMod.BoneMenu;

using UnityEngine;

using Steamworks;

using MelonLoader;

namespace Entanglement.UI
{
    public static class LobbiesUI {
        static MenuCategory lobbiesCategory;

        static CallResult<LobbyMatchList_t> lobbyListResult;

        const string refreshText = "Refresh";

        public static void CreateUI(MenuCategory category) {
            lobbiesCategory = category.CreateSubCategory("Public Lobbies", Color.white);

            lobbiesCategory.CreateFunctionElement(refreshText, Color.white, Refresh);

            lobbyListResult = CallResult<LobbyMatchList_t>.Create(OnSteamLobbySearch);
        }

        public static void Refresh() {
            ClearMenuItems();

            // Only list lobbies created by Entanglement
            SteamMatchmaking.AddRequestLobbyListStringFilter("entanglement", "true", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            lobbyListResult.Set(SteamMatchmaking.RequestLobbyList());

            UpdateMenu();
        }

        public static void OnSteamLobbySearch(LobbyMatchList_t result, bool bIOFailure) {
            if (bIOFailure) {
                EntangleLogger.Log($"Failed to search for Public Lobbies!");
                return;
            }

            int lobbies = (int)result.m_nLobbiesMatching;

            EntangleLogger.Log($"Searched for {lobbies} Public Lobb{(lobbies == 1 ? "y" : "ies")}.");

            for (int i = 0; i < lobbies; i++) {
                CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
#if DEBUG
                EntangleLogger.Log($"Found Lobby with id {lobbyId.m_SteamID}.");
#endif
                AddLobby(lobbyId);
            }
        }

        public static void ClearMenuItems() {
            List<string> elementsToRemove = new List<string>();
            foreach (MenuElement element in lobbiesCategory.elements) {
                if (element.displayText != refreshText) elementsToRemove.Add(element.displayText);
            }

            foreach (string element in elementsToRemove) lobbiesCategory.RemoveElement(element);
        }

        public static void AddLobby(CSteamID lobbyId) {
#if DEBUG
            EntangleLogger.Log($"Trying to add lobby with id {lobbyId.m_SteamID}.");
#endif
            string hostName = SteamMatchmaking.GetLobbyData(lobbyId, "host_name");
            string scene = SteamMatchmaking.GetLobbyData(lobbyId, "scene");

            if (string.IsNullOrEmpty(hostName))
                hostName = "Unknown";

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            int memberLimit = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);

            string title = $"{hostName}'s Game ({memberCount}/{memberLimit})";
            if (!string.IsNullOrEmpty(scene))
                title += $" - {scene}";

            CreateLobbyItem(title, lobbyId);
        }

        public static void CreateLobbyItem(string name, CSteamID lobbyId) {
            lobbiesCategory.CreateFunctionElement(name, Color.white, () => {
                if (SteamIntegration.hasLobby) {
                    EntangleLogger.Error("Already in a server!");
                    return;
                }

                Client.instance.JoinLobby(lobbyId);
            });

            UpdateMenu();
        }

        public static void UpdateMenu() => MenuManager.OpenCategory(lobbiesCategory);
    }
}
