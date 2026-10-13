using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;

using MelonLoader;

namespace Entanglement.Sync
{
    public static class ThunderstoreIndex
    {
        const string CatalogUrl = "https://thunderstore.io/c/boneworks/api/v1/package/";
        const double CatalogMaxAgeHours = 24;

        static readonly object gate = new object();

        class FileEntry {
            public ThunderstorePackage package;
            public long size;
        }

        // File name (no folders) -> every package that has a file of that name.
        static readonly Dictionary<string, List<FileEntry>> files = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
        static bool localBuilt;

        // "author/name" -> latest version, from Thunderstore.
        static readonly Dictionary<string, string> online = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<ThunderstorePackage> onlineList = new List<ThunderstorePackage>();

        enum OnlineState { NotStarted, Loading, Ready, Failed }
        static OnlineState onlineState = OnlineState.NotStarted;

        public static bool OnlineReady { get { lock (gate) return onlineState == OnlineState.Ready; } }
        public static bool OnlineLoading { get { lock (gate) return onlineState == OnlineState.Loading || onlineState == OnlineState.NotStarted; } }

        /// <summary>Builds both indexes in the background. Safe to call often.</summary>
        public static void Warmup() {
            lock (gate) {
                if (onlineState != OnlineState.NotStarted)
                    return;
                onlineState = OnlineState.Loading;
            }
            Thread thread = new Thread(() => {
                try { EnsureLocal(); }
                catch (Exception e) { EntangleLogger.Warn("[Thunderstore] Could not index installed packages: " + e.Message); }
                LoadOnline();
            }) {
                IsBackground = true,
                Name = "EntanglementThunderstoreIndex"
            };
            thread.Start();
        }

        // ---- Installed files ------------------------------------------------------------------------

        /// <summary>
        /// Where r2modman and the Thunderstore Mod Manager keep BONEWORKS. Each folder once: Windows
        /// paths ignore case, so "Boneworks" and "BONEWORKS" are the same folder.
        /// </summary>
        public static readonly string[] ManagerGameFolders = BuildManagerGameFolders();

        static string[] BuildManagerGameFolders() {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] managers = {
                Path.Combine(app, "r2modmanPlus-local"),
                Path.Combine(app, "r2modmanPlus"),
                Path.Combine(app, "Thunderstore Mod Manager", "DataFolder"),
            };
            List<string> folders = new List<string>();
            for (int m = 0; m < managers.Length; m++)
                folders.Add(Path.Combine(managers[m], "Boneworks"));
            return folders.ToArray();
        }

        static void EnsureLocal() {
            lock (gate) {
                if (localBuilt)
                    return;
                localBuilt = true;
            }

            int before = FileCount();
            for (int m = 0; m < ManagerGameFolders.Length; m++) {
                string root = ManagerGameFolders[m];
                if (!Directory.Exists(root))
                    continue;
                IndexCache(Path.Combine(root, "cache"));
                IndexStates(Path.Combine(root, "profiles"));
            }

            try {
                string game = Directory.GetParent(MelonUtils.UserDataDirectory).FullName;
                IndexManifests(Path.Combine(game, "Mods"));
                IndexManifests(MelonUtils.UserDataDirectory);
            }
            catch { }

            EntangleLogger.Log("[Thunderstore] Indexed " + (FileCount() - before) + " file name(s) from installed packages.");
        }

        static int FileCount() {
            lock (gate) return files.Count;
        }

        // cache/Author-Package/1.2.3/<the package, unpacked>
        static void IndexCache(string cache) {
            if (!Directory.Exists(cache))
                return;
            string[] packages;
            try { packages = Directory.GetDirectories(cache); }
            catch { return; }
            for (int p = 0; p < packages.Length; p++) {
                string author, name;
                if (!SplitFullName(Path.GetFileName(packages[p]), out author, out name))
                    continue;
                string[] versions;
                try { versions = Directory.GetDirectories(packages[p]); }
                catch { continue; }
                for (int v = 0; v < versions.Length; v++) {
                    string version = Path.GetFileName(versions[v]);
                    if (!IsVersion(version))
                        continue;
                    ThunderstorePackage package = new ThunderstorePackage { author = author, name = name, version = version, displayName = name };
                    IndexFolder(versions[v], package);
                }
            }
        }

        // profiles/<profile>/_state/Author-Package-state.yml: what that package put where.
        static void IndexStates(string profiles) {
            if (!Directory.Exists(profiles))
                return;
            string[] states;
            try { states = Directory.GetFiles(profiles, "*-state.yml", SearchOption.AllDirectories); }
            catch { return; }
            for (int s = 0; s < states.Length; s++) {
                string stem = Path.GetFileName(states[s]);
                stem = stem.Substring(0, stem.Length - "-state.yml".Length);
                string author, name;
                if (!SplitFullName(stem, out author, out name))
                    continue;
                ThunderstorePackage package = new ThunderstorePackage { author = author, name = name, version = null, displayName = name };

                string[] lines;
                try { lines = File.ReadAllLines(states[s]); }
                catch { continue; }
                for (int i = 0; i < lines.Length; i++) {
                    int colon = lines[i].IndexOf(':');
                    if (colon < 0)
                        continue;
                    string value = lines[i].Substring(colon + 1).Trim().Trim('\'', '"');
                    if (value.IndexOf('/') < 0 && value.IndexOf('\\') < 0)
                        continue;
                    string fileName;
                    try { fileName = Path.GetFileName(value.Replace('/', Path.DirectorySeparatorChar)); }
                    catch { continue; }
                    if (fileName.IndexOf('.') > 0)
                        Add(fileName, package, -1);
                }
            }
        }

        static void IndexManifests(string root) {
            if (!Directory.Exists(root))
                return;
            string[] manifests;
            try { manifests = Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories); }
            catch { return; }
            for (int i = 0; i < manifests.Length; i++) {
                try {
                    string folder = Path.GetDirectoryName(manifests[i]);
                    string json = File.ReadAllText(manifests[i]);
                    string name = ReadJsonString(json, "name");
                    string version = ReadJsonString(json, "version_number");
                    string folderName = Path.GetFileName(folder);
                    string author = null;
                    if (!string.IsNullOrEmpty(name) && folderName.EndsWith("-" + name, StringComparison.OrdinalIgnoreCase))
                        author = folderName.Substring(0, folderName.Length - name.Length - 1);
                    if (!Thunderstore.IsToken(author) || !Thunderstore.IsToken(name) || !IsVersion(version))
                        continue;
                    IndexFolder(folder, new ThunderstorePackage { author = author, name = name, version = version, displayName = name });
                }
                catch { }
            }
        }

        static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "manifest.json", "icon.png", "readme.md", "changelog.md", "license", "license.txt", "license.md",
        };

        static void IndexFolder(string folder, ThunderstorePackage package) {
            string[] all;
            try { all = Directory.GetFiles(folder, "*", SearchOption.AllDirectories); }
            catch { return; }
            for (int f = 0; f < all.Length; f++) {
                string fileName = Path.GetFileName(all[f]);
                if (string.IsNullOrEmpty(fileName) || Skipped.Contains(fileName))
                    continue;
                long size = -1;
                try { size = new FileInfo(all[f]).Length; }
                catch { }
                Add(fileName, package, size);
            }
        }

        static void Add(string fileName, ThunderstorePackage package, long size) {
            lock (gate) {
                List<FileEntry> list;
                if (!files.TryGetValue(fileName, out list)) {
                    list = new List<FileEntry>();
                    files[fileName] = list;
                }
                for (int i = 0; i < list.Count; i++) {
                    ThunderstorePackage have = list[i].package;
                    if (string.Equals(have.author, package.author, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(have.name, package.name, StringComparison.OrdinalIgnoreCase)) {
                        // The same package again: keep the newest version we know, and a size.
                        if (IsVersion(package.version) && (!IsVersion(have.version) || CompareVersions(package.version, have.version) > 0)) {
                            list[i].package = package;
                            if (size >= 0)
                                list[i].size = size;
                        }
                        else if (list[i].size < 0 && size >= 0)
                            list[i].size = size;
                        return;
                    }
                }
                list.Add(new FileEntry { package = package, size = size });
            }
        }

        public static bool TryFile(string filePath, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            if (string.IsNullOrEmpty(filePath))
                return false;
            try { EnsureLocal(); }
            catch { }

            string fileName = Path.GetFileName(filePath);
            long size = -1;
            try { size = new FileInfo(filePath).Length; }
            catch { }

            lock (gate) {
                List<FileEntry> list;
                if (!files.TryGetValue(fileName, out list) || list.Count == 0)
                    return false;
                if (list.Count == 1) {
                    package = list[0].package;
                    return true;
                }
                int hits = 0;
                for (int i = 0; i < list.Count; i++) {
                    if (size >= 0 && list[i].size == size) {
                        package = list[i].package;
                        hits++;
                    }
                }
                if (hits == 1)
                    return true;
                package = new ThunderstorePackage();
                EntangleLogger.Warn("[Thunderstore] " + list.Count + " packages have a file named " + fileName + "; can't tell which this one is.");
                return false;
            }
        }

        // ---- Thunderstore's list ----------------------------------------------------------------

        static string CatalogPath() {
            return Path.Combine(MelonUtils.UserDataDirectory, "Entanglement", "ts_catalog.txt");
        }

        static readonly Regex VersionName = new Regex("\"full_name\"\\s*:\\s*\"([A-Za-z0-9_]+)-([A-Za-z0-9_]+)-([0-9]+\\.[0-9]+\\.[0-9]+)\"", RegexOptions.Compiled);

        static void LoadOnline() {
            bool fromDisk = false;
            try {
                string path = CatalogPath();
                if (File.Exists(path) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalHours < CatalogMaxAgeHours)
                    fromDisk = ReadCatalogFile(path);
            }
            catch { }

            if (!fromDisk) {
                try {
                    string json = Fetch();
                    int count = ParseCatalog(json);
                    if (count > 0) {
                        WriteCatalogFile();
                        EntangleLogger.Log("[Thunderstore] Thunderstore lists " + count + " BONEWORKS package(s).");
                    }
                }
                catch (Exception e) {
                    EntangleLogger.Warn("[Thunderstore] Could not get the package list from Thunderstore: " + e.Message);
                    // An old copy is better than none.
                    try {
                        if (File.Exists(CatalogPath()))
                            fromDisk = ReadCatalogFile(CatalogPath());
                    }
                    catch { }
                }
            }

            lock (gate)
                onlineState = online.Count > 0 ? OnlineState.Ready : OnlineState.Failed;
        }

        static string Fetch() {
            HttpClientHandler handler = new HttpClientHandler() {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            };
            using (HttpClient client = new HttpClient(handler)) {
                client.Timeout = TimeSpan.FromSeconds(90);
                try { client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EntanglementRedux/2.1.0"); }
                catch { }
                return client.GetStringAsync(CatalogUrl).GetAwaiter().GetResult();
            }
        }

        static int ParseCatalog(string json) {
            if (string.IsNullOrEmpty(json))
                return 0;
            int added = 0;
            lock (gate) {
                foreach (Match match in VersionName.Matches(json)) {
                    string key = match.Groups[1].Value + "/" + match.Groups[2].Value;
                    if (online.ContainsKey(key))
                        continue;
                    online[key] = match.Groups[3].Value;
                    onlineList.Add(new ThunderstorePackage {
                        author = match.Groups[1].Value,
                        name = match.Groups[2].Value,
                        version = match.Groups[3].Value,
                        displayName = match.Groups[2].Value,
                    });
                    added++;
                }
            }
            return added;
        }

        static bool ReadCatalogFile(string path) {
            string[] lines = File.ReadAllLines(path);
            int added = 0;
            lock (gate) {
                for (int i = 0; i < lines.Length; i++) {
                    string[] parts = lines[i].Trim().Split('/');
                    if (parts.Length != 3 || !Thunderstore.IsToken(parts[0]) || !Thunderstore.IsToken(parts[1]) || !IsVersion(parts[2]))
                        continue;
                    string key = parts[0] + "/" + parts[1];
                    if (online.ContainsKey(key))
                        continue;
                    online[key] = parts[2];
                    onlineList.Add(new ThunderstorePackage { author = parts[0], name = parts[1], version = parts[2], displayName = parts[1] });
                    added++;
                }
            }
            return added > 0;
        }

        static void WriteCatalogFile() {
            try {
                List<string> lines = new List<string>();
                lock (gate) {
                    for (int i = 0; i < onlineList.Count; i++)
                        lines.Add(onlineList[i].author + "/" + onlineList[i].name + "/" + onlineList[i].version);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath()));
                File.WriteAllLines(CatalogPath(), lines.ToArray());
            }
            catch { }
        }

        public static bool TryKnowsPackage(string author, string name, out bool exists, out string latest) {
            exists = false;
            latest = null;
            lock (gate) {
                if (onlineState != OnlineState.Ready)
                    return false;
                exists = online.TryGetValue(author + "/" + name, out latest);
                return true;
            }
        }

        // Shorter than this, one name inside another means nothing ("g" is in most names).
        const int MinNearName = 6;

        /// <summary>
        /// The one Thunderstore package a mod or file name stands for. Names can be shared (two
        /// authors both published a "Short Sleeve Ford"): the author, then a version in the name
        /// ("short sleeve ford v5.1"), tell them apart. A package whose name only holds this one, or
        /// the other way round, counts only when the author or that version agrees as well. Anything
        /// still unclear is no match: a guess would download someone else's mod.
        /// </summary>
        public static bool TryName(string title, string author, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            string folded = Fold(title);
            string trimmed = Fold(TrimVersion(title));
            string version = VersionIn(title);
            if (folded.Length < 3)
                return false;
            lock (gate) {
                if (onlineState != OnlineState.Ready)
                    return false;
                List<ThunderstorePackage> exact = new List<ThunderstorePackage>();
                List<ThunderstorePackage> near = new List<ThunderstorePackage>();
                for (int i = 0; i < onlineList.Count; i++) {
                    string name = Fold(onlineList[i].name);
                    if (name == folded || name == trimmed)
                        exact.Add(onlineList[i]);
                    else if (trimmed.Length >= MinNearName && name.Length >= MinNearName && (name.Contains(trimmed) || trimmed.Contains(name)))
                        near.Add(onlineList[i]);
                }
                if (exact.Count == 1) {
                    package = exact[0];
                    return true;
                }
                if (exact.Count > 1)
                    return Settle(exact, author, version, out package);
                return Settle(near, author, version, out package);
            }
        }

        // The one candidate the author names, else the one the version names.
        static bool Settle(List<ThunderstorePackage> candidates, string author, string version, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            if (candidates.Count == 0)
                return false;
            string foldedAuthor = Fold(author);
            if (foldedAuthor.Length > 0 && Single(candidates, c => Fold(c.author) == foldedAuthor, out package))
                return true;
            if (!string.IsNullOrEmpty(version) && Single(candidates, c => SameVersion(c.version, version), out package))
                return true;
            package = new ThunderstorePackage();
            return false;
        }

        static bool Single(List<ThunderstorePackage> candidates, Func<ThunderstorePackage, bool> test, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            int found = 0;
            for (int i = 0; i < candidates.Count; i++) {
                if (!test(candidates[i]))
                    continue;
                package = candidates[i];
                found++;
            }
            return found == 1;
        }

        // "5.1" is "5.1.0" and "5.1.2"; "5.1.0" is only "5.1.0".
        static bool SameVersion(string listed, string named) {
            if (string.IsNullOrEmpty(listed))
                return false;
            return listed == named || listed.StartsWith(named + ".");
        }

        // The version at the end of a name, without its "v": "Rifle Pack v2.1" -> "2.1".
        static readonly Regex VersionOnly = new Regex("[vV]?([0-9]+(\\.[0-9]+)*)$", RegexOptions.Compiled);

        static string VersionIn(string title) {
            if (string.IsNullOrEmpty(title))
                return null;
            Match match = VersionOnly.Match(title.Trim());
            return match.Success ? match.Groups[1].Value : null;
        }

        // "Rifle Pack v2.1", "RiflePack_1.0.3" -> "Rifle Pack", "RiflePack".
        static readonly Regex VersionTail = new Regex("[ _\\-.]*[vV]?[0-9]+(\\.[0-9]+)*$", RegexOptions.Compiled);

        static string TrimVersion(string title) {
            if (string.IsNullOrEmpty(title))
                return title;
            return VersionTail.Replace(title, "");
        }

        // ---- Helpers ------------------------------------------------------------------------------

        static string Fold(string value) {
            if (string.IsNullOrEmpty(value))
                return "";
            char[] chars = new char[value.Length];
            int n = 0;
            for (int i = 0; i < value.Length; i++) {
                char c = char.ToLowerInvariant(value[i]);
                if (c == '_' || c == '-' || c == ' ' || c == '.')
                    continue;
                chars[n++] = c;
            }
            return new string(chars, 0, n);
        }

        // Thunderstore names are letters, digits and underscores, so the first dash splits them.
        static bool SplitFullName(string fullName, out string author, out string name) {
            author = null;
            name = null;
            if (string.IsNullOrEmpty(fullName))
                return false;
            int dash = fullName.IndexOf('-');
            if (dash <= 0 || dash >= fullName.Length - 1)
                return false;
            author = fullName.Substring(0, dash);
            name = fullName.Substring(dash + 1);
            return Thunderstore.IsToken(author) && Thunderstore.IsToken(name) && name.IndexOf('-') < 0;
        }

        static bool IsVersion(string value) {
            if (string.IsNullOrEmpty(value))
                return false;
            string[] parts = value.Split('.');
            if (parts.Length < 2 || parts.Length > 4)
                return false;
            for (int i = 0; i < parts.Length; i++) {
                int n;
                if (!int.TryParse(parts[i], out n) || n < 0)
                    return false;
            }
            return true;
        }

        static int CompareVersions(string a, string b) {
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int length = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < length; i++) {
                int x = 0, y = 0;
                if (i < pa.Length) int.TryParse(pa[i], out x);
                if (i < pb.Length) int.TryParse(pb[i], out y);
                if (x != y)
                    return x.CompareTo(y);
            }
            return 0;
        }

        static string ReadJsonString(string json, string key) {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
