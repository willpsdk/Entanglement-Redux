using System;

using ModThatIsNotMod;

using Entanglement.Network;

namespace Entanglement
{
    public static class EntangleNotif {
        public static void PlayerJoin(string username) {
            Notifications.SendNotification($"{username} has joined!", 4f);
        }

        public static void PlayerLeave(string username) {
            Notifications.SendNotification($"{username} left.", 4f);
        }

        public static void PlayerDisconnect(DisconnectReason reason, string additionalReason = "") {
            if (reason == DisconnectReason.MissingMods && !string.IsNullOrEmpty(additionalReason))
                Notifications.SendNotification($"You are missing mods:\n{additionalReason}", 8f);
            else if (!string.IsNullOrEmpty(additionalReason))
                Notifications.SendNotification($"Disconnected: {reason}\n{additionalReason}", 6f);
            else
                Notifications.SendNotification($"Disconnected: {reason}", 4f);
        }

        public static void MissingMods(string missingMods) {
            Notifications.SendNotification($"Cannot join, you are missing mods:\n{missingMods}", 8f);
        }

        public static void LobbyStarted() {
            Notifications.SendNotification("Lobby has been created!", 4f);
        }

        public static void JoinServer(string username) {
            Notifications.SendNotification($"Joined {username}'s game.", 4f);
        }

        public static void LeftServer() {
            Notifications.SendNotification("You have left", 4f);
        }

        public static void InvalidSteam() {
            Notifications.SendNotification("Steam did not start up, so Entanglement is off.\nCheck Steam is running and launch the game through it.", 4f);
        }
    }
}
