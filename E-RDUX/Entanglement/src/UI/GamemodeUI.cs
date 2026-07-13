using System.Collections.Generic;

using UnityEngine;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Network;
using Entanglement.Gamemodes;

namespace Entanglement.UI {
    public static class GamemodeUI {
        static MenuCategory scoresCategory;
        const string refreshText = "Refresh Scores";

        public static void CreateUI(MenuCategory category) {
            MenuCategory gmCategory = category.CreateSubCategory("Gamemodes", Color.magenta);

            MenuCategory hostControls = gmCategory.CreateSubCategory("Host Controls", Color.yellow);

            // One button per mode - picks the mode and starts the first round together, so there's
            // no separate "now start a round" step to hunt for. Refuses (with a reason on screen)
            // if you're alone or a round's already going.
            foreach (EntanglementGamemode mode in GamemodeHandler.registeredModes.Values) {
                hostControls.CreateFunctionElement($"Play: {mode.DisplayName}", mode.MenuColor, () => {
                    if (!Node.isServer) return;
                    if (!GamemodeHandler.TryStartMatch(mode.Id, out string reason))
                        Notifications.SendNotification(reason, 4f);
                });
            }

            hostControls.CreateFunctionElement("Force Stop Gamemode", Color.red, () => {
                if (!Node.isServer) return;
                GamemodeHandler.StopMode();
            });

            MenuCategory timerControls = hostControls.CreateSubCategory("Round Timer", Color.cyan);

            // Pre-round default override. 0 hands it back to whatever the mode itself sets.
            timerControls.CreateIntElement("Default Length (s, 0 = mode default)", Color.white, 0, (value) => {
                if (!Node.isServer) return;
                GamemodeHandler.SetRoundDuration(value);
            },
            30, 0, 3600, true);

            timerControls.CreateIntElement("Set Time Left (s)", Color.white, 0, (value) => {
                if (!Node.isServer) return;
                if (!GamemodeHandler.RoundActive) { Notifications.SendNotification("No round is running.", 3f); return; }
                GamemodeHandler.SetRoundTimeRemaining(value);
                Notifications.SendNotification($"Time left: {(int)GamemodeHandler.RoundTimeRemaining}s", 3f);
            },
            30, 0, 3600, true);

            timerControls.CreateFunctionElement("Add 60 seconds", Color.green, () => AdjustTime(60f));
            timerControls.CreateFunctionElement("Remove 60 seconds", Color.red, () => AdjustTime(-60f));

            scoresCategory = gmCategory.CreateSubCategory("Scores", Color.white);
            scoresCategory.CreateFunctionElement(refreshText, Color.white, RefreshScores);
        }

        // Nudges the running round's clock and tells you what it landed on - the buttons looked
        // dead before because nothing on screen changed when there was no round to adjust.
        static void AdjustTime(float delta) {
            if (!Node.isServer) return;
            if (!GamemodeHandler.RoundActive) { Notifications.SendNotification("No round is running.", 3f); return; }
            GamemodeHandler.AddRoundTime(delta);
            Notifications.SendNotification($"Time left: {(int)GamemodeHandler.RoundTimeRemaining}s", 3f);
        }

        static void ClearScores() {
            List<string> toRemove = new List<string>();
            foreach (MenuElement element in scoresCategory.elements) {
                if (element.displayText != refreshText) toRemove.Add(element.displayText);
            }
            foreach (string element in toRemove) scoresCategory.RemoveElement(element);
        }

        static void RefreshScores() {
            ClearScores();

            if (GamemodeHandler.ActiveMode == null) {
                scoresCategory.CreateFunctionElement("No gamemode active", Color.grey, () => { });
                MenuManager.OpenCategory(scoresCategory);
                return;
            }

            scoresCategory.CreateFunctionElement($"Mode: {GamemodeHandler.ActiveMode.DisplayName}", Color.white, () => { });
            scoresCategory.CreateFunctionElement(GamemodeHandler.RoundActive ? $"Round time left: {(int)GamemodeHandler.RoundTimeRemaining}s" : "No round active", Color.white, () => { });

            foreach (var pair in GamemodeHandler.scores) {
                string name = SteamIntegration.GetUserName(pair.Key);
                string tag = GamemodeHandler.eliminated.Contains(pair.Key) ? " (eliminated)" : "";
                scoresCategory.CreateFunctionElement($"{name}: {pair.Value}{tag}", Color.white, () => { });
            }

            MenuManager.OpenCategory(scoresCategory);
        }
    }
}
