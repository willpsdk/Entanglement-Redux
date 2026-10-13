using System;
using System.Text;

using MelonLoader;

using Entanglement.Entities;
using Entanglement.Gamemodes;
using Entanglement.Network;
using Entanglement.Network.Layers;
using Entanglement.Objects;
using Entanglement.Representation;
using Entanglement.UI;

namespace Entanglement
{
    /// <summary>
    /// Radial Settings → Debug. Master verbose switch plus channel dumps.
    /// Entanglement does not register a BoneMenu category — MTINM/BoneMenu is only
    /// used for Player Models / Custom Maps listing and notification fallback.
    /// </summary>
    public static class DebugLog
    {
        static MelonPreferences_Category category;
        static MelonPreferences_Entry<bool> saved;
        static MelonPreferences_Entry<bool> entitiesPref;
        static MelonPreferences_Entry<bool> puppetPref;
        static MelonPreferences_Entry<bool> combatPref;
        static MelonPreferences_Entry<bool> gamemodePref;
        static MelonPreferences_Entry<bool> transportPref;

        static void EnsurePrefs() {
            if (saved != null)
                return;
            category = MelonPreferences.CreateCategory("EntanglementDebug");
            saved = category.CreateEntry("debugLog", false,
                description: "Master verbose Entanglement logging (Latest.log)");
            entitiesPref = category.CreateEntry("debugEntities", true,
                description: "Log NetworkEntity / NetworkPlayer / NpcEntity / NetworkProp");
            puppetPref = category.CreateEntry("debugPuppet", true,
                description: "Log RigPuppet / PhysicsRigPuppet create-drive");
            combatPref = category.CreateEntry("debugCombat", true,
                description: "Log NetworkCombat / PlayerAttack");
            gamemodePref = category.CreateEntry("debugGamemode", true,
                description: "Log gamemode select/start/stop");
            transportPref = category.CreateEntry("debugTransport", true,
                description: "Log network layer / Steam AppID transport");
        }

        static bool Saved {
            get {
                try {
                    EnsurePrefs();
                    return saved.Value;
                }
                catch { return false; }
            }
        }

        public static bool On => Saved;

        public static bool Entities {
            get { try { EnsurePrefs(); return On && entitiesPref.Value; } catch { return false; } }
        }
        public static bool Puppet {
            get { try { EnsurePrefs(); return On && puppetPref.Value; } catch { return false; } }
        }
        public static bool Combat {
            get { try { EnsurePrefs(); return On && combatPref.Value; } catch { return false; } }
        }
        public static bool Gamemode {
            get { try { EnsurePrefs(); return On && gamemodePref.Value; } catch { return false; } }
        }
        public static bool Transport {
            get { try { EnsurePrefs(); return On && transportPref.Value; } catch { return false; } }
        }

        public static void Initialize() {
            Apply();
            if (On)
                EntangleLogger.Log("[Debug] Debug log is on (Settings > Debug to turn it off).", ConsoleColor.DarkYellow);
        }

        public static void Toggle() {
            EnsurePrefs();
            bool next = !Saved;
            try {
                saved.Value = next;
                MelonPreferences.Save();
            }
            catch (Exception e) { EntangleLogger.Warn("[Debug] Could not save the debug log setting: " + e.Message); }
            Apply();

            if (next) {
                EntangleLogger.Log("[Debug] Debug log on.", ConsoleColor.DarkYellow);
                DumpAll();
            }
            else
                EntangleLogger.Log("[Debug] Debug log off" + (PlayerRepresentation.RepresentationTestActive
                    ? " (the Representation Test keeps it on until it is stopped)." : "."), ConsoleColor.DarkYellow);
        }

        public static void ToggleChannel(string channel) {
            EnsurePrefs();
            MelonPreferences_Entry<bool> entry = Entry(channel);
            if (entry == null)
                return;
            try {
                entry.Value = !entry.Value;
                MelonPreferences.Save();
            }
            catch (Exception e) { EntangleLogger.Warn("[Debug] Could not save channel: " + e.Message); }
            EntangleLogger.Log("[Debug] " + channel + " → " + (entry.Value ? "on" : "off"), ConsoleColor.DarkYellow);
        }

        public static bool PrefEnabled(string channel) {
            EnsurePrefs();
            MelonPreferences_Entry<bool> entry = Entry(channel);
            try { return entry != null && entry.Value; }
            catch { return false; }
        }

        static MelonPreferences_Entry<bool> Entry(string channel) {
            if (channel == "entities") return entitiesPref;
            if (channel == "puppet") return puppetPref;
            if (channel == "combat") return combatPref;
            if (channel == "gamemode") return gamemodePref;
            if (channel == "transport") return transportPref;
            return null;
        }

        public static void Apply() {
            bool on = Saved || PlayerRepresentation.RepresentationTestActive;
            EntangleLogger.verbose = on;
            try { NpcTrace.enabled = on; } catch { }
            try { BossClawSync.traceEnabled = on; } catch { }
            try { SceneEventSync.traceEnabled = on; } catch { }
        }

