using System;

using Entanglement.Network;
using Entanglement.UI;

namespace Entanglement
{
    /// <summary>
    /// Thin join/leave helpers — all player-facing text goes through GameNotify
    /// (Boneworks PopupText / subtitle path, Fusion Notifier ideology).
    /// </summary>
    public static class EntangleNotif {
        public static void PlayerJoin(string username) {
            GameNotify.ShowTitle("Joined", username + " has joined!", 4f, GameNotify.Kind.Success, "join:" + username);
        }

        public static void PlayerLeave(string username) {
            GameNotify.ShowTitle("Left", username + " left.", 4f, GameNotify.Kind.Info, "leave:" + username);
        }

        public static void PlayerDisconnect(DisconnectReason reason, string additionalReason = "") {
            if (reason == DisconnectReason.MissingMods && !string.IsNullOrEmpty(additionalReason))
                GameNotify.ShowTitle("Missing mods", additionalReason, 8f, GameNotify.Kind.Error, "disconnect");
            else if (!string.IsNullOrEmpty(additionalReason))
                GameNotify.ShowTitle("Disconnected", reason + " — " + additionalReason, 6f, GameNotify.Kind.Warning, "disconnect");
            else
                GameNotify.ShowTitle("Disconnected", reason.ToString(), 4f, GameNotify.Kind.Warning, "disconnect");
        }

        public static void MissingMods(string missingMods) {
            GameNotify.ShowTitle("Cannot join", "Missing mods:\n" + missingMods, 8f, GameNotify.Kind.Error, "missingmods");
        }

        public static void LobbyStarted() {
            GameNotify.ShowTitle("Lobby", "Lobby has been created!", 4f, GameNotify.Kind.Success, "lobby");
        }

        public static void JoinServer(string username) {
            GameNotify.ShowTitle("Joined", "Joined " + username + "'s game.", 4f, GameNotify.Kind.Success, "joinserver");
        }

        public static void LeftServer() {
            GameNotify.ShowTitle("Left", "You have left.", 4f, GameNotify.Kind.Info, "leftserver");
        }

        public static void InvalidSteam() {
            GameNotify.ShowTitle(
                "Steam",
                "Steam did not start. Launch through Steam. For Meta Horizon Link use Net: SteamVR in Settings.",
                6f,
                GameNotify.Kind.Error,
                "invalidsteam");
        }
    }
}
