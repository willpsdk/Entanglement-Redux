using MelonLoader;

namespace Entanglement.Sync
{
    public static class SyncPrefs
    {
        static readonly MelonPreferences_Category category = MelonPreferences.CreateCategory("EntanglementFileSync");

        public static readonly MelonPreferences_Entry<bool> itemSyncEnabled =
            category.CreateEntry("itemSyncEnabled", true, description: "Automatically send/receive custom spawned items you don't have installed");

        public static readonly MelonPreferences_Entry<bool> playermodelSyncEnabled =
            category.CreateEntry("playermodelSyncEnabled", true, description: "Automatically send/receive playermodels other players are wearing");

        public static readonly MelonPreferences_Entry<bool> mapSyncEnabled =
            category.CreateEntry("mapSyncEnabled", true, description: "Automatically send/receive custom map files when the host loads a map you don't have");

        public static readonly MelonPreferences_Entry<bool> requireDownloadConsent =
            category.CreateEntry("requireDownloadConsent", true, description: "Ask before downloading synced files (custom maps always ask). Accept/Decline from Circle → Downloads");

        public static readonly MelonPreferences_Entry<bool> allowPeerFiles =
            category.CreateEntry("allowPeerFiles", false, description: "UNSAFE. Let other players send you files directly. Leave this off so maps, models, and mods only download from Thunderstore.");

        public static readonly MelonPreferences_Entry<int> maxSyncSizeKB =
            category.CreateEntry("maxSyncSizeKB", 100 * 1024, description: "Refuse to send or receive a single item/model file larger than this many KB (default 100MB)");

        public static readonly MelonPreferences_Entry<string[]> blacklistedPaths =
            category.CreateEntry("blacklistedPaths", new string[0], description: "Melon/model file paths that will never be sent to other players, even if requested");

        public static readonly MelonPreferences_Entry<long[]> blockedUsers =
            category.CreateEntry("blockedUsers", new long[0], description: "Steam ids to never send files to or accept files from");

        public static readonly MelonPreferences_Entry<long[]> trustedUsers =
            category.CreateEntry("trustedUsers", new long[0], description: "Steam ids whose downloads skip the Accept/Decline prompt");

        static SyncPrefs() {
            category.SaveToFile(false);
        }

        public static bool IsUserBlocked(long userId) {
            long[] blocked = blockedUsers.Value;
            for (int i = 0; i < blocked.Length; i++)
                if (blocked[i] == userId) return true;
            return false;
        }

        public static bool IsUserTrusted(long userId) {
            if (IsUserBlocked(userId))
                return false;

            long[] trusted = trustedUsers.Value;
            for (int i = 0; i < trusted.Length; i++)
                if (trusted[i] == userId) return true;
            return false;
        }

        public static bool IsPathBlacklisted(string path) {
            string[] paths = blacklistedPaths.Value;
            for (int i = 0; i < paths.Length; i++)
                if (paths[i] == path) return true;
            return false;
        }
    }
}
