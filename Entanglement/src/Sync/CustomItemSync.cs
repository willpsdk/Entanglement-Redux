using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using UnityEngine;

using MelonLoader;

using StressLevelZero.Pool;

using Entanglement.Network;

namespace Entanglement.Sync
{
    public static class CustomItemSync
    {
        public static string syncFolder => Path.Combine(MelonUtils.UserDataDirectory, "Entanglement", "SyncedItems");

        struct PendingSpawn {
            public Vector3 position;
            public Quaternion rotation;
            public ushort objectId;
            public long userId;

            // Host: a client's spawn-gun request, run as a pooled host spawn once the item is here.
            public bool hostSpawn;
            public Entanglement.Data.SimplifiedTransform transform;

            // Client: the host's pooled spawn (SpawnClient) of an item this client didn't have.
            public bool poolSpawn;
            public byte rbCount;
        }

        static readonly Dictionary<string, List<PendingSpawn>> waitingSpawns = new Dictionary<string, List<PendingSpawn>>();
        static readonly Dictionary<string, string> incomingFileTitles = new Dictionary<string, string>(); // "peer|fileName" -> title
        static readonly Dictionary<string, FileTransfer> pendingTransfersByKey = new Dictionary<string, FileTransfer>();
        static readonly HashSet<string> requestedTitles = new HashSet<string>();

        static bool initialized;

        public static void Initialize() {
            if (initialized) return;
            initialized = true;

            if (!Directory.Exists(syncFolder))
                Directory.CreateDirectory(syncFolder);

            FileTransferManager.RegisterCategoryHandler(FileTransferCategory.CustomItem, OnItemFileReceived, OnItemFileFailed);
            Thunderstore.RegisterInstaller(FileTransferCategory.CustomItem, OnThunderstoreReady);
        }

        // Called by SpawnObjectMessage when it can't resolve a title locally
        public static void RequestItem(long ownerPeer, string title, Vector3 position, Quaternion rotation, ushort objectId, long userId) {
            Request(ownerPeer, title, new PendingSpawn { position = position, rotation = rotation, objectId = objectId, userId = userId });
        }

        public static void RequestItemForHostSpawn(long client, string title, Entanglement.Data.SimplifiedTransform transform) {
            Request(client, title, new PendingSpawn { hostSpawn = true, transform = transform, position = transform.position, userId = client });
        }

        public static void RequestItemForPoolSpawn(string title, Vector3 position, Quaternion rotation, ushort id, byte rbCount) {
            long host = SteamIntegration.lobbyOwnerId;
            if (host == 0 || host == SteamIntegration.currentUserId)
                return;
            Request(host, title, new PendingSpawn { poolSpawn = true, position = position, rotation = rotation, objectId = id, rbCount = rbCount, userId = host });
        }

        static void Request(long ownerPeer, string title, PendingSpawn spawn) {
            if (!SyncPrefs.itemSyncEnabled.Value) return;
            if (!SteamIntegration.hasLobby) return;
            if (SyncPrefs.IsUserBlocked(ownerPeer)) return;
            if (string.IsNullOrEmpty(title)) return;
            if (unavailableTitles.Contains(title)) return;

            if (!waitingSpawns.TryGetValue(title, out List<PendingSpawn> pending)) {
                pending = new List<PendingSpawn>();
                waitingSpawns[title] = pending;
            }

            pending.Add(spawn);

            if (!requestedTitles.Add(title))
                return;

            EntangleLogger.Log($"[ItemSync] Missing custom item '{title}', asking {ownerPeer} for it");

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ItemSyncRequest, new ItemSyncRequestData { title = title });
            if (message != null)
                Node.activeNode?.SendMessage(ownerPeer, NetworkChannel.Reliable, message.GetBytes());

            MelonCoroutines.Start(ClearRequestIfStale(title));
        }

        static System.Collections.IEnumerator ClearRequestIfStale(string title) {
            float waited = 0f;
            while (waited < 45f) {
                waited += UnityEngine.Time.unscaledDeltaTime;
                yield return null;
                if (!requestedTitles.Contains(title))
                    yield break;
            }
            requestedTitles.Remove(title);
            EntangleLogger.Warn("[ItemSync] Request for '" + title + "' timed out; will ask again if needed");
            Entanglement.UI.GameNotify.Show("No answer about " + title + ". It will be asked for again next time it spawns.", 6f);
        }

        public static void OnItemRequested(long requester, string title) {
            if (!SyncPrefs.itemSyncEnabled.Value) return;

            if (SyncPrefs.IsUserBlocked(requester)) {
                SendUnavailable(requester, title, ReasonBlocked);
                return;
            }

            string filePath = TryFindMelonPathForItem(title, out bool hasExecutableCode);

            if (filePath == null) {
                EntangleLogger.Log($"[ItemSync] {requester} asked for '{title}' but we can't find which melon it came from");
                SendUnavailable(requester, title, ReasonNotAnItem);
                return;
            }

            if (hasExecutableCode) {
                EntangleLogger.Warn($"[ItemSync] Refusing to send '{title}' to {requester} - its melon contains compiled code, which this sync will never transfer");
                SendUnavailable(requester, title, ReasonCode);
                return;
            }

            if (SyncPrefs.IsPathBlacklisted(filePath)) {
                EntangleLogger.Log($"[ItemSync] Not sending '{title}' - {filePath} is blacklisted");
                SendUnavailable(requester, title, ReasonBlocked);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) {
                SendUnavailable(requester, title, ReasonNotAnItem);
                return;
            }

            string fileName = Path.GetFileName(filePath);

            NetworkMessage announce = NetworkMessage.CreateMessage(BuiltInMessageType.ItemSyncFileIncoming, new ItemSyncFileIncomingData { title = title, fileName = fileName });
            if (announce != null)
                Node.activeNode?.SendMessage(requester, NetworkChannel.Reliable, announce.GetBytes());

            Action notOffered = () => {
                EntangleLogger.Warn("[ItemSync] Not sending '" + title + "'. It is not a Thunderstore package.");
                SendUnavailable(requester, title, ReasonNotThunderstore);
            };
            if (Thunderstore.TryOfferFile(requester, filePath, FileTransferCategory.CustomItem, title, notOffered))
                return;
            notOffered();
        }

        const char ReasonMark = '\u0001';
        const char ReasonNotAnItem = 'n';
        const char ReasonCode = 'c';
        const char ReasonBlocked = 'b';
        const char ReasonNotThunderstore = 't';

        static void SendUnavailable(long requester, string title, char reason) {
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ItemSyncUnavailable, new ItemSyncUnavailableData { title = title + ReasonMark + reason });
            if (message != null)
                Node.activeNode?.SendMessage(requester, NetworkChannel.Reliable, message.GetBytes());
        }

        // Player models have no "can't share" message of their own; the item one carries it, marked.
        const string ModelMark = "\u0002pm:";

        public static void SendModelUnavailable(long requester, string modelPath, string why) {
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ItemSyncUnavailable,
                new ItemSyncUnavailableData { title = ModelMark + (modelPath ?? "") + ReasonMark + (why ?? "") });
            if (message != null)
                Node.activeNode?.SendMessage(requester, NetworkChannel.Reliable, message.GetBytes());
        }

        // Titles someone has said they can't share, so they aren't asked again every time one spawns.
        static readonly HashSet<string> unavailableTitles = new HashSet<string>();

        public static void OnItemUnavailable(long sender, string raw) {
            if (string.IsNullOrEmpty(raw))
                return;

            if (raw.StartsWith(ModelMark, StringComparison.Ordinal)) {
                string body = raw.Substring(ModelMark.Length);
                int split = body.IndexOf(ReasonMark);
                PlayermodelSync.OnUnavailable(sender, split >= 0 ? body.Substring(0, split) : body,
                    split >= 0 ? body.Substring(split + 1) : "it can't be shared");
                return;
            }
            string title = raw;
            char reason = ReasonNotThunderstore;
            int mark = raw.LastIndexOf(ReasonMark);
            if (mark >= 0) {
                title = raw.Substring(0, mark);
                if (mark + 1 < raw.Length)
                    reason = raw[mark + 1];
            }

            ClearRequested(title);
            waitingSpawns.Remove(title);
            bool first = unavailableTitles.Add(title);

            EntangleLogger.Warn($"[ItemSync] {sender} can't share '{title}' (reason '{reason}'), giving up on it");

            if (!first || reason == ReasonNotAnItem)
                return;

            // This only went to the log, so all anyone saw was the request and then nothing.
            string who;
            try { who = SteamIntegration.GetUserName(sender); }
            catch { who = null; }
            if (string.IsNullOrEmpty(who))
                who = "The player who spawned it";
            string why = reason == ReasonCode ? "it comes with code, which is never sent"
                : reason == ReasonBlocked ? "they have blocked sharing it"
                : "it isn't a Thunderstore package on their side";
            Entanglement.UI.GameNotify.Show(who + " can't share " + title + ": " + why + ".", 8f);
        }

        public static void ClearRequested(string title) {
            if (string.IsNullOrEmpty(title))
                return;
            requestedTitles.Remove(title);
        }

        public static void ClearAllRequested() {
            requestedTitles.Clear();
        }

        public static void OnSceneCleanup() {
            incomingFileTitles.Clear();
            pendingTransfersByKey.Clear();
            waitingSpawns.Clear();
            requestedTitles.Clear();
            unavailableTitles.Clear();
        }

        public static void OnFileIncoming(long sender, string title, string fileName) {
            incomingFileTitles[$"{sender}|{fileName}"] = title;
        }

        static void OnItemFileReceived(FileTransfer transfer) {
            string key = $"{transfer.peer}|{transfer.fileName}";
            if (!incomingFileTitles.TryGetValue(key, out string title)) {
                EntangleLogger.Warn($"[ItemSync] Received {transfer.fileName} from {transfer.peer} but never announced/requested it, ignoring");
                return;
            }
            incomingFileTitles.Remove(key);

            string destPath = Path.Combine(syncFolder, transfer.fileName);
            try {
                FileTransferManager.WriteReceivedFile(transfer, destPath);
            }
            catch (Exception e) {
                EntangleLogger.Error($"[ItemSync] Failed writing {destPath}: {e.Message}");
                waitingSpawns.Remove(title);
                requestedTitles.Remove(title);
                return;
            }

            MelonCoroutines.Start(LoadAndReplay(title, destPath));
        }

        static void OnThunderstoreReady(string fileName, string note) {
            if (string.IsNullOrEmpty(note))
                return;
            string destPath = Path.Combine(syncFolder, fileName);
            MelonCoroutines.Start(LoadAndReplay(note, destPath));
        }

        static void OnItemFileFailed(FileTransfer transfer) {
            EntangleLogger.Warn($"[ItemSync] Transfer failed for {transfer?.fileName ?? "unknown file"} from {transfer?.peer}");
            if (transfer == null)
                return;

            string key = transfer.peer + "|" + transfer.fileName;
            if (incomingFileTitles.TryGetValue(key, out string title)) {
                incomingFileTitles.Remove(key);
                ClearRequested(title);
                waitingSpawns.Remove(title);
                return;
            }

            // Thunderstore offers use the item title as note; P2P uses fileName as key fallback.
            if (!string.IsNullOrEmpty(transfer.fileName)) {
                ClearRequested(transfer.fileName);
                waitingSpawns.Remove(transfer.fileName);
            }
        }

        static IEnumerator LoadAndReplay(string title, string bundlePath) {
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(bundlePath);
            while (!request.isDone) yield return null;

            AssetBundle bundle = request.assetBundle;
            if (bundle == null) {
                EntangleLogger.Warn($"[ItemSync] {bundlePath} did not load as a valid asset bundle");
                waitingSpawns.Remove(title);
                requestedTitles.Remove(title);
                yield break;
            }

            if (bundle.GetAllAssetNames().Any(a => a.EndsWith(".bytes"))) {
                EntangleLogger.Warn($"[ItemSync] {bundlePath} contains compiled code (.bytes) - refusing to load it. This should have been caught before the transfer started.");
                bundle.Unload(true);
                waitingSpawns.Remove(title);
                requestedTitles.Remove(title);
                yield break;
            }

            bool loaded = TryLoadMelonBundle(bundle, out string loadError);
            if (!loaded) {
                EntangleLogger.Error($"[ItemSync] Failed to hand off {bundlePath} to the item loader: {loadError}");
                bundle.Unload(true);
                waitingSpawns.Remove(title);
                requestedTitles.Remove(title);
                yield break;
            }

            EntangleLogger.Log($"[ItemSync] Loaded '{title}', spawning {(waitingSpawns.TryGetValue(title, out var p) ? p.Count : 0)} pending item(s)");

            if (waitingSpawns.TryGetValue(title, out List<PendingSpawn> pending)) {
                foreach (PendingSpawn spawn in pending) {
                    if (spawn.hostSpawn) {
                        // Only while still hosting; no requester, so a second miss asks nobody.
                        if (Node.isServer)
                            MelonCoroutines.Start(SpawnRequestMessageHandler.RegisterAndSpawn(title, spawn.transform, 0));
                    }
                    else if (spawn.poolSpawn) {
                        // Built as if the SpawnClient had just arrived; RegisterAndSpawn ends the build.
                        if (!Node.isServer && !Entanglement.Objects.PooleeSyncable._PooleeLookup.ContainsKey(spawn.objectId)) {
                            Entanglement.Objects.NpcPending.BeginBuild(spawn.objectId);
                            MelonCoroutines.Start(SpawnClientMessageHandler.RegisterAndSpawn(title, spawn.position, spawn.rotation, spawn.objectId, spawn.rbCount));
                        }
                    }
                    else
                        MelonCoroutines.Start(SpawnObjectMessage.RegisterAndSpawn(title, spawn.position, spawn.rotation, spawn.objectId, spawn.userId));
                }
            }

            waitingSpawns.Remove(title);
            requestedTitles.Remove(title);
        }

        // Finds which installed melon contains the given item title via MTINM's item registry
        static string TryFindMelonPathForItem(string title, out bool hasExecutableCode) {
            hasExecutableCode = false;

            try {
                Type itemLoadingType = Type.GetType("ModThatIsNotMod.Internals.ItemLoading, ModThatIsNotMod");
                if (itemLoadingType == null) return null;

                // loadedMelons is private in MTINM (verified via decompile), no public accessor exists
                object loadedMelons = itemLoadingType.GetField("loadedMelons", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                if (!(loadedMelons is IEnumerable melons)) return null;

                foreach (object melon in melons) {
                    Type melonType = melon.GetType();
                    object loadedItems = melonType.GetField("loadedItems")?.GetValue(melon);
                    if (!(loadedItems is IEnumerable items)) continue;

                    bool containsItem = false;
                    foreach (object item in items) {
                        string itemName = item.GetType().GetField("itemName")?.GetValue(item) as string;
                        if (itemName == title) { containsItem = true; break; }
                    }

                    if (!containsItem) continue;

                    string relativePath = melonType.GetField("filePath")?.GetValue(melon) as string;
                    if (string.IsNullOrEmpty(relativePath)) return null;

                    string fullPath = Path.Combine(MelonUtils.UserDataDirectory, relativePath);

                    // Best-effort executable-code check up front, LoadAndReplay double-checks on the receive side too
                    try {
                        AssetBundle probe = AssetBundle.LoadFromFile(fullPath);
                        if (probe != null) {
                            hasExecutableCode = probe.GetAllAssetNames().Any(a => a.EndsWith(".bytes"));
                            probe.Unload(false);
                        }
                    }
                    catch { }

                    return fullPath;
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[ItemSync] Couldn't look up melon for item '{title}': {e.Message}");
            }

            return null;
        }

        static bool TryLoadMelonBundle(AssetBundle bundle, out string error) {
            error = null;
            try {
                Type itemLoadingType = Type.GetType("ModThatIsNotMod.Internals.ItemLoading, ModThatIsNotMod");
                MethodInfo loadFromBundle = itemLoadingType?.GetMethod("LoadFromBundle", BindingFlags.Public | BindingFlags.Static);
                if (loadFromBundle == null) {
                    error = "ItemLoading.LoadFromBundle not found (ModThatIsNotMod version mismatch?)";
                    return false;
                }

                object melon = loadFromBundle.Invoke(null, new object[] { bundle });
                if (melon == null) {
                    error = "LoadFromBundle returned null";
                    return false;
                }

                object loadedItems = melon.GetType().GetField("loadedItems")?.GetValue(melon);
                if (loadedItems is IEnumerable items) {
                    Pool[] allPools = UnityEngine.Object.FindObjectsOfType<Pool>();
                    foreach (object item in items) {
                        string itemName = item.GetType().GetField("itemName")?.GetValue(item) as string;
                        if (string.IsNullOrEmpty(itemName)) continue;

                        Pool match = allPools.FirstOrDefault(p => p.name == "pool - " + itemName);
                        if (match != null)
                            PoolManager.DynamicPools[itemName] = match;
                    }
                }

                return true;
            }
            catch (Exception e) {
                error = e.Message;
                return false;
            }
        }
    }
}
