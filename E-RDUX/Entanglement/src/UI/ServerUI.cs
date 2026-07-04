using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Network;
using Entanglement.Data;

using ModThatIsNotMod.BoneMenu;

using UnityEngine;

using Steamworks;

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

            serverCategory.CreateFunctionElement("Invite Friends", Color.white, () => {
                if (SteamIntegration.hasLobby)
                    SteamFriends.ActivateGameOverlayInviteDialog(SteamIntegration.lobby);
                else
                    EntangleLogger.Error("You aren't in a server!");
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

            serverPrefsCategory.CreateEnumElement("Visibility", Color.white, ServerVisibility.Private, (value) =>
            {
                if (!(value is ServerVisibility)) return;

                ServerVisibility visibility = (ServerVisibility)value;
                Server.visibility = visibility;
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

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobby);

            for (int m = 0; m < memberCount; m++) {
                long userId = (long)SteamMatchmaking.GetLobbyMemberByIndex(SteamIntegration.lobby, m).m_SteamID;

                if (userId == SteamIntegration.currentUserId)
                    continue;

                AddUser(userId, SteamIntegration.GetUserName(userId));
            }

            UpdateMenu();
        }

        public static void UpdateMenu() => MenuManager.OpenCategory(playersCategory);

        public static void AddUser(long userId, string userName) {
            string playerName = userName;
            Color playerColor = Color.white;
            if (userId == SteamIntegration.lobbyOwnerId) {
                playerName += " (Host)";
                playerColor = Color.yellow;
            }

            MenuCategory userItem = playersCategory.CreateSubCategory(playerName, playerColor);
            if (SteamIntegration.isHost) {
                userItem.CreateFunctionElement("Kick", Color.red, () => {
                    if (!SteamIntegration.isHost) return;

                    Server.instance?.KickUser(userId, playerName);

                    Refresh();
                });

                userItem.CreateFunctionElement("Ban", Color.red, () => {
                    if (!SteamIntegration.isHost) return;

                    BanList.BanUser(userId, userName);
                    Server.instance.KickUser(userId, playerName, DisconnectReason.Banned);

                    Refresh();
                });

                userItem.CreateFunctionElement("Teleport To", Color.yellow, () => { Server.instance?.TeleportTo(userId); });
            }

            userItem.CreateFunctionElement("View Steam Profile", Color.white, () => {
                SteamFriends.ActivateGameOverlayToUser("steamid", new CSteamID((ulong)userId));
            });
        }
    }
}
