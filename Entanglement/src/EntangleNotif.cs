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
            try { Notifications.SendNotification($"{username} has joined the server!", 4f); } catch { }
        }

        public static void PlayerLeave(string username)
        {
            try { Notifications.SendNotification($"{username} has left the server!", 4f); } catch { }
        }

        public static void PlayerDisconnect(DisconnectReason reason)
        {
            // Debounce to prevent notification spam
            if (UnityEngine.Time.time - lastDisconnectNotifTime < DISCONNECT_NOTIF_COOLDOWN)
                return;

            lastDisconnectNotifTime = UnityEngine.Time.time;
            try { Notifications.SendNotification($"You were disconnected for reason {reason}.", 4f); } catch { }
        }

        public static void JoinServer(string username)
        {
            try { Notifications.SendNotification($"Joined {username}'s server!", 4f); } catch { }
        }

        public static void LeftServer()
        {
            // Debounce to prevent notification spam
            if (UnityEngine.Time.time - lastDisconnectNotifTime < DISCONNECT_NOTIF_COOLDOWN)
                return;

            lastDisconnectNotifTime = UnityEngine.Time.time;
            try { Notifications.SendNotification("You left the server.", 4f); } catch { }
        }

        // --- NEW NOTIFICATIONS FOR SERVER HOSTING ---
        public static void ServerStarted()
        {
            try { Notifications.SendNotification("Server started!", 4f); } catch { }
        }

        public static void ServerStopped()
        {
            try { Notifications.SendNotification("Server stopped!", 4f); } catch { }
        }
    }
}