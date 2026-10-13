using System;

using Entanglement.Gamemodes;
using Entanglement.Network;
using Entanglement.Network.Layers;

using ModThatIsNotMod.BoneMenu;

using UnityEngine;

namespace Entanglement.UI
{
    /// <summary>
    /// ModThatIsNotMod BoneMenu root for Entanglement Redux.
    /// Radial (<see cref="GameRadial"/>) stays the primary hub; BoneMenu mirrors
    /// gamemodes, transport, and quick lobby actions.
    /// </summary>
    public static class EntanglementBoneMenu
    {
        static MenuCategory root;
        static MenuCategory gamemodes;
        static bool built;
        static bool hooked;

        public static void Create() {
            if (built)
                return;
            try {
                root = MenuManager.CreateCategory("Entanglement Redux", new Color(0.35f, 0.75f, 1f));
                if (root == null) {
                    EntangleLogger.Warn("[BoneMenu] CreateCategory returned null.");
                    return;
                }

                root.CreateFunctionElement("Open Radial Menu", Color.white, () => {
                    if (!GameRadial.Open())
                        GameNotify.Show("Radial menu not ready yet.", 3f, GameNotify.Kind.Warning);
                });

                root.CreateFunctionElement("Start Server", Color.green, () => {
                    Server.StartServer();
                    GameNotify.Show("Lobby has been created!", 4f, GameNotify.Kind.Success);
                });

                root.CreateFunctionElement("Stop / Disconnect", Color.red, () => {
                    if (Server.instance != null || (SteamIntegration.hasLobby && SteamIntegration.isHost)) {
                        if (Server.StopHosting())
                            GameNotify.Show("Server stopped.", 3f, GameNotify.Kind.Success);
                        else
                            GameNotify.Show("Couldn't stop the server.", 3f, GameNotify.Kind.Warning);
                    }
                    else if (Node.activeNode is Client client) {
                        client.DisconnectFromServer();
                    }
                    else {
                        GameNotify.Show("Not in a server.", 3f);
                    }
                });

                root.CreateFunctionElement("Cycle Transport (Steam / SteamVR)", Color.yellow, () => {
                    string next = NetworkLayerManager.CyclePreferredTitle();
                    GameNotify.ShowTitle(
                        "Transport",
                        next + " selected. Restart the game to apply AppID changes. Active now: "
                            + NetworkLayerManager.ActiveTitle,
                        5f);
                });

                root.CreateFunctionElement("Show Transport", Color.grey, () => {
                    GameNotify.ShowTitle(
                        "Transport",
                        "Active: " + NetworkLayerManager.ActiveTitle
                            + " | Preferred: " + NetworkPrefs.ResolvedLayerTitle(),
                        4f);
                });

                gamemodes = root.CreateSubCategory("Gamemodes", new Color(1f, 0.55f, 0.2f));
                BuildGamemodes();

                root.CreateFunctionElement("Notifications Inbox", Color.cyan, () => {
                    int count = GameNotify.Inbox.Count;
                    string line = count <= 0
                        ? "No notifications."
                        : (count == 1 ? "1 notification — open Radial → Notifications." : count + " notifications — open Radial → Notifications.");
                    GameNotify.Show(line, 3f);
                });

                HookGamemodeEvents();
                built = true;
                EntangleLogger.Log("[BoneMenu] Entanglement Redux category ready.");
            }
            catch (Exception e) {
                EntangleLogger.Warn("[BoneMenu] Create failed: " + e.Message);
            }
        }

        static void HookGamemodeEvents() {
            if (hooked)
                return;
            hooked = true;
            GamemodeManager.OnGamemodeChanged += _ => NotifyActive();
            GamemodeManager.OnGamemodeStarted += mode => {
                GameNotify.ShowTitle("Gamemode", (mode != null ? mode.Title : "?") + " started.", 3f, GameNotify.Kind.Success);
            };
            GamemodeManager.OnGamemodeStopped += mode => {
                GameNotify.ShowTitle("Gamemode", (mode != null ? mode.Title : "?") + " stopped.", 3f);
            };
        }

        static void NotifyActive() {
            string active = GamemodeManager.Active != null
                ? GamemodeManager.Active.Title + (GamemodeManager.Active.IsStarted ? " (live)" : " (selected)")
                : "Sandbox";
            GameNotify.ShowTitle("Gamemode", active, 2.5f, GameNotify.Kind.Info, "gamemode-active", false);
        }

        static void BuildGamemodes() {
            if (gamemodes == null)
                return;

            try {
                EnsureFunction(gamemodes, "Show Active Mode", Color.white, () => {
                    string active = GamemodeManager.Active != null
                        ? GamemodeManager.Active.Title + (GamemodeManager.Active.IsStarted ? " (live)" : " (selected)")
                        : "Sandbox";
                    GameNotify.ShowTitle("Gamemode", active, 3f);
                });

                for (int i = 0; i < GamemodeManager.Gamemodes.Count; i++) {
                    Gamemode mode = GamemodeManager.Gamemodes[i];
                    if (mode == null)
                        continue;
                    Gamemode captured = mode;
                    EnsureFunction(gamemodes, "Select " + captured.Title, Color.grey, () => {
                        GamemodeManager.Select(captured);
                    });
                }

                EnsureFunction(gamemodes, "Start Mode", Color.green, () => GamemodeManager.StartActive());
                EnsureFunction(gamemodes, "Stop Mode", Color.red, () => GamemodeManager.Stop());
                EnsureFunction(gamemodes, "Clear Selection", Color.yellow, () => GamemodeManager.Deselect());
            }
            catch (Exception e) {
                EntangleLogger.Warn("[BoneMenu] Gamemodes build: " + e.Message);
            }
        }

        static void EnsureFunction(MenuCategory category, string label, Color color, Action action) {
            if (category == null || string.IsNullOrEmpty(label) || action == null)
                return;
            if (MenuHas(category, label))
                return;
            category.CreateFunctionElement(label, color, action);
        }

        static bool MenuHas(MenuCategory category, string label) {
            if (category == null || category.elements == null)
                return false;
            for (int i = 0; i < category.elements.Count; i++) {
                MenuElement element = category.elements[i];
                if (element != null && element.displayText == label)
                    return true;
            }
            return false;
        }
    }
}