        public static void Entity(string message) {
            if (Entities)
                EntangleLogger.Log("[Entity] " + message, ConsoleColor.DarkCyan);
        }

        public static void PuppetLog(string message) {
            if (Puppet)
                EntangleLogger.Log("[Puppet] " + message, ConsoleColor.DarkMagenta);
        }

        public static void CombatLog(string message) {
            if (Combat)
                EntangleLogger.Log("[Combat] " + message, ConsoleColor.DarkYellow);
        }

        public static void GamemodeLog(string message) {
            if (Gamemode)
                EntangleLogger.Log("[Gamemode] " + message, ConsoleColor.DarkGreen);
        }

        public static void TransportLog(string message) {
            if (Transport)
                EntangleLogger.Log("[Transport] " + message, ConsoleColor.Blue);
        }

        public static string Snapshot() {
            try {
                string role = !SteamIntegration.hasLobby ? "no lobby" : (Node.isServer ? "host" : "client");
                string scene;
                try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
                catch { scene = "?"; }
                return "Entanglement " + EntanglementMod.VersionString + " (build " + EntanglementVersion.BuildTag + "), "
                    + role + ", " + PlayerRepresentation.reps.Count + " other player(s), scene " + scene
                    + ", slow-mo " + SlowMoSync.Mode + ", holstered items pinned " + HolsterSync.PinnedCount
                    + ", buttons held for others " + Patching.ButtonTogglePatch.RemoteHeldCount
                    + ", gun slides shown for others " + Patching.GunSlideSync.MirrorCount
                    + ", Discord " + (DiscordInvite.Connected ? "connected" : "not connected")
                    + (DiscordInvite.RivalApp != null ? " (" + DiscordInvite.RivalApp + " running)" : "") + ". " + NetStats.StateLine();
            }
            catch (Exception e) { return "snapshot unavailable: " + e.Message; }
        }

        public static void DumpAll() {
            var sb = new StringBuilder();
            sb.AppendLine("======== Entanglement debug dump ========");
            sb.AppendLine(Snapshot());

            try {
                uint appId = 0;
                SteamNetworkLayer steam = NetworkLayerManager.Layer as SteamNetworkLayer;
                if (steam != null)
                    appId = steam.ApplicationID;
                sb.AppendLine("Transport: active=" + NetworkLayerManager.ActiveTitle
                    + " platform=" + NetworkLayerManager.ActivePlatform
                    + " preferred=" + NetworkPrefs.ResolvedLayerTitle()
                    + " appId=" + (appId == 0 ? "game/default" : appId.ToString()));
            }
            catch (Exception e) { sb.AppendLine("Transport: " + e.Message); }

            sb.AppendLine("BoneMenu: MTINM dependency present; Entanglement registers no BoneMenu category.");

            try {
                string mode = GamemodeManager.Active != null
                    ? GamemodeManager.Active.Title + (GamemodeManager.Active.IsStarted ? " live" : " selected")
                    : "Sandbox";
                sb.AppendLine("Gamemode: " + mode + " (" + GamemodeManager.Gamemodes.Count + " registered)");
            }
            catch (Exception e) { sb.AppendLine("Gamemode: " + e.Message); }

            try {
                sb.AppendLine("Entities: NetworkEntity=" + NetworkEntityManager.Count
                    + " NetworkPlayer=" + NetworkPlayer.Count
                    + " NpcEntity=" + NpcEntity.Count
                    + " NetworkProp=" + NetworkProp.Count
                    + " syncedObjects=" + ObjectSync.syncedObjects.Count);
            }
            catch (Exception e) { sb.AppendLine("Entities: " + e.Message); }

            try {
                int puppets = 0;
                int valid = 0;
                foreach (var pair in PlayerRepresentation.Remotes()) {
                    PlayerRepresentation rep = pair.Value;
                    if (rep == null)
                        continue;
                    if (rep.PhysicsPuppet != null) {
                        puppets++;
                        if (rep.PhysicsPuppet.IsValid)
                            valid++;
                    }
                    sb.AppendLine("  Rep " + pair.Key
                        + " puppet=" + (rep.PhysicsPuppet != null && rep.PhysicsPuppet.IsValid ? "ok" : "missing")
                        + " enemyTarget=" + (rep.EnemyProxy != null)
                        + " netPlayer=" + NetworkPlayer.IsRegistered(pair.Key));
                }
                sb.AppendLine("Puppets: " + valid + "/" + puppets + " valid");
            }
            catch (Exception e) { sb.AppendLine("Puppets: " + e.Message); }

            sb.AppendLine("Channels: entities=" + PrefEnabled("entities")
                + " puppet=" + PrefEnabled("puppet")
                + " combat=" + PrefEnabled("combat")
                + " gamemode=" + PrefEnabled("gamemode")
                + " transport=" + PrefEnabled("transport")
                + " master=" + On);
            sb.AppendLine("========================================");
            EntangleLogger.Log(sb.ToString(), ConsoleColor.DarkYellow);
            try { GameNotify.Show("Debug dump written to MelonLoader/Latest.log.", 4f, GameNotify.Kind.Info); }
            catch { }
        }
    }
}
