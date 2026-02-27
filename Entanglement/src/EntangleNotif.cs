using System;

using ModThatIsNotMod;

using Entanglement.Network;

namespace Entanglement
{
    public static class EntangleNotif
    {
        private static float lastDisconnectNotifTime = -10f;
        private const float DISCONNECT_NOTIF_COOLDOWN = 2f;

        public static void PlayerJoin(string username)
        {
            Notifications.SendNotification($"{username} has joined the server!", 4f);
        }

        public static void PlayerLeave(string username)
        {
            Notifications.SendNotification($"{username} has left the server!", 4f);
        }

        public static void PlayerDisconnect(DisconnectReason reason)
        {
            // Debounce to prevent notification spam
            if (UnityEngine.Time.time - lastDisconnectNotifTime < DISCONNECT_NOTIF_COOLDOWN)
                return;

            lastDisconnectNotifTime = UnityEngine.Time.time;
            Notifications.SendNotification($"You were disconnected for reason {reason}.", 4f);
        }

        public static void JoinServer(string username)
        {
            Notifications.SendNotification($"Joined {username}'s server!", 4f);
        }

        public static void LeftServer()
        {
            // Debounce to prevent notification spam
            if (UnityEngine.Time.time - lastDisconnectNotifTime < DISCONNECT_NOTIF_COOLDOWN)
                return;

            lastDisconnectNotifTime = UnityEngine.Time.time;
            Notifications.SendNotification("You left the server.", 4f);
        }

        // --- NEW NOTIFICATIONS FOR SERVER HOSTING ---
        public static void ServerStarted()
        {
            Notifications.SendNotification("Server started!", 4f);
        }

        public static void ServerStopped()
        {
            Notifications.SendNotification("Server stopped!", 4f);
        }
    }
}