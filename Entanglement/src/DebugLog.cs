using System;

using MelonLoader;

using Entanglement.Network;
using Entanglement.Objects;
using Entanglement.Representation;

namespace Entanglement
{
    public static class DebugLog
    {
        static MelonPreferences_Category category;
        static MelonPreferences_Entry<bool> saved;

        static bool Saved {
            get {
                try {
                    if (saved == null) {
                        category = MelonPreferences.CreateCategory("EntanglementDebug");
                        saved = category.CreateEntry("debugLog", false,
                            description: "Detailed Entanglement logging, the same as the Representation Test's, without the dummy");
                    }
                    return saved.Value;
                }
                catch { return false; }
            }
        }

        /// <summary>The switch itself (not the Representation Test).</summary>
        public static bool On => Saved;

        /// <summary>At start: the switch as it was left.</summary>
        public static void Initialize() {
            Apply();
            if (On)
                EntangleLogger.Log("[Debug] Debug log is on (Settings > Debug to turn it off).", ConsoleColor.DarkYellow);
        }

        public static void Toggle() {
            bool next = !Saved;
            try {
                saved.Value = next;
                MelonPreferences.Save();
            }
            catch (Exception e) { EntangleLogger.Warn("[Debug] Could not save the debug log setting: " + e.Message); }
            Apply();

            if (next) {
                EntangleLogger.Log("[Debug] Debug log on.", ConsoleColor.DarkYellow);
                EntangleLogger.Log("[Debug] " + Snapshot(), ConsoleColor.DarkYellow);
            }
            else
                EntangleLogger.Log("[Debug] Debug log off" + (PlayerRepresentation.RepresentationTestActive
                    ? " (the Representation Test keeps it on until it is stopped)." : "."), ConsoleColor.DarkYellow);
        }

        public static void Apply() {
            bool on = Saved || PlayerRepresentation.RepresentationTestActive;
            EntangleLogger.verbose = on;
            try { NpcTrace.enabled = on; } catch { }
            try { BossClawSync.traceEnabled = on; } catch { }
            try { SceneEventSync.traceEnabled = on; } catch { }
        }

        /// <summary>One line of where things stand, for the top of a debug log.</summary>
        public static string Snapshot() {
            try {
                string role = !SteamIntegration.hasLobby ? "no lobby" : (Node.isServer ? "host" : "client");
                string scene;
                try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
                catch { scene = "?"; }
                return "Entanglement " + EntanglementMod.VersionString + " (build " + EntanglementVersion.BuildTag + "), "
                    + role + ", " + PlayerRepresentation.representations.Count + " other player(s), scene " + scene
                    + ", slow-mo " + SlowMoSync.Mode + ", holstered items pinned " + HolsterSync.PinnedCount
                    + ", buttons held for others " + Patching.ButtonTogglePatch.RemoteHeldCount
                    + ", gun slides shown for others " + Patching.GunSlideSync.MirrorCount
                    + ", Discord " + (DiscordInvite.Connected ? "connected" : "not connected")
                    + (DiscordInvite.RivalApp != null ? " (" + DiscordInvite.RivalApp + " running)" : "") + ". " + NetStats.StateLine();
            }
            catch (Exception e) { return "snapshot unavailable: " + e.Message; }
        }
    }
}
