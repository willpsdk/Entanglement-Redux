using System;
using System.Collections.Generic;

using Entanglement.Gamemodes;
using Entanglement.Network;

using UnityEngine;

namespace Entanglement.UI
{
    /// <summary>
    /// Fusion <c>MenuGamemode</c> for Boneworks radial — overview, browse list,
    /// Select / Exit (host), Ready / Started status. Mirrors BONELAB Fusion's
    /// page_Overview + grid_GamemodeSelection (no Fusion MenuPage prefab on Boneworks).
    /// </summary>
    public static class MenuGamemode
    {
        /// <summary>Fusion SelectedGamemode — highlighted in the list, not yet committed.</summary>
        public static Gamemode SelectedGamemode { get; private set; }

        static Action _backToHub;
        static int browsePage;
        static bool hooked;

        /// <summary>Solo-test / browse helper — Fusion list highlight without committing.</summary>
        public static void Highlight(Gamemode mode) => SelectedGamemode = mode;

        public static void OnInitialize() {
            if (hooked)
                return;
            hooked = true;
            GamemodeManager.OnGamemodeChanged += _ => { };
            GamemodeManager.OnGamemodeStarted += _ => { };
            GamemodeManager.OnGamemodeStopped += _ => { };
            GamemodeManager.OnGamemodeReady += () => { };
            GamemodeManager.OnGamemodeUnready += () => { };
            GamemodeManager.OnStartTimerChanged += _ => { };
        }

        public static void Show(Action backToHub) {
            OnInitialize();
            _backToHub = backToHub ?? (() => { });
            SelectedGamemode = null;
            browsePage = 0;
            ShowOverview();
        }

        /// <summary>Fusion page_Overview — title, ready/started, host actions, browse.</summary>
        public static void ShowOverview() {
            var entries = new List<GameRadial.MenuEntry>();

            Gamemode active = GamemodeManager.Active;
            string title = active != null ? active.Title : "Sandbox";
            entries.Add(new GameRadial.MenuEntry(title, () => {
                string desc = active != null
                    ? (string.IsNullOrEmpty(active.Description) ? active.Title : active.Description)
                    : "No gamemode — free sandbox.";
                GameNotify.ShowTitle(title, desc, 4f, GameNotify.Kind.Info, "gamemode-info", false);
            }, true));

            entries.Add(new GameRadial.MenuEntry(ReadyLabel(), null, true));
            entries.Add(new GameRadial.MenuEntry(StartedLabel(), null, true));

            bool host = IsHostOrSolo();

            if (host && SelectedGamemode != null && !ReferenceEquals(SelectedGamemode, active)) {
                Gamemode pick = SelectedGamemode;
                entries.Add(new GameRadial.MenuEntry("Select " + pick.Title, () => {
                    GamemodeManager.Select(pick);
                    SelectedGamemode = null;
                    ShowOverview();
                }));
            }

            if (host && active != null) {
                entries.Add(new GameRadial.MenuEntry("Exit " + active.Title, () => {
                    GamemodeManager.Deselect();
                    SelectedGamemode = null;
                    ShowOverview();
                }));
            }

            if (host && active != null && active.IsSelected && !active.IsStarted) {
                if (!active.IsReady) {
                    entries.Add(new GameRadial.MenuEntry("Ready Up", () => {
                        GamemodeManager.Ready();
                        ShowOverview();
                    }));
                }
                entries.Add(new GameRadial.MenuEntry("Start Now", () => {
                    GamemodeManager.StartActive();
                    ShowOverview();
                }));
            }

            if (host && active != null && active.IsStarted) {
                entries.Add(new GameRadial.MenuEntry("Stop Mode", () => {
                    GamemodeManager.Stop();
                    ShowOverview();
                }));
            }

            if (host && active == null) {
                entries.Add(new GameRadial.MenuEntry("Browse Modes", ShowBrowse));
            }
            else if (host) {
                entries.Add(new GameRadial.MenuEntry("Change Mode", ShowBrowse));
            }
            else {
                entries.Add(new GameRadial.MenuEntry(
                    active != null ? "Host controls modes" : "Waiting for host",
                    null, true));
            }

            if (SelectedGamemode != null && !ReferenceEquals(SelectedGamemode, active)) {
                entries.Add(new GameRadial.MenuEntry(
                    "Picked: " + SelectedGamemode.Title,
                    () => {
                        string d = SelectedGamemode.Description;
                        GameNotify.ShowTitle(SelectedGamemode.Title,
                            string.IsNullOrEmpty(d) ? SelectedGamemode.Author : d,
                            4f, GameNotify.Kind.Info, "gamemode-pick", false);
                    }, true));
            }

            GameRadial.OpenBackPage(_backToHub, entries, false);
        }

        /// <summary>Fusion scrollRect_Gamemodes — highlight a mode, then Select on overview.</summary>
        public static void ShowBrowse() {
            if (!IsHostOrSolo()) {
                GameNotify.Show("Only the host can change gamemodes.", 3f, GameNotify.Kind.Warning);
                ShowOverview();
                return;
            }

            var modes = GamemodeManager.Gamemodes;
            const int pageSize = 5;
            int pages = modes.Count == 0 ? 1 : (modes.Count + pageSize - 1) / pageSize;
            if (browsePage >= pages)
                browsePage = 0;
            if (browsePage < 0)
                browsePage = 0;

            var entries = new List<GameRadial.MenuEntry>();
            entries.Add(new GameRadial.MenuEntry(
                modes.Count == 0 ? "No gamemodes" : ("Modes " + (browsePage + 1) + "/" + pages),
                null, true));

            int start = browsePage * pageSize;
            for (int i = start; i < modes.Count && i < start + pageSize; i++) {
                Gamemode mode = modes[i];
                if (mode == null)
                    continue;
                bool highlight = ReferenceEquals(SelectedGamemode, mode)
                    || (SelectedGamemode == null && ReferenceEquals(GamemodeManager.Active, mode));
                string label = (highlight ? "> " : "") + mode.Title;
                Gamemode captured = mode;
                entries.Add(new GameRadial.MenuEntry(label, () => {
                    SelectedGamemode = captured;
                    string d = captured.Description;
                    GameNotify.ShowTitle(captured.Title,
                        string.IsNullOrEmpty(d) ? ("by " + captured.Author) : d,
                        3f, GameNotify.Kind.Info, "gamemode-browse", false);
                    ShowOverview();
                }));
            }

            if (pages > 1) {
                entries.Add(new GameRadial.MenuEntry("Next Page", () => {
                    browsePage = (browsePage + 1) % pages;
                    ShowBrowse();
                }));
            }

            entries.Add(new GameRadial.MenuEntry("Sandbox", () => {
                SelectedGamemode = null;
                if (GamemodeManager.Active != null)
                    GamemodeManager.Deselect();
                ShowOverview();
            }));

            GameRadial.OpenBackPage(ShowOverview, entries, false);
        }

        static string ReadyLabel() {
            Gamemode active = GamemodeManager.Active;
            if (active == null)
                return "Ready: —";
            return active.IsReady ? "Ready" : "Not Ready";
        }

        static string StartedLabel() {
            Gamemode active = GamemodeManager.Active;
            if (active == null)
                return "Started: —";
            if (GamemodeManager.StartTimer >= 0f && active.IsReady && !active.IsStarted)
                return "Starting in " + Mathf.CeilToInt(GamemodeManager.StartTimer) + "s";
            return active.IsStarted ? "Started" : "Not Started";
        }

        static bool IsHostOrSolo() {
            return !SteamIntegration.hasLobby || SteamIntegration.isHost;
        }
    }
}
