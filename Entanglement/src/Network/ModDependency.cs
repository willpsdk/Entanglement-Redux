using System;
using System.Collections.Generic;
using System.Text;

using MelonLoader;

using Steamworks;

using Entanglement.Sync;

namespace Entanglement.Network
{
    public static class ModDependency
    {
        public const string LobbyDataKey = "required_mods";
        public const string ThunderstoreDataKey = "ts_mods";
        public const string ThunderstoreDataKey2 = "ts_mod2";
        public const int MaxLobbyValueLength = 240;

        static readonly List<ThunderstorePackage> lastMissingPackages = new List<ThunderstorePackage>();
        static readonly List<ThunderstorePackage> lastMissingContent = new List<ThunderstorePackage>();

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
                EntangleLogger.Warn("[ModDependency] Failed enumerating local mods: " + e.Message);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        static string ModAuthor(MelonMod mod) {
            try { return mod.Info.Author; }
            catch { return ""; }
        }

        public static void PublishHostRequirements() {
            if (!SteamIntegration.hasLobby || !SteamIntegration.isHost)
                return;

            List<string> mods = GetLocalContentMods();
            string packed = Pack(mods);
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, LobbyDataKey, packed);
            string ts1, ts2;
            PackThunderstoreKeys(out ts1, out ts2);
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, ThunderstoreDataKey, ts1);
            SteamMatchmaking.SetLobbyData(SteamIntegration.lobby, ThunderstoreDataKey2, ts2);
            EntangleLogger.Debug("[ModDependency] Published " + mods.Count + " required mod(s) and Thunderstore packages to lobby metadata");
        }

        static void PackThunderstoreKeys(out string first, out string second) {
            first = "";
            second = "";
            var packed = new List<string>();
            var seen = HashSetCreate();

            try {
                foreach (MelonMod mod in MelonHandler.Mods) {
                    if (mod == null)
                        continue;
                    string name = null;
                    try { name = mod.Info.Name; } catch { }
                    if (string.IsNullOrEmpty(name) || IgnoredMods.Contains(name))
                        continue;
                    ThunderstorePackage package;
                    if (!Thunderstore.TryMatchModName(name, ModAuthor(mod), out package)) {
                        EntangleLogger.Warn("[ModDependency] No Thunderstore package for '" + name + "'.");
                        continue;
                    }
                    AddPacked(packed, seen, package);
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn("[ModDependency] Thunderstore pack failed: " + e.Message);
            }

            string mapFile = CustomMapSync.CurrentMapFile;
            if (!string.IsNullOrEmpty(mapFile)) {
                ThunderstorePackage mapPackage;
                string full = System.IO.Path.Combine(Entanglement.Compat.CustomMaps.CustomMapsPatch.customMapsPath, mapFile);
                if (System.IO.File.Exists(full) && Thunderstore.TryMatchFile(full, out mapPackage))
                    AddPacked(packed, seen, mapPackage);
            }

            for (int i = 0; i < packed.Count; i++) {
                string piece = packed[i];
                if (first.Length == 0) {
                    first = piece;
                    continue;
                }
                if ((first.Length + 1 + piece.Length) <= MaxLobbyValueLength) {
                    first = first + ";" + piece;
                    continue;
                }
                if (second.Length == 0) {
                    second = piece;
                    continue;
                }
                if ((second.Length + 1 + piece.Length) <= MaxLobbyValueLength)
                    second = second + ";" + piece;
            }

            string packedLine = string.IsNullOrEmpty(second) ? first : first + ";" + second;
            if (packedLine != lastPackedLogged) {
                lastPackedLogged = packedLine;
                EntangleLogger.Debug("[ModDependency] Thunderstore packed: " + packedLine);
            }
        }

        // Lobby metadata is republished on all sorts of events; the packed list rarely changes.
        static string lastPackedLogged;

        static HashSet<string> HashSetCreate() {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        static void AddPacked(List<string> packed, HashSet<string> seen, ThunderstorePackage package) {
            if (!Thunderstore.IsToken(package.author) || !Thunderstore.IsToken(package.name) || !Thunderstore.IsToken(package.version))
                return;
            if (IgnoredMods.Contains(package.name) || IgnoredMods.Contains(package.displayName))
                return;
            string id = package.author + "/" + package.name;
            if (!seen.Add(id))
                return;
            packed.Add(package.author + "/" + package.name + "/" + package.version);
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
            lastMissingPackages.Clear();
            lastMissingContent.Clear();
            string packed = SteamMatchmaking.GetLobbyData(lobby, LobbyDataKey);
            List<string> required = Unpack(packed);

            List<string> missing = FindMissingMods(required);
            CollectThunderstore(
                SteamMatchmaking.GetLobbyData(lobby, ThunderstoreDataKey),
                SteamMatchmaking.GetLobbyData(lobby, ThunderstoreDataKey2),
                missing);

            if (missing.Count > 0)
                missingCsv = string.Join(", ", missing);
            return missing.Count == 0;
        }

        public static int MissingContentCount => lastMissingContent.Count;
        public static int MissingCodeCount => lastMissingPackages.Count;

        static void CollectThunderstore(string packed, string packed2, List<string> missingCode) {
            var packages = new List<ThunderstorePackage>();
            ParsePackedPackages(packed, packages);
            ParsePackedPackages(packed2, packages);

            for (int i = 0; i < packages.Count; i++) {
                ThunderstorePackage package = packages[i];
                if (Thunderstore.HasInstalled(package.author, package.name))
                    continue;

                bool code = false;
                for (int m = 0; m < missingCode.Count; m++) {
                    if (NamesLike(missingCode[m], package)) {
                        code = true;
                        break;
                    }
                }
                if (code)
                    lastMissingPackages.Add(package);
                else
                    lastMissingContent.Add(package);
            }

            for (int m = 0; m < missingCode.Count; m++) {
                bool have = false;
                for (int i = 0; i < lastMissingPackages.Count; i++) {
                    if (NamesLike(missingCode[m], lastMissingPackages[i])) {
                        have = true;
                        break;
                    }
                }
                if (have)
                    continue;
                ThunderstorePackage resolved;
                if (!Thunderstore.TryMatchModName(missingCode[m], out resolved))
                    continue;
                lastMissingPackages.Add(resolved);
            }
        }

        static bool NamesLike(string melonName, ThunderstorePackage package) {
            if (string.IsNullOrEmpty(melonName))
                return false;
            return string.Equals(melonName, package.displayName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(melonName, package.name, StringComparison.OrdinalIgnoreCase)
                || melonName.Replace(" ", "").Equals(package.name.Replace("_", ""), StringComparison.OrdinalIgnoreCase);
        }

        static void ParsePackedPackages(string packed, List<ThunderstorePackage> dest) {
            if (string.IsNullOrEmpty(packed))
                return;
            string[] entries = packed.Split(';');
            for (int i = 0; i < entries.Length; i++) {
                string raw = entries[i];
                string display = "";
                int bar = raw.IndexOf('|');
                if (bar >= 0) {
                    display = raw.Substring(bar + 1);
                    raw = raw.Substring(0, bar);
                }
                string[] parts = raw.Split('/');
                if (parts.Length != 3)
                    continue;
                if (!Thunderstore.IsToken(parts[0]) || !Thunderstore.IsToken(parts[1]) || !Thunderstore.IsToken(parts[2]))
                    continue;
                dest.Add(new ThunderstorePackage {
                    author = parts[0],
                    name = parts[1],
                    version = parts[2],
                    displayName = string.IsNullOrEmpty(display) ? parts[1] : display,
                });
            }
        }

        public static int DownloadMissingFromThunderstore() {
            int started = 0;
            for (int i = 0; i < lastMissingPackages.Count; i++) {
                Thunderstore.QueueMod(lastMissingPackages[i]);
                started++;
            }
            return started;
        }

        public static int OfferMissingContent() {
            int offered = 0;
            for (int i = 0; i < lastMissingContent.Count; i++) {
                ThunderstorePackage package = lastMissingContent[i];
                Thunderstore.OfferPackage(0, package, Thunderstore.GuessCategory(package), "", "", false);
                offered++;
            }
            return offered;
        }
    }
}
