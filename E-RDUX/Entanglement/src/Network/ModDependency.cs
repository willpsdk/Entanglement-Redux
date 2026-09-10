using System;
using System.Collections.Generic;
using System.Text;

using MelonLoader;

using Steamworks;

namespace Entanglement.Network
{
    /// <summary>
    /// Host publishes its MelonMod list into lobby metadata; joiners compare before connecting.
    /// Missing required mods are named in the UI rather than failing silently.
    /// </summary>
    public static class ModDependency
    {
        public const string LobbyDataKey = "required_mods";
        public const int MaxLobbyValueLength = 240;

        static readonly HashSet<string> IgnoredMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Entanglement",
            "EntanglementRedux",
            "Entanglement Redux",
            "ModThatIsNotMod",
            "ModThatIsNotMod.CustomMapLoader",
            "BoneLib",
            "MelonLoader",
            "Harmony",
            "0Harmony",
        };

        public static List<string> GetLocalContentMods() {
            var names = new List<string>();
            try {
                // BONEWORKS ships MelonLoader 0.4.x (MelonHandler.Mods). Newer loaders use RegisteredMelons.
                // MelonLoader 0.4.x (BONEWORKS) lists loaded mods on MelonHandler.Mods
                foreach (MelonMod mod in MelonHandler.Mods) {
                    if (mod == null) continue;
                    string name = null;
                    try { name = mod.Info.Name; } catch { }
                    if (string.IsNullOrEmpty(name)) continue;
                    if (IgnoredMods.Contains(name)) continue;
                    if (!names.Contains(name))
                        names.Add(name);
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[ModDependency] Failed enumerating local mods: {e.Message}");
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static void PublishHostRequirements() {
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                return;

            List<string> mods = GetLocalContentMods();
            string packed = Pack(mods);
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, LobbyDataKey, packed);
            EntangleLogger.Log($"[ModDependency] Published {mods.Count} required mod(s) to lobby metadata");
        }

        public static string Pack(List<string> mods) {
            if (mods == null || mods.Count == 0)
                return "";

            var sb = new StringBuilder();
            foreach (string mod in mods) {
                string piece = mod.Replace(';', ',').Trim();
                if (piece.Length == 0) continue;
                string candidate = sb.Length == 0 ? piece : (sb + ";" + piece);
                if (candidate.Length > MaxLobbyValueLength) {
                    EntangleLogger.Warn("[ModDependency] Lobby mod list truncated to fit Steam metadata limits");
                    break;
                }
                if (sb.Length > 0) sb.Append(';');
                sb.Append(piece);
            }
            return sb.ToString();
        }

        public static List<string> Unpack(string packed) {
            var list = new List<string>();
            if (string.IsNullOrEmpty(packed))
                return list;
            foreach (string part in packed.Split(';')) {
                string name = part.Trim();
                if (name.Length > 0 && !list.Contains(name))
                    list.Add(name);
            }
            return list;
        }

        public static List<string> FindMissingMods(IEnumerable<string> required) {
            HashSet<string> local = new HashSet<string>(GetLocalContentMods(), StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            foreach (string req in required) {
                if (string.IsNullOrEmpty(req) || IgnoredMods.Contains(req))
                    continue;
                if (!local.Contains(req))
                    missing.Add(req);
            }
            missing.Sort(StringComparer.OrdinalIgnoreCase);
            return missing;
        }

        public static bool TryValidateLobbyMods(CSteamID lobby, out string missingCsv) {
            missingCsv = "";
            string packed = SteamMatchmaking.GetLobbyData(lobby, LobbyDataKey);
            List<string> required = Unpack(packed);
            if (required.Count == 0)
                return true;

            List<string> missing = FindMissingMods(required);
            if (missing.Count == 0)
                return true;

            missingCsv = string.Join(", ", missing);
            return false;
        }
    }
}
