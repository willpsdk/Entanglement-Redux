using System;
using System.Collections.Generic;

using Entanglement.Network;
using Entanglement.Data;

using ModThatIsNotMod.BoneMenu;

using UnityEngine;
using Steamworks; // FIXED: Swapped Discord for Steamworks
using MelonLoader;

namespace Entanglement.UI {
    public static class ServerUI {
        static MenuCategory playersCategory;

        const string refreshText = "Refresh";

        public static void CreateUI(MenuCategory category) {
            MenuCategory serverCategory = category.CreateSubCategory("Server Menu", Color.white);

            serverCategory.CreateFunctionElement("Start Server", Color.white, () => { Server.StartServer(); });

            serverCategory.CreateFunctionElement("Stop Server", Color.white, () => {
                if (Server.instance != null)
                    Server.instance.Shutdown();
            });

            serverCategory.CreateFunctionElement("Disconnect", Color.white, () => {
                if (Node.activeNode is Client client) {
                    client.DisconnectFromServer();
                }
            });

            MenuCategory serverPrefsCategory = serverCategory.CreateSubCategory("Server Settings", Color.white);

            serverPrefsCategory.CreateIntElement("Max Players", Color.white, 8, (value) => {
                Server.maxPlayers = (byte)value;
                Server.instance?.UpdateLobbyConfig();
            },
            1, Server.serverMinimum, Server.serverCapacity, true);

            serverPrefsCategory.CreateBoolElement("Locked", Color.white, false, (value) =>
            {
                Server.isLocked = value;
                Server.instance?.UpdateLobbyConfig();
            });

            serverPrefsCategory.CreateEnumElement("Visibility", Color.white, LobbyType.Private, (value) =>
            {
                if (!(value is LobbyType)) return;

                LobbyType type = (LobbyType)value;
                Server.lobbyType = type;
                Server.instance?.UpdateLobbyConfig();
            });

            playersCategory = serverCategory.CreateSubCategory("Players", Color.white);

            playersCategory.CreateFunctionElement(refreshText, Color.white, Refresh);
        }

        public static void ClearPlayers() {
            List<string> elementsToRemove = new List<string>();
            foreach (MenuElement element in playersCategory.elements) {
                if (element.displayText != refreshText) elementsToRemove.Add(element.displayText);
            }

            foreach (string element in elementsToRemove) playersCategory.RemoveElement(element);
        }

        public static void Refresh() {
            ClearPlayers();

            if (!SteamIntegration.hasLobby) {
                UpdateMenu();
                return;
            }

            // FIXED: Steam uses GetNumLobbyMembers and GetLobbyMemberByIndex instead of an enumerable list
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobbyId);

            for (int i = 0; i < memberCount; i++) {
                CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(SteamIntegration.lobbyId, i);
                if (memberId == SteamIntegration.currentUser)
                    continue;

                AddUser(memberId);
            }

            UpdateMenu();
        }

        public static void UpdateMenu() => MenuManager.OpenCategory(playersCategory);

        // FIXED: Swapped Discord User for CSteamID
        public static void AddUser(CSteamID player) {
            string playerName = SteamFriends.GetFriendPersonaName(player);
            Color playerColor = Color.white;
            
            // FIXED: Replaced Discord lobby.OwnerId with SteamIntegration hostUser
            if (player == SteamIntegration.hostUser) { 
                playerName += " (Host)";
                playerColor = Color.yellow;
            }

            MenuCategory userItem = playersCategory.CreateSubCategory(playerName, playerColor);
            if (SteamIntegration.isHost) {
                userItem.CreateFunctionElement("Kick", Color.red, () => {
                    if (!SteamIntegration.isHost) return;

                    Server.instance?.KickUser(player.m_SteamID, playerName);

                    Refresh();
                });

                userItem.CreateFunctionElement("Ban", Color.red, () => {
                    if (!SteamIntegration.isHost) return;

                    // WARNING: Ensure BanList.cs is updated to accept ulong and string!
                    BanList.BanUser(player.m_SteamID, playerName); 
                    Server.instance.KickUser(player.m_SteamID, playerName, DisconnectReason.Banned);

                    Refresh();
                });

                userItem.CreateFunctionElement("Teleport To", Color.yellow, () => { Server.instance?.TeleportTo(player.m_SteamID); });
            }
            
            // Note: Discord's voiceManager UI elements were removed because Steamworks 
            // does not have a native individual player volume mixer in the base API.
        }
    }
}