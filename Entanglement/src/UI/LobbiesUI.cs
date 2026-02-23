using System;
using System.Collections.Generic;

using Entanglement.Network;
using ModThatIsNotMod.BoneMenu;
using UnityEngine;
using Steamworks;

namespace Entanglement.UI
{
    public static class LobbiesUI {
        static MenuCategory lobbiesCategory;
        const string refreshText = "Refresh Lobbies";

        // Stores the callback request so it isn't garbage collected
        static CallResult<LobbyMatchList_t> lobbyListResult;

        public static void CreateUI(MenuCategory category) {
            lobbiesCategory = category.CreateSubCategory("Public Lobbies", Color.white);
            lobbiesCategory.CreateFunctionElement(refreshText, Color.white, Refresh);

            lobbyListResult = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
        }

        public static void Refresh() {
            ClearMenuItems();

            EntangleLogger.Log("Searching for public Steam lobbies...");

            // Filter for worldwide lobbies
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
            lobbyListResult.Set(call);

            UpdateMenu();
        }

        public static void OnLobbyMatchList(LobbyMatchList_t result, bool bIOFailure) {
            if (bIOFailure) {
                EntangleLogger.Error("Failed to search for Steam Lobbies (IO Failure).");
                return;
            }

            int lobbyCount = (int)result.m_nLobbiesMatching;
            EntangleLogger.Log($"Found {lobbyCount} Public Lobb{(lobbyCount == 1 ? "y" : "ies")}.");

            for (int i = 0; i < lobbyCount; i++) {
                CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
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
            string lobbyName = SteamMatchmaking.GetLobbyData(lobbyId, "name");
            string version = SteamMatchmaking.GetLobbyData(lobbyId, "version");
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            int maxMembers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);

            if (string.IsNullOrEmpty(lobbyName))
                lobbyName = "Unnamed Server";

            // Hide lobbies that do not match our current mod version
            if (version != EntanglementMod.VersionString) return;

            CreateLobbyItem($"{lobbyName} ({memberCount}/{maxMembers})", lobbyId);
        }

        public static void CreateLobbyItem(string name, CSteamID lobbyId) {
            lobbiesCategory.CreateFunctionElement(name, Color.white, () => {
                if (SteamIntegration.hasLobby) {
                    EntangleLogger.Error("Already in a server!");
                    return;
                }

                Client.instance?.JoinLobby(lobbyId);
            });

            UpdateMenu();
        }

        public static void UpdateMenu() => MenuManager.OpenCategory(lobbiesCategory);
    }
}