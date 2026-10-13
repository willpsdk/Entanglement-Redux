using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;

using MelonLoader;

using UnityEngine;

using Entanglement.Network;
using Entanglement.UI;

namespace Entanglement.Sync
{
    public struct ThunderstorePackage {
        public string author;
        public string name;
        public string version;
        public string displayName;
    }

    public static class Thunderstore
    {
        const long MaxZipBytes = 4L * 1024 * 1024 * 1024;
        const int DownloadTimeoutMinutes = 180;
        const string ApiRoot = "https://thunderstore.io/api/experimental/package/";

        static readonly object gate = new object();
        static string notice;
        static bool busy;
        static float visibleUntil;
        static string activeName = "";
        static long received;
        static long total;

        struct Done {
            public FileTransferCategory category;
            public string fileName;
            public string note;
            public bool installMod;
            public bool apply;
            public string label;
        }

        static readonly List<Done> finished = new List<Done>();
        static readonly HashSet<string> queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<FileTransferCategory, Action<string, string>> installers = new Dictionary<FileTransferCategory, Action<string, string>>();
        static List<ThunderstorePackage> catalog;
        static bool catalogLoaded;

        public struct PendingOffer {
            public string key;
            public long sender;
            public ThunderstorePackage package;
            public FileTransferCategory category;
            public string fileName;
            public string note;
            public string label;
        }

        static readonly List<PendingOffer> pendingOffers = new List<PendingOffer>();

        public static bool IsBusy {
            get { lock (gate) return busy || Time.unscaledTime < visibleUntil; }
        }

        public static bool HasPending {
            get { lock (gate) return pendingOffers.Count > 0; }
        }

        public static List<PendingOffer> GetPending() {
            lock (gate) return new List<PendingOffer>(pendingOffers);
        }

        public static void RegisterInstaller(FileTransferCategory category, Action<string, string> onInstalled) {
            installers[category] = onInstalled;
        }

        // A download waiting on the player said so once, for eight seconds, in the subtitles.
        const float ReminderInterval = 45f;
        static float nextReminder;

        static bool indexStarted;

        struct DeferredOffer {
            public long peer;
            public string filePath;
            public FileTransferCategory category;
            public string note;
            public Action notOffered;
            public float since;
        }

        // Files not matched yet while Thunderstore's package list was still on its way.
        static readonly List<DeferredOffer> deferredOffers = new List<DeferredOffer>();
        const float DeferredOfferMax = 120f;

        public static void Tick() {
            if (!indexStarted) {
                indexStarted = true;
                ThunderstoreIndex.Warmup();
            }
            RetryDeferredOffers();

            string text;
            Done[] ready = null;
            int waiting;
            lock (gate) {
                text = notice;
                notice = null;
                if (finished.Count > 0) {
                    ready = finished.ToArray();
                    finished.Clear();
                }
                waiting = pendingOffers.Count;
            }
            if (!string.IsNullOrEmpty(text)) {
                GameNotify.Show(text, 8f);
                nextReminder = Time.unscaledTime + ReminderInterval;
            }
            else if (waiting > 0 && Time.unscaledTime >= nextReminder) {
                nextReminder = Time.unscaledTime + ReminderInterval;
                GameNotify.Show(waiting == 1
                    ? "A download is waiting for you. Open the circle menu to accept it."
                    : waiting + " downloads are waiting for you. Open the circle menu to accept them.", 6f);
            }
            if (ready == null)
                return;

            for (int i = 0; i < ready.Length; i++) {
                if (ready[i].installMod) {
                    GameNotify.Show(ready[i].label + " is a code mod, so restart Boneworks to load it.", 8f);
                    continue;
                }
                if (!ready[i].apply)
                    continue;
                if (!string.IsNullOrEmpty(ready[i].fileName))
                    GameNotify.Show(ready[i].fileName + " is ready to use.", 5f);
                Action<string, string> installer;
                if (installers.TryGetValue(ready[i].category, out installer) && installer != null)
                    installer(ready[i].fileName, ready[i].note);
            }
        }

        public static bool TryGetProgress(out string name, out float doneMb, out float totalMb, out float progress) {
            lock (gate) {
                name = activeName;
                doneMb = received / 1024f / 1024f;
                totalMb = total > 0 ? total / 1024f / 1024f : 0f;
                progress = total > 0 ? (float)received / total : 0.05f;
                return busy;
            }
        }

        public static void QueueMod(ThunderstorePackage package) {
            Queue(package, FileTransferCategory.Custom1, "", "", true);
        }

        public static void QueueLatestMod(string author, string name) {
            if (!IsToken(author) || !IsToken(name))
                return;

            Thread thread = new Thread(() => {
                try {
                    string version = LookupLatestVersion(author, name);
                    if (!IsToken(version))
                        throw new InvalidOperationException("no version listed");
                    Queue(new ThunderstorePackage {
                        author = author,
                        name = name,
                        version = version,
                        displayName = name,
                    }, FileTransferCategory.Custom1, "", "", true);
                }
                catch (Exception e) {
                    EntangleLogger.Warn("[Thunderstore] Could not look up " + author + "/" + name + ": " + e.Message);
                    SetNotice("Thunderstore does not have " + author + "/" + name + ".");
                }
            }) {
                IsBackground = true,
                Name = "EntanglementThunderstoreLookup"
            };
            thread.Start();
        }

        public static void InstallCustomContentDependencies() {
            lock (gate) {
                busy = true;
                visibleUntil = Time.unscaledTime + 8f;
                activeName = "FieldInjector";
                received = 0;
                total = 0;
            }
            SetNotice("Installing Custom Content Dependencies");
            QueueLatestMod("WNP78", "FieldInjection");
            QueueLatestMod("Maranara", "Custom_Maps");
            QueueLatestMod("Maranara", "Player_Models");
        }

        public static bool TryOfferFile(long peer, string filePath, FileTransferCategory category, string note, Action notOffered = null) {
            ThunderstorePackage package;
            if (!TryMatchFile(filePath, out package)) {
                string fileName = Path.GetFileName(filePath);
                if (notOffered != null && ThunderstoreIndex.OnlineLoading) {
                    lock (gate) {
                        deferredOffers.Add(new DeferredOffer {
                            peer = peer, filePath = filePath, category = category, note = note,
                            notOffered = notOffered, since = Time.realtimeSinceStartup,
                        });
                    }
                    EntangleLogger.Log("[Thunderstore] " + fileName + " isn't matched to a package yet; trying again when Thunderstore's list is in.");
                    return true;
                }
                EntangleLogger.Warn("[Thunderstore] Not sending " + fileName + ". It is not a published Thunderstore package, so it will not be sent as a raw file.");
                return false;
            }
            return SendOffer(peer, filePath, package, category, note);
        }

        static void RetryDeferredOffers() {
            DeferredOffer[] due;
            lock (gate) {
                if (deferredOffers.Count == 0)
                    return;
                bool timedOut = Time.realtimeSinceStartup - deferredOffers[0].since > DeferredOfferMax;
                if (ThunderstoreIndex.OnlineLoading && !timedOut)
                    return;
                due = deferredOffers.ToArray();
                deferredOffers.Clear();
            }
            for (int i = 0; i < due.Length; i++) {
                ThunderstorePackage package;
                bool sent = false;
                try {
                    if (TryMatchFile(due[i].filePath, out package))
                        sent = SendOffer(due[i].peer, due[i].filePath, package, due[i].category, due[i].note);
                }
                catch { }
                if (sent)
                    continue;
                EntangleLogger.Warn("[Thunderstore] Not sending " + Path.GetFileName(due[i].filePath) + ". It is not a published Thunderstore package, so it will not be sent as a raw file.");
                try { due[i].notOffered?.Invoke(); }
                catch (Exception e) { EntangleLogger.Warn("[Thunderstore] " + e.Message); }
            }
        }

        static bool SendOffer(long peer, string filePath, ThunderstorePackage package, FileTransferCategory category, string note) {
            string offerName = Path.GetFileName(filePath);
            if (!IsFileName(offerName)) {
                EntangleLogger.Warn("[Thunderstore] Not offering " + offerName + ": the file name can't be sent.");
                return false;
            }

            var data = new ThunderstoreOfferData {
                author = package.author,
                name = package.name,
                version = package.version,
                fileName = offerName,
                note = note ?? "",
                category = category,
            };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ThunderstoreOffer, data);
            if (message == null)
                return false;
            Node.activeNode?.SendMessage(peer, NetworkChannel.Reliable, message.GetBytes());
            EntangleLogger.Log("[Thunderstore] Offered " + package.author + "/" + package.name + "/" + package.version + " instead of sending " + offerName);
            return true;
        }

        public static void AcceptOffer(long sender, ThunderstoreOfferData data) {
            if (SyncPrefs.IsUserBlocked(sender))
                return;
            if (data == null || !IsToken(data.author) || !IsToken(data.name) || !IsToken(data.version)) {
                EntangleLogger.Warn("[Thunderstore] Rejected a package offer from " + sender + " with a bad name.");
                return;
            }
            if (!string.IsNullOrEmpty(data.fileName) && !IsFileName(data.fileName)) {
                EntangleLogger.Warn("[Thunderstore] Rejected a package offer from " + sender + " with a bad file name.");
                return;
            }

            OfferPackage(sender, new ThunderstorePackage {
                author = data.author,
                name = data.name,
                version = data.version,
                displayName = string.IsNullOrEmpty(data.fileName) ? data.name : data.fileName,
            }, data.category, data.fileName ?? "", data.note ?? "", false);
        }

        public static void OfferPackage(long sender, ThunderstorePackage package, FileTransferCategory category, string fileName, string note, bool installMod) {
            if (!IsToken(package.author) || !IsToken(package.name)) {
                EntangleLogger.Warn("[Thunderstore] Ignored an offer with an unusable name: '"
                    + package.author + "/" + package.name + "'");
                return;
            }

            bool matchedWithoutVersion;
            if (HasInstalledVersion(package.author, package.name, package.version, out matchedWithoutVersion) && !installMod) {
                bool contentMissing = false;

                if (category == FileTransferCategory.CustomItem)
                    contentMissing = true;
                else if (category == FileTransferCategory.CustomMap)
                    contentMissing = string.IsNullOrEmpty(fileName) || !CustomMapSync.HasLocalMap(fileName);
                else if (category == FileTransferCategory.Playermodel) {
                    try {
                        contentMissing = string.IsNullOrEmpty(fileName)
                            || !File.Exists(Path.Combine(Entanglement.Compat.Playermodels.PlayermodelsPatch.playerModelsPath,
                                                         Path.GetFileName(fileName)));
                    }
                    catch { contentMissing = true; }
                }

                if (!contentMissing) {
                    EntangleLogger.Debug("[Thunderstore] Skipping " + package.author + "/" + package.name
                        + " " + package.version + ": already installed"
                        + (matchedWithoutVersion ? " (matched without a version)" : "") + ".");
                    return;
                }
            }

            string key = package.author + "/" + package.name + "/" + (package.version ?? "") + "/" + (fileName ?? "") + "/" + (installMod ? "mod" : "file");
            lock (gate) {
                for (int i = 0; i < pendingOffers.Count; i++) {
                    if (pendingOffers[i].key == key) {
                        EntangleLogger.Debug("[Thunderstore] Already asking for " + key);
                        return;
                    }
                }
                string label = string.IsNullOrEmpty(package.displayName) ? package.name : package.displayName;
                pendingOffers.Add(new PendingOffer {
                    key = key,
                    sender = sender,
                    package = package,
                    category = category,
                    fileName = fileName ?? "",
                    note = note ?? "",
                    label = label,
                });
            }

            string shown = string.IsNullOrEmpty(fileName) ? package.name : Path.GetFileName(fileName);
            SetNotice("Waiting on you to accept " + shown + ".");
            EntangleLogger.Log("[Thunderstore] Asking to download " + package.author + "/" + package.name);
            try { GameRadial.ForceOpenDownloads(); }
            catch { }
        }

        public static void AcceptPending(string key) {
            PendingOffer offer = default(PendingOffer);
            bool found = false;
            lock (gate) {
                for (int i = 0; i < pendingOffers.Count; i++) {
                    if (pendingOffers[i].key != key)
                        continue;
                    offer = pendingOffers[i];
                    pendingOffers.RemoveAt(i);
                    found = true;
                    break;
                }
            }
            if (!found)
                return;
            bool installMod = offer.key.EndsWith("/mod");
            Queue(offer.package, offer.category, offer.fileName, offer.note, installMod, true);
        }

        public static void DeclinePending(string key) {
            string fileName = null;
            string note = null;
            FileTransferCategory category = FileTransferCategory.CustomItem;
            lock (gate) {
                for (int i = 0; i < pendingOffers.Count; i++) {
                    if (pendingOffers[i].key != key)
                        continue;
                    fileName = pendingOffers[i].fileName;
                    note = pendingOffers[i].note;
                    category = pendingOffers[i].category;
                    pendingOffers.RemoveAt(i);
                    break;
                }
            }
            if (!string.IsNullOrEmpty(fileName)) {
                CustomMapSync.ClearRequested(fileName);
                PlayermodelSync.ClearRequested(fileName);
            }
            if (category == FileTransferCategory.CustomItem) {
                if (!string.IsNullOrEmpty(note))
                    CustomItemSync.ClearRequested(note);
                if (!string.IsNullOrEmpty(fileName))
                    CustomItemSync.ClearRequested(fileName);
            }
            // Only cancel an in-flight join when the declined offer was blocking it.
            Entanglement.Network.DownloadGate.CancelDeferredJoinIfRequired(fileName, category == FileTransferCategory.CustomMap);
        }

        public static void AcceptAllPending() {
            PendingOffer[] copy;
            lock (gate) {
                copy = pendingOffers.ToArray();
                pendingOffers.Clear();
            }
            for (int i = 0; i < copy.Length; i++) {
                bool installMod = copy[i].key.EndsWith("/mod");
                Queue(copy[i].package, copy[i].category, copy[i].fileName, copy[i].note, installMod, true);
            }
        }

        public static void DeclineAllPending() {
            PendingOffer[] copy;
            lock (gate) {
                copy = pendingOffers.ToArray();
                pendingOffers.Clear();
            }
            for (int i = 0; i < copy.Length; i++) {
                if (!string.IsNullOrEmpty(copy[i].fileName)) {
                    CustomMapSync.ClearRequested(copy[i].fileName);
                    PlayermodelSync.ClearRequested(copy[i].fileName);
                }
                if (copy[i].category == FileTransferCategory.CustomItem) {
                    if (!string.IsNullOrEmpty(copy[i].note))
                        CustomItemSync.ClearRequested(copy[i].note);
                    if (!string.IsNullOrEmpty(copy[i].fileName))
                        CustomItemSync.ClearRequested(copy[i].fileName);
                }
            }
            Entanglement.Network.DownloadGate.CancelDeferredJoin("Declined required downloads. Not joining.");
        }

        public static bool HasInstalledVersion(string author, string name, string version, out bool matchedWithoutVersion) {
            matchedWithoutVersion = false;

            if (!IsToken(author) || !IsToken(name))
                return false;

            EnsureCatalog();
            bool sawPackage = false;
            for (int i = 0; i < catalog.Count; i++) {
                if (!string.Equals(catalog[i].author, author, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(catalog[i].name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                sawPackage = true;

                // No version to compare against on either side - treat as a versionless match.
                if (!IsToken(version) || !IsToken(catalog[i].version))
                    continue;

                if (string.Equals(catalog[i].version, version, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (sawPackage) {
                if (IsToken(version)) {
                    EntangleLogger.Debug("[Thunderstore] Have " + author + "/" + name
                        + " but not version " + version + "; fetching the sender's version.");
                    return false;
                }
                matchedWithoutVersion = true;
                return true;
            }

            if (HasInstalled(author, name)) {
                matchedWithoutVersion = true;
                return true;
            }

            return false;
        }

        public static bool HasInstalled(string author, string name) {
            if (!IsToken(author) || !IsToken(name))
                return false;
            EnsureCatalog();
            for (int i = 0; i < catalog.Count; i++) {
                if (string.Equals(catalog[i].author, author, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(catalog[i].name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            try {
                foreach (MelonMod mod in MelonHandler.Mods) {
                    if (mod == null)
                        continue;
                    string modName = null;
                    try { modName = mod.Info.Name; } catch { }
                    if (string.IsNullOrEmpty(modName))
                        continue;
                    if (NamesMatch(modName, name) || NamesMatch(modName, author + name) || SharesToken(modName, name))
                        return true;
                }
            }
            catch { }
            return HasMatchingContentFile(name);
        }

        public static List<ThunderstorePackage> GetIndexedPackages() {
            EnsureCatalog();
            var unique = new List<ThunderstorePackage>();
            for (int i = 0; i < catalog.Count; i++) {
                ThunderstorePackage item = catalog[i];
                if (!IsToken(item.author) || !IsToken(item.name) || !IsToken(item.version))
                    continue;
                bool dup = false;
                for (int u = 0; u < unique.Count; u++) {
                    if (string.Equals(unique[u].author, item.author, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(unique[u].name, item.name, StringComparison.OrdinalIgnoreCase)) {
                        dup = true;
                        break;
                    }
                }
                if (!dup)
                    unique.Add(item);
            }
            return unique;
        }

        public static FileTransferCategory GuessCategory(ThunderstorePackage package) {
            string folded = FoldName(package.name) + FoldName(package.displayName);
            if (folded.IndexOf("playermodel") >= 0 || folded == "playermodels")
                return FileTransferCategory.Custom1;
            if (folded.IndexOf("custommaps") >= 0 || folded == "custommap")
                return FileTransferCategory.Custom1;
            if (folded.IndexOf("fieldinject") >= 0 || folded.IndexOf("melonloader") >= 0 || folded.IndexOf("modthatisnotmod") >= 0)
                return FileTransferCategory.Custom1;
            if (folded.IndexOf("playground") >= 0 || folded.IndexOf("wuhu") >= 0 || folded.IndexOf("island") >= 0
                || folded.EndsWith("map") || folded.IndexOf("bcm") >= 0)
                return FileTransferCategory.CustomMap;
            if (folded.IndexOf("spider") >= 0 || folded.IndexOf("body") >= 0)
                return FileTransferCategory.Playermodel;
            return FileTransferCategory.CustomItem;
        }

        static bool HasMatchingContentFile(string packageName) {
            string folded = FoldName(packageName);
            if (folded.Length < 4)
                return false;
            if (FolderHasToken(Entanglement.Compat.CustomMaps.CustomMapsPatch.customMapsPath, folded, ".bcm", ".cma"))
                return true;
            if (FolderHasToken(Entanglement.Compat.Playermodels.PlayermodelsPatch.playerModelsPath, folded, ".body", null))
                return true;
            return false;
        }

        static bool FolderHasToken(string folder, string folded, string ext1, string ext2) {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return false;
            string[] files;
            try { files = Directory.GetFiles(folder); }
            catch { return false; }
            for (int i = 0; i < files.Length; i++) {
                string name = Path.GetFileName(files[i]);
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!string.IsNullOrEmpty(ext1) && !name.EndsWith(ext1, StringComparison.OrdinalIgnoreCase)
                    && (ext2 == null || !name.EndsWith(ext2, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (FoldName(Path.GetFileNameWithoutExtension(name)).IndexOf(folded, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        public static void QueueSaved(ThunderstorePackage package, FileTransferCategory category, string fileName) {
            Queue(package, category, fileName, "", false, false);
        }

        static void Queue(ThunderstorePackage package, FileTransferCategory category, string fileName, string note, bool installMod, bool apply = true) {
            if (!IsToken(package.author) || !IsToken(package.name))
                return;
            if (!IsToken(package.version))
                package.version = "latest";

            string key = package.author + "/" + package.name + "/" + package.version + "/" + (fileName ?? "");
            lock (gate) {
                if (!queued.Add(key))
                    return;
                busy = true;
                activeName = string.IsNullOrEmpty(package.name) ? fileName : package.name;
            }

            Thread thread = new Thread(() => Run(package, category, fileName, note, installMod, apply, key)) {
                IsBackground = true,
                Name = "EntanglementThunderstore"
            };
            thread.Start();
        }

        static void Run(ThunderstorePackage package, FileTransferCategory category, string fileName, string note, bool installMod, bool apply, string key) {
            string label = package.name + " " + package.version;

            try {
                lock (gate) {
                    busy = true;
                    activeName = label;
                    received = 0;
                    total = 0;
                }
                SetNotice("Pulling " + label + " off Thunderstore.");

                string url = LookupDownloadUrl(package);
                if (string.IsNullOrEmpty(url)) {
                    string latest = LookupLatestVersion(package.author, package.name);
                    if (!IsToken(latest))
                        throw new InvalidOperationException("Thunderstore has no download for that version");

                    if (IsToken(package.version) && package.version != "latest"
                        && !string.Equals(package.version, latest, StringComparison.OrdinalIgnoreCase)) {
                        EntangleLogger.Warn("[Thunderstore] " + package.author + "/" + package.name
                            + " version " + package.version + " is not downloadable; falling back to "
                            + latest + ". This does NOT match the sender's version and may desync.");
                        SetNotice(package.name + ": they are on " + package.version + ", you are getting " + latest + ".");
                    }

                    package.version = latest;
                    label = package.name + " " + package.version;
                    url = LookupDownloadUrl(package);
                }
                if (string.IsNullOrEmpty(url))
                    throw new InvalidOperationException("Thunderstore has no download for that version");

                string folder = Path.Combine(MelonUtils.UserDataDirectory, "Entanglement", "Thunderstore");
                Directory.CreateDirectory(folder);
                string zipPath = Path.Combine(folder, package.name + "-" + package.version + ".zip");
                Download(url, zipPath);

                if (installMod)
                    ExtractMod(zipPath);
                else if (string.IsNullOrEmpty(fileName)) {
                    if (category == FileTransferCategory.CustomMap)
                        fileName = ExtractMapFile(zipPath);
                    else if (category == FileTransferCategory.Playermodel)
                        fileName = ExtractBodyFiles(zipPath);
                    else
                        fileName = ExtractFirstContentFile(zipPath, category);
                }
                else if (ExtractNamed(zipPath, fileName, category))
                    installMod = true;
                if (!installMod && !string.IsNullOrEmpty(fileName))
                    RememberFile(fileName, package);

                try { File.Delete(zipPath); } catch { }

                lock (gate) {
                    finished.Add(new Done {
                        category = category,
                        fileName = fileName,
                        note = note,
                        installMod = installMod,
                        apply = apply,
                        label = label,
                    });
                }
                EntangleLogger.Log("[Thunderstore] Finished " + package.author + "/" + package.name + "/" + package.version);
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Thunderstore] " + label + " failed: " + e.Message);
                SetNotice("Could not download " + label + ".");
            }
            finally {
                lock (gate) {
                    queued.Remove(key);
                    busy = queued.Count > 0;
                    if (!busy) {
                        activeName = "";
                        received = 0;
                        total = 0;
                    }
                }
            }
        }

        static HttpClient NewClient(TimeSpan timeout) {
            HttpClientHandler handler = new HttpClientHandler() {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            };
            HttpClient client = new HttpClient(handler);
            client.Timeout = timeout;
            try { client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EntanglementRedux/2.1.0"); }
            catch { }
            return client;
        }

        static string LookupLatestVersion(string author, string name) {
            HttpClient client = NewClient(TimeSpan.FromSeconds(20));
            try {
                string api = ApiRoot + author + "/" + name + "/";
                string json = client.GetStringAsync(api).GetAwaiter().GetResult();
                int latest = json.IndexOf("\"latest\"", StringComparison.Ordinal);
                if (latest < 0)
                    return null;
                const string key = "\"version_number\":\"";
                int start = json.IndexOf(key, latest, StringComparison.Ordinal);
                if (start < 0)
                    return null;
                start += key.Length;
                int end = json.IndexOf('"', start);
                if (end < 0)
                    return null;
                return json.Substring(start, end - start);
            }
            finally {
                client.Dispose();
            }
        }

        static string LookupDownloadUrl(ThunderstorePackage package) {
            HttpClient client = NewClient(TimeSpan.FromSeconds(20));
            try {
                string api = ApiRoot + package.author + "/" + package.name + "/";
                string json = client.GetStringAsync(api).GetAwaiter().GetResult();
                return FindDownloadUrl(json, package.version);
            }
            finally {
                client.Dispose();
            }
        }

        static string FindDownloadUrl(string json, string version) {
            if (string.IsNullOrEmpty(json))
                return null;

            string needle = "\"version_number\":\"" + version + "\"";
            int at = json.IndexOf(needle, StringComparison.Ordinal);
            if (at < 0)
                return null;

            const string key = "\"download_url\":\"";
            int start = json.IndexOf(key, at, StringComparison.Ordinal);
            int nextVersion = json.IndexOf("\"version_number\"", at + needle.Length, StringComparison.Ordinal);
            if (start < 0 || (nextVersion > 0 && start > nextVersion))
                return null;

            start += key.Length;
            int end = json.IndexOf('"', start);
            if (end < 0)
                return null;

            string url = json.Substring(start, end - start).Replace("\\/", "/");
            if (!IsAllowedUrl(url))
                throw new InvalidOperationException("download URL is not on Thunderstore");
            return url;
        }

        static void Download(string url, string zipPath) {
            HttpClientHandler handler = new HttpClientHandler() {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None
            };
            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(DownloadTimeoutMinutes);
            try { client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EntanglementRedux/2.1.0"); }
            catch { }
            try {
                for (int hop = 0; hop < 5; hop++) {
                    if (!IsAllowedUrl(url))
                        throw new InvalidOperationException("refusing a redirect off Thunderstore");

                    HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400) {
                        Uri next = response.Headers.Location;
                        response.Dispose();
                        if (next == null)
                            throw new InvalidOperationException("redirect had no target");
                        url = next.IsAbsoluteUri ? next.AbsoluteUri : new Uri(new Uri(url), next).AbsoluteUri;
                        continue;
                    }

                    if (code < 200 || code >= 300) {
                        response.Dispose();
                        throw new InvalidOperationException("Thunderstore returned " + code);
                    }

                    long length = response.Content.Headers.ContentLength ?? 0;
                    if (length > MaxZipBytes) {
                        response.Dispose();
                        throw new InvalidOperationException("package is over the size limit");
                    }
                    lock (gate)
                        total = length;

                    using (Stream input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (FileStream output = File.Create(zipPath)) {
                        byte[] buffer = new byte[262144];
                        int read;
                        long got = 0;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                            got += read;
                            if (got > MaxZipBytes)
                                throw new InvalidOperationException("package is over the size limit");
                            output.Write(buffer, 0, read);
                            lock (gate)
                                received = got;
                        }
                    }
                    response.Dispose();
                    return;
                }
                throw new InvalidOperationException("too many redirects");
            }
            finally {
                client.Dispose();
                handler.Dispose();
            }
        }

        static bool ExtractNamed(string zipPath, string fileName, FileTransferCategory category) {
            if (!IsFileName(fileName))
                throw new InvalidOperationException("refusing that file name");
            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) {
                ExtractDll(zipPath, fileName);
                return true;
            }
            if (IsDangerous(fileName))
                throw new InvalidOperationException("refusing that file name");

            string dest = Destination(category, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));

            using (ZipArchive zip = ZipFile.OpenRead(zipPath)) {
                ZipArchiveEntry match = null;
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;
                    if (!string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!SafeEntry(entry.FullName))
                        continue;
                    match = entry;
                    break;
                }
                if (match == null)
                    throw new InvalidOperationException("package does not contain " + fileName);
                match.ExtractToFile(dest, true);
            }
            return false;
        }

        static string ExtractBodyFiles(string zipPath) {
            string first = null;
            string folder = Entanglement.Compat.Playermodels.PlayermodelsPatch.playerModelsPath;
            Directory.CreateDirectory(folder);
            using (ZipArchive zip = ZipFile.OpenRead(zipPath)) {
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (string.IsNullOrEmpty(entry.Name) || !SafeEntry(entry.FullName))
                        continue;
                    if (!entry.Name.EndsWith(".body", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (entry.Name.IndexOf('/') >= 0 || entry.Name.IndexOf('\\') >= 0)
                        continue;
                    string dest = Path.Combine(folder, entry.Name);
                    entry.ExtractToFile(dest, true);
                    if (first == null)
                        first = entry.Name;
                }
            }
            if (first == null)
                throw new InvalidOperationException("package has no playermodel");
            return first;
        }

        static string ExtractFirstContentFile(string zipPath, FileTransferCategory category) {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath)) {
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (string.IsNullOrEmpty(entry.Name) || !SafeEntry(entry.FullName) || !IsFileName(entry.Name))
                        continue;
                    string lower = entry.Name.ToLowerInvariant();
                    bool map = lower.EndsWith(".cma") || lower.EndsWith(".bcm");
                    bool model = lower.EndsWith(".body");
                    bool item = lower.EndsWith(".melon") || lower.EndsWith(".bundle");
                    if (category == FileTransferCategory.CustomMap && !map)
                        continue;
                    if (category == FileTransferCategory.Playermodel && !model)
                        continue;
                    if (category == FileTransferCategory.CustomItem && !item && !map && !model)
                        continue;
                    if (!map && !model && !item)
                        continue;
                    FileTransferCategory dest = map ? FileTransferCategory.CustomMap
                        : model ? FileTransferCategory.Playermodel
                        : FileTransferCategory.CustomItem;
                    string path = Destination(dest, entry.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path, true);
                    return entry.Name;
                }
            }
            throw new InvalidOperationException("package has no usable content file");
        }

        static string ExtractMapFile(string zipPath) {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath)) {
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (string.IsNullOrEmpty(entry.Name) || !SafeEntry(entry.FullName) || !IsFileName(entry.Name))
                        continue;
                    string lower = entry.Name.ToLowerInvariant();
                    if (!lower.EndsWith(".cma") && !lower.EndsWith(".bcm"))
                        continue;
                    string dest = Destination(FileTransferCategory.CustomMap, entry.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                    return entry.Name;
                }
            }
            throw new InvalidOperationException("package has no map file");
        }

        static void ExtractDll(string zipPath, string fileName) {
            string mods = Path.Combine(Directory.GetParent(MelonUtils.UserDataDirectory).FullName, "Mods");
            Directory.CreateDirectory(mods);
            using (ZipArchive zip = ZipFile.OpenRead(zipPath)) {
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (!string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!SafeEntry(entry.FullName))
                        continue;
                    entry.ExtractToFile(Path.Combine(mods, entry.Name), true);
                    return;
                }
            }
            throw new InvalidOperationException("package does not contain " + fileName);
        }

        static void ExtractMod(string zipPath) {
            string mods = Path.Combine(Directory.GetParent(MelonUtils.UserDataDirectory).FullName, "Mods");
            Directory.CreateDirectory(mods);

            using (ZipArchive zip = ZipFile.OpenRead(zipPath)) {
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (string.IsNullOrEmpty(entry.Name) || !SafeEntry(entry.FullName))
                        continue;
                    if (IsDangerous(entry.Name) && !entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && !entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        && !entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                        && !entry.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        && !entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                        && !entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string dest = Path.Combine(mods, entry.Name);
                    entry.ExtractToFile(dest, true);
                }
            }
        }

        static string Destination(FileTransferCategory category, string fileName) {
            if (category == FileTransferCategory.CustomMap)
                return Path.Combine(Entanglement.Compat.CustomMaps.CustomMapsPatch.customMapsPath, fileName);
            if (category == FileTransferCategory.Playermodel)
                return Path.Combine(Entanglement.Compat.Playermodels.PlayermodelsPatch.playerModelsPath, fileName);
            return Path.Combine(MelonUtils.UserDataDirectory, "Entanglement", "SyncedItems", fileName);
        }

        static bool SafeEntry(string fullName) {
            if (string.IsNullOrEmpty(fullName))
                return false;
            string slash = fullName.Replace('\\', '/');
            if (slash.StartsWith("/") || slash.IndexOf("..", StringComparison.Ordinal) >= 0)
                return false;
            return true;
        }

        static bool IsDangerous(string fileName) {
            string lower = fileName.ToLowerInvariant();
            return lower.EndsWith(".exe") || lower.EndsWith(".bat") || lower.EndsWith(".cmd")
                || lower.EndsWith(".ps1") || lower.EndsWith(".vbs") || lower.EndsWith(".js")
                || lower.EndsWith(".scr") || lower.EndsWith(".msi") || lower.EndsWith(".dll")
                || lower.EndsWith(".bytes");
        }

        static bool IsAllowedUrl(string url) {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return false;
            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                return false;
            string host = uri.Host.ToLowerInvariant();
            return host == "thunderstore.io" || host.EndsWith(".thunderstore.io");
        }

        public static bool IsToken(string value) {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || value == "." || value == "..")
                return false;
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                    || c == '_' || c == '-' || c == '.';
                if (!ok)
                    return false;
            }
            return true;
        }

        public static bool IsFileName(string value) {
            if (string.IsNullOrEmpty(value) || value.Length > 160)
                return false;
            if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.IndexOf("..", StringComparison.Ordinal) >= 0)
                return false;
            return value == Path.GetFileName(value);
        }

        public static bool TryMatchFile(string filePath, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(filePath));
            for (int i = 0; i < 6 && dir != null; i++) {
                string manifest = Path.Combine(dir.FullName, "manifest.json");
                if (File.Exists(manifest) && TryReadManifest(dir, manifest, out package))
                    return true;
                dir = dir.Parent;
            }

            // By its exact name, in what the mod managers installed (ThunderstoreIndex).
            if (ThunderstoreIndex.TryFile(filePath, out package)) {
                package = WithVersion(package);
                return true;
            }

            string fileName = Path.GetFileName(filePath);
            if (TryRemembered(fileName, out package))
                return true;

            string stem = Path.GetFileNameWithoutExtension(filePath);
            EnsureCatalog();
            ThunderstorePackage exact = new ThunderstorePackage();
            ThunderstorePackage loose = new ThunderstorePackage();
            HashSet<string> exactKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> looseKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < catalog.Count; i++) {
                ThunderstorePackage item = catalog[i];
                string key = item.author + "/" + item.name;
                if (NamesMatch(item.name, stem) || NamesMatch(item.displayName, stem)) {
                    exact = item;
                    exactKeys.Add(key);
                    continue;
                }
                if (SharesToken(stem, item.name) || SharesToken(stem, item.displayName)) {
                    loose = item;
                    looseKeys.Add(key);
                }
            }
            int exactHits = exactKeys.Count;
            int looseHits = looseKeys.Count;
            if (exactHits == 1) {
                package = exact;
                RememberFile(fileName, package);
                return true;
            }
            // Thunderstore's own list, by name, before a loose guess from what is installed here.
            if (ThunderstoreIndex.TryName(stem, null, out package)) {
                RememberFile(fileName, package);
                return true;
            }
            if (exactHits == 0 && looseHits == 1) {
                package = loose;
                RememberFile(fileName, package);
                return true;
            }
            if (TryKnownFile(fileName, stem, out package)) {
                RememberFile(fileName, package);
                return true;
            }
            EntangleLogger.Warn("[Thunderstore] Could not match file '" + fileName + "' to a Thunderstore package.");
            return false;
        }

        public static bool TryMatchModName(string modName, out ThunderstorePackage package) {
            return TryMatchModName(modName, null, out package);
        }

        public static bool TryMatchModName(string modName, string author, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            if (string.IsNullOrEmpty(modName))
                return false;
            if (TryKnownMod(modName, out package))
                return true;

            string dll = ModFile(modName);
            if (dll != null && ThunderstoreIndex.TryFile(dll, out package)) {
                package = WithVersion(package);
                return true;
            }

            // The catalog lists a package once per mod manager profile, so count packages, not rows.
            EnsureCatalog();
            ThunderstorePackage named = new ThunderstorePackage();
            ThunderstorePackage namedByAuthor = new ThunderstorePackage();
            ThunderstorePackage loose = new ThunderstorePackage();
            HashSet<string> namedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> namedByAuthorKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> looseKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < catalog.Count; i++) {
                ThunderstorePackage item = catalog[i];
                string key = item.author + "/" + item.name;
                if (NamesMatch(item.displayName, modName) || NamesMatch(item.name, modName)) {
                    named = item;
                    namedKeys.Add(key);
                    if (!string.IsNullOrEmpty(author) && string.Equals(item.author, author, StringComparison.OrdinalIgnoreCase)) {
                        namedByAuthor = item;
                        namedByAuthorKeys.Add(key);
                    }
                    continue;
                }
                if (SharesToken(modName, item.displayName) || SharesToken(modName, item.name)) {
                    loose = item;
                    looseKeys.Add(key);
                }
            }
            if (namedByAuthorKeys.Count == 1) {
                package = namedByAuthor;
                return true;
            }
            if (namedKeys.Count == 1) {
                package = named;
                return true;
            }
            if (ThunderstoreIndex.TryName(modName, author, out package))
                return true;
            if (namedKeys.Count == 0 && looseKeys.Count == 1) {
                package = loose;
                return true;
            }
            if (IsToken(author) && TryLookupAuthorPackage(author, modName, out package))
                return true;
            package = new ThunderstorePackage();
            return false;
        }

        static bool SamePackage(ThunderstorePackage a, ThunderstorePackage b) {
            return string.Equals(a.author, b.author, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        }

        // The DLL a loaded mod was read from, or null.
        static string ModFile(string modName) {
            try {
                foreach (MelonMod mod in MelonHandler.Mods) {
                    if (mod == null)
                        continue;
                    string name = null;
                    try { name = mod.Info.Name; } catch { }
                    if (!string.Equals(name, modName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string location = null;
                    try {
                        System.Reflection.PropertyInfo asm = mod.GetType().GetProperty("Assembly");
                        System.Reflection.Assembly assembly = asm != null ? asm.GetValue(mod, null) as System.Reflection.Assembly : null;
                        if (assembly != null)
                            location = assembly.Location;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(location)) {
                        try {
                            System.Reflection.PropertyInfo loc = mod.GetType().GetProperty("Location");
                            location = loc != null ? loc.GetValue(mod, null) as string : null;
                        }
                        catch { }
                    }
                    return string.IsNullOrEmpty(location) ? null : location;
                }
            }
            catch { }
            return null;
        }

        static ThunderstorePackage WithVersion(ThunderstorePackage package) {
            if (IsToken(package.version) && package.version.IndexOf('.') > 0)
                return package;
            EnsureCatalog();
            for (int i = 0; i < catalog.Count; i++) {
                if (SamePackage(catalog[i], package) && IsToken(catalog[i].version)) {
                    package.version = catalog[i].version;
                    return package;
                }
            }
            bool exists;
            string latest;
            if (ThunderstoreIndex.TryKnowsPackage(package.author, package.name, out exists, out latest) && exists && IsToken(latest)) {
                package.version = latest;
                return package;
            }
            package.version = "latest";
            return package;
        }

        static bool TryKnownMod(string modName, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            string pkgAuthor = null;
            string pkgName = null;
            string folded = FoldName(modName);

            if (folded == "customplayermodels" || folded == "playermodels" || folded == "playermodel") {
                pkgAuthor = "Maranara";
                pkgName = "Player_Models";
            }
            else if (folded == "custommaps" || folded == "custommap") {
                pkgAuthor = "Maranara";
                pkgName = "Custom_Maps";
            }
            else if (folded == "fieldinjector" || folded == "fieldinjection") {
                pkgAuthor = "WNP78";
                pkgName = "FieldInjection";
            }
            else if (folded == "modthatisnotmod") {
                pkgAuthor = "gnonme";
                pkgName = "ModThatIsNotMod";
            }

            if (pkgName == null)
                return false;

            if (TryFillFromCatalog(pkgAuthor, pkgName, modName, out package))
                return true;

            package = new ThunderstorePackage {
                author = pkgAuthor,
                name = pkgName,
                version = KnownFallbackVersion(pkgName),
                displayName = string.IsNullOrEmpty(modName) ? pkgName : modName,
            };
            return IsToken(package.version);
        }

        static string KnownFallbackVersion(string name) {
            if (name == "Player_Models")
                return "1.3.3";
            if (name == "Custom_Maps")
                return "2.1.0";
            if (name == "FieldInjection")
                return "1.1.0";
            if (name == "ModThatIsNotMod")
                return "0.2.7";
            if (name == "Playground" || name == "Wuhu_Island")
                return "1.2.0";
            if (name == "Classic_Spiderman")
                return "1.0.0";
            return "1.0.0";
        }

        static bool TryFillFromCatalog(string author, string name, string display, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            EnsureCatalog();
            for (int i = 0; i < catalog.Count; i++) {
                if (string.Equals(catalog[i].author, author, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(catalog[i].name, name, StringComparison.OrdinalIgnoreCase)
                    && IsToken(catalog[i].version)) {
                    package = catalog[i];
                    if (string.IsNullOrEmpty(package.displayName))
                        package.displayName = display;
                    return true;
                }
            }
            return false;
        }

        static bool TryKnownFile(string fileName, string stem, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            string lower = (fileName ?? "").ToLowerInvariant();
            string folded = FoldName(stem);

            if (folded.IndexOf("playground", StringComparison.Ordinal) >= 0 || lower.IndexOf("playground") >= 0)
                return TryFillLatest("Lirf", "Playground", stem, out package);
            if (folded.IndexOf("spiderman", StringComparison.Ordinal) >= 0 || lower.IndexOf("spider") >= 0)
                return TryFillLatest("josh1266", "Classic_Spiderman", stem, out package);
            if (folded.IndexOf("wuhuisland", StringComparison.Ordinal) >= 0 || lower.IndexOf("wuhu") >= 0)
                return TryFillLatest("Lirf", "Wuhu_Island", stem, out package);
            return false;
        }

        static readonly Dictionary<string, ThunderstorePackage> lookupCache = new Dictionary<string, ThunderstorePackage>(StringComparer.OrdinalIgnoreCase);

        static bool TryFillLatest(string author, string name, string display, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            string key = author + "/" + name;
            ThunderstorePackage cached;
            if (lookupCache.TryGetValue(key, out cached)) {
                package = cached;
                return true;
            }

            EnsureCatalog();
            for (int i = 0; i < catalog.Count; i++) {
                if (string.Equals(catalog[i].author, author, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(catalog[i].name, name, StringComparison.OrdinalIgnoreCase)
                    && IsToken(catalog[i].version)) {
                    package = catalog[i];
                    if (string.IsNullOrEmpty(package.displayName))
                        package.displayName = display;
                    lookupCache[key] = package;
                    return true;
                }
            }

            bool exists;
            string latest;
            if (ThunderstoreIndex.TryKnowsPackage(author, name, out exists, out latest)) {
                if (!exists)
                    return false;
                package = new ThunderstorePackage {
                    author = author,
                    name = name,
                    version = latest,
                    displayName = string.IsNullOrEmpty(display) ? name : display,
                };
                lookupCache[key] = package;
                return true;
            }

            string fallback = KnownFallbackVersion(name);
            package = new ThunderstorePackage {
                author = author,
                name = name,
                version = fallback,
                displayName = string.IsNullOrEmpty(display) ? name : display,
            };
            lookupCache[key] = package;
            catalog.Add(package);
            EntangleLogger.Debug("[Thunderstore] Mapped '" + display + "' to " + key + "/" + fallback);
            return true;
        }

        static bool TryLookupAuthorPackage(string author, string modName, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            string[] candidates = NameCandidates(modName);
            for (int i = 0; i < candidates.Length; i++) {
                if (string.IsNullOrEmpty(candidates[i]) || !IsToken(candidates[i]))
                    continue;
                if (TryFillLatest(author, candidates[i], modName, out package))
                    return true;
            }
            return false;
        }

        static string[] NameCandidates(string modName) {
            string underscored = (modName ?? "").Replace(' ', '_').Replace('-', '_');
            string noCustom = underscored;
            if (noCustom.StartsWith("Custom_", StringComparison.OrdinalIgnoreCase))
                noCustom = noCustom.Substring(7);
            else if (noCustom.StartsWith("Custom", StringComparison.OrdinalIgnoreCase) && noCustom.Length > 6)
                noCustom = noCustom.Substring(6);
            return new string[] { underscored, noCustom, FoldName(modName) };
        }

        static bool TryReadManifest(DirectoryInfo dir, string manifestPath, out ThunderstorePackage package) {
            package = new ThunderstorePackage();
            string json;
            try { json = File.ReadAllText(manifestPath); }
            catch { return false; }

            string name = ReadJsonString(json, "name");
            string version = ReadJsonString(json, "version_number");
            if (!IsToken(name) || !IsToken(version))
                return false;

            string author = AuthorFromFolder(dir.Name, name);
            if (!IsToken(author))
                return false;

            package.author = author;
            package.name = name;
            package.version = version;
            package.displayName = name;
            return true;
        }

        static string AuthorFromFolder(string folder, string packageName) {
            string suffix = "-" + packageName;
            if (folder.Length <= suffix.Length || !folder.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return null;
            return folder.Substring(0, folder.Length - suffix.Length);
        }

        static void ScanManifests(string root) {
            if (!Directory.Exists(root))
                return;
            string[] manifests;
            try { manifests = Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories); }
            catch { return; }

            for (int i = 0; i < manifests.Length; i++) {
                ThunderstorePackage package;
                if (TryReadManifest(new DirectoryInfo(Path.GetDirectoryName(manifests[i])), manifests[i], out package))
                    catalog.Add(package);
            }
        }

        static void ScanManagerLists() {
            string[] folders = ThunderstoreIndex.ManagerGameFolders;
            for (int i = 0; i < folders.Length; i++)
                ScanProfileRoot(Path.Combine(folders[i], "profiles"));
        }

        static void ScanProfileRoot(string root) {
            if (!Directory.Exists(root))
                return;
            string[] files;
            try { files = Directory.GetFiles(root, "mods.yml", SearchOption.AllDirectories); }
            catch { return; }
            for (int i = 0; i < files.Length; i++)
                ReadModsYml(files[i]);
        }

        static void ReadModsYml(string path) {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return; }

            string name = null, author = null, display = null, website = null;
            int major = -1, minor = -1, patch = -1;
            bool enabled = true;

            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i];
                if (line.StartsWith("- ")) {
                    CommitYml(name, author, display, website, major, minor, patch, enabled);
                    name = author = display = website = null;
                    major = minor = patch = -1;
                    enabled = true;
                }
                string key, value;
                if (!SplitYml(line, out key, out value))
                    continue;
                if (key == "name") name = value;
                else if (key == "authorName") author = value;
                else if (key == "displayName") display = value;
                else if (key == "websiteUrl") website = value;
                else if (key == "enabled") enabled = value != "false";
                else if (key == "major") int.TryParse(value, out major);
                else if (key == "minor") int.TryParse(value, out minor);
                else if (key == "patch") int.TryParse(value, out patch);
            }
            CommitYml(name, author, display, website, major, minor, patch, enabled);
        }

        static void CommitYml(string name, string author, string display, string website, int major, int minor, int patch, bool enabled) {
            ApplyWebsite(website, ref author, ref name);
            StripAuthorPrefix(ref author, ref name);
            if (!enabled || !IsToken(name) || !IsToken(author) || major < 0)
                return;
            string version = major + "." + minor + "." + patch;
            catalog.Add(new ThunderstorePackage {
                author = author,
                name = name,
                version = version,
                displayName = string.IsNullOrEmpty(display) ? name : display,
            });
        }

        // r2modman stores name as Author-Package (e.g. Maranara-Custom_Maps). Thunderstore wants Package.
        static void StripAuthorPrefix(ref string author, ref string name) {
            if (string.IsNullOrEmpty(author) || string.IsNullOrEmpty(name))
                return;
            string prefix = author + "-";
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(prefix.Length);
        }

        static void ApplyWebsite(string url, ref string author, ref string name) {
            if (string.IsNullOrEmpty(url))
                return;
            const string marker = "/p/";
            int at = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return;
            string rest = url.Substring(at + marker.Length).Trim('/');
            string[] parts = rest.Split('/');
            if (parts.Length < 2)
                return;
            if (IsToken(parts[0]))
                author = parts[0];
            if (IsToken(parts[1]))
                name = parts[1];
        }

        static bool NamesMatch(string a, string b) {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;
            string fa = FoldName(a);
            string fb = FoldName(b);
            return fa.Length > 0 && fa == fb;
        }

        static bool SharesToken(string a, string b) {
            string fa = FoldName(a);
            string fb = FoldName(b);
            if (fa.Length < 6 || fb.Length < 6)
                return false;
            if (fa.Contains(fb) || fb.Contains(fa))
                return true;
            string shorter = fa.Length <= fb.Length ? fa : fb;
            string longer = fa.Length <= fb.Length ? fb : fa;
            for (int len = shorter.Length; len >= 8; len--) {
                for (int i = 0; i + len <= shorter.Length; i++) {
                    if (longer.IndexOf(shorter.Substring(i, len), StringComparison.Ordinal) >= 0)
                        return true;
                }
            }
            int n = 0;
            int limit = fa.Length < fb.Length ? fa.Length : fb.Length;
            while (n < limit && fa[n] == fb[n])
                n++;
            return n >= 10;
        }

        static string FoldName(string value) {
            if (string.IsNullOrEmpty(value))
                return "";
            var chars = new char[value.Length];
            int n = 0;
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                if (c == '_' || c == '-' || c == ' ' || c == '.')
                    continue;
                chars[n++] = c;
            }
            return new string(chars, 0, n);
        }

        static readonly Dictionary<string, ThunderstorePackage> rememberedFiles = new Dictionary<string, ThunderstorePackage>(StringComparer.OrdinalIgnoreCase);

        static string RememberPath() {
            return Path.Combine(MelonUtils.UserDataDirectory, "Entanglement", "ts_files.txt");
        }

        static void RememberFile(string fileName, ThunderstorePackage package) {
            fileName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(fileName) || !IsToken(package.author) || !IsToken(package.name))
                return;
            lock (gate)
                rememberedFiles[fileName] = package;

            try {
                string folder = Path.GetDirectoryName(RememberPath());
                Directory.CreateDirectory(folder);
                File.AppendAllText(RememberPath(), fileName + "\t" + package.author + "/" + package.name + "/" + package.version + "\n");
            }
            catch { }
        }

        static bool TryRemembered(string fileName, out ThunderstorePackage package) {
            LoadRemembered();
            lock (gate)
                return rememberedFiles.TryGetValue(fileName, out package);
        }

        static bool rememberedLoaded;

        static void LoadRemembered() {
            if (rememberedLoaded)
                return;
            rememberedLoaded = true;
            string path = RememberPath();
            if (!File.Exists(path))
                return;
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return; }
            for (int i = 0; i < lines.Length; i++) {
                int tab = lines[i].IndexOf('\t');
                if (tab <= 0)
                    continue;
                string fileName = lines[i].Substring(0, tab).Trim();
                string[] parts = lines[i].Substring(tab + 1).Trim().Split('/');
                if (parts.Length != 3 || !IsToken(parts[0]) || !IsToken(parts[1]) || !IsToken(parts[2]))
                    continue;
                lock (gate) {
                    rememberedFiles[fileName] = new ThunderstorePackage {
                        author = parts[0],
                        name = parts[1],
                        version = parts[2],
                        displayName = fileName,
                    };
                }
            }
        }

        static void EnsureCatalog() {
            if (!indexStarted) {
                indexStarted = true;
                ThunderstoreIndex.Warmup();
            }
            if (catalogLoaded)
                return;
            catalogLoaded = true;
            catalog = new List<ThunderstorePackage>();
            LoadRemembered();
            ScanManifests(Path.Combine(Directory.GetParent(MelonUtils.UserDataDirectory).FullName, "Mods"));
            ScanManagerLists();
            EntangleLogger.Log("[Thunderstore] Indexed " + catalog.Count + " package(s) from r2modman/manifests.");
        }

        static bool SplitYml(string line, out string key, out string value) {
            key = null;
            value = null;
            int colon = line.IndexOf(':');
            if (colon < 0)
                return false;
            key = line.Substring(0, colon).Trim().TrimStart('-').Trim();
            value = line.Substring(colon + 1).Trim().Trim('\'', '"');
            return key.Length > 0;
        }

        static string ReadJsonString(string json, string key) {
            string needle = "\"" + key + "\":\"";
            int start = json.IndexOf(needle, StringComparison.Ordinal);
            if (start < 0)
                return null;
            start += needle.Length;
            int end = json.IndexOf('"', start);
            if (end < 0)
                return null;
            return json.Substring(start, end - start);
        }

        static void SetNotice(string text) {
            lock (gate)
                notice = text;
        }
    }
}
