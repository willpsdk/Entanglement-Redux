using System.Collections.Generic;

using UnityEngine;

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

            foreach (EntanglementGamemode mode in GamemodeHandler.registeredModes.Values) {
                hostControls.CreateFunctionElement($"Start: {mode.DisplayName}", mode.MenuColor, () => {
                    if (!Node.isServer) return;
                    GamemodeHandler.StartMode(mode.Id);
                });
            }

            hostControls.CreateFunctionElement("Force Stop Gamemode", Color.red, () => {
                if (!Node.isServer) return;
                GamemodeHandler.StopMode();
            });

            hostControls.CreateFunctionElement("Start Round", Color.green, () => {
                if (!Node.isServer) return;
                GamemodeHandler.StartRound();
            });

            // 0 means "use the mode's own default" - see GamemodeHandler.roundDurationOverrideSeconds
            hostControls.CreateIntElement("Round Duration (s, 0 = mode default)", Color.white, 0, (value) => {
                if (!Node.isServer) return;
                GamemodeHandler.SetRoundDuration(value);
            },
            30, 0, 3600, true);

            hostControls.CreateIntElement("Set Time Remaining (s)", Color.white, 0, (value) => {
                if (!Node.isServer) return;
                GamemodeHandler.SetRoundTimeRemaining(value);
            },
            30, 0, 3600, true);

            hostControls.CreateFunctionElement("+60s", Color.white, () => {
                if (!Node.isServer) return;
                GamemodeHandler.AddRoundTime(60f);
            });

            hostControls.CreateFunctionElement("-60s", Color.white, () => {
                if (!Node.isServer) return;
                GamemodeHandler.AddRoundTime(-60f);
            });

            scoresCategory = gmCategory.CreateSubCategory("Scores", Color.white);
            scoresCategory.CreateFunctionElement(refreshText, Color.white, RefreshScores);
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
