using System;

using ModThatIsNotMod;

using Entanglement.Network;

namespace Entanglement
{
    public static class EntangleNotif {
        public static void PlayerJoin(string username) {
            Notifications.SendNotification($"{username} has joined the server!", 4f);
        }

        public static void PlayerLeave(string username) {
            Notifications.SendNotification($"{username} has left the server!", 4f);
        }

        public static void PlayerDisconnect(DisconnectReason reason) {
            Notifications.SendNotification($"You were disconnected for reason {reason}.", 4f);
        }

        public static void JoinServer(string username) {
            Notifications.SendNotification($"Joined {username}'s server!", 4f);
        }

        public static void LeftServer() {
            Notifications.SendNotification("You left the server.", 4f);
        }

        public static void InvalidSteam() {
            Notifications.SendNotification("Failed to initialize the Steam API! Continuing without Entanglement!\nMake sure Steam is running, you are logged in, and the game was launched through Steam.", 4f);
        }

        public static void GamemodeStarted(string modeName) {
            Notifications.SendNotification($"{modeName} has started!", 4f);
        }

        public static void GamemodeEnded(string modeName) {
            Notifications.SendNotification($"{modeName} round over!", 4f);
        }
    }
}
