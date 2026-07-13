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
    // Inspired by extraes' ItemSync, P2P over Steam instead of a third-party host, asset-bundle
    // only (never transfers .bytes/compiled code). Hooks SpawnObjectMessage: an unrecognized
    // title means we're missing the item, so we ask the spawner for it and replay once it lands.
    public static class CustomItemSync
    {
        public static string syncFolder => Path.Combine(MelonUtils.UserDataDirectory, "Entanglement", "SyncedItems");

        struct PendingSpawn {
            public Vector3 position;
            public Quaternion rotation;
            public ushort objectId;
            public long userId;
        }

        static readonly Dictionary<string, List<PendingSpawn>> waitingSpawns = new Dictionary<string, List<PendingSpawn>>();
        static readonly Dictionary<string, string> incomingFileTitles = new Dictionary<string, string>(); // "peer|fileName" -> title
        static readonly HashSet<string> requestedTitles = new HashSet<string>();

        static bool initialized;

        public static void Initialize() {
            if (initialized) return;
            initialized = true;

            if (!Directory.Exists(syncFolder))
                Directory.CreateDirectory(syncFolder);

            FileTransferManager.RegisterCategoryHandler(FileTransferCategory.CustomItem, OnItemFileReceived, OnItemFileFailed);
        }

        // Called by SpawnObjectMessage when it can't resolve a title locally
        public static void RequestItem(long ownerPeer, string title, Vector3 position, Quaternion rotation, ushort objectId, long userId) {
            if (!SyncPrefs.itemSyncEnabled.Value) return;
            if (!SteamIntegration.hasLobby) return;
            if (SyncPrefs.IsUserBlocked(ownerPeer)) return;

            if (!waitingSpawns.TryGetValue(title, out List<PendingSpawn> pending)) {
                pending = new List<PendingSpawn>();
                waitingSpawns[title] = pending;
            }

            pending.Add(new PendingSpawn { position = position, rotation = rotation, objectId = objectId, userId = userId });

            if (!requestedTitles.Add(title))
                return; // Already asked for this title, just queued another spawn to replay once it lands

            EntangleLogger.Log($"[ItemSync] Missing custom item '{title}', asking {ownerPeer} for it");

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ItemSyncRequest, new ItemSyncRequestData { title = title });
            if (message != null)
                Node.activeNode?.SendMessage(ownerPeer, NetworkChannel.Reliable, message.GetBytes());
        }

        // We're the owner: someone doesn't have an item we spawned, find its melon and send it
        public static void OnItemRequested(long requester, string title) {
            if (!SyncPrefs.itemSyncEnabled.Value) return;
            if (SyncPrefs.IsUserBlocked(requester)) {
                SendUnavailable(requester, title);
                return;
            }

            string filePath = TryFindMelonPathForItem(title, out bool hasExecutableCode);

            if (filePath == null) {
                EntangleLogger.Verbose($"[ItemSync] {requester} asked for '{title}' but we can't find which melon it came from");
                SendUnavailable(requester, title);
                return;
            }

            if (hasExecutableCode) {
                EntangleLogger.Warn($"[ItemSync] Refusing to send '{title}' to {requester} - its melon contains compiled code, which this sync will never transfer");
                SendUnavailable(requester, title);
                return;
            }

            if (SyncPrefs.IsPathBlacklisted(filePath)) {
                EntangleLogger.Log($"[ItemSync] Not sending '{title}' - {filePath} is blacklisted");
                SendUnavailable(requester, title);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) {
                SendUnavailable(requester, title);
                return;
            }

            if (fileInfo.Length / 1024 > SyncPrefs.maxSyncSizeKB.Value) {
                EntangleLogger.Log($"[ItemSync] Not sending '{title}' - {fileInfo.Length / 1024}KB is over the {SyncPrefs.maxSyncSizeKB.Value}KB sync limit");
                SendUnavailable(requester, title);
                return;
            }

            string fileName = Path.GetFileName(filePath);

            NetworkMessage announce = NetworkMessage.CreateMessage(BuiltInMessageType.ItemSyncFileIncoming, new ItemSyncFileIncomingData { title = title, fileName = fileName });
            if (announce != null)
                Node.activeNode?.SendMessage(requester, NetworkChannel.Reliable, announce.GetBytes());

            EntangleLogger.Log($"[ItemSync] Sending '{title}' ({filePath}) to {requester}");
            FileTransferManager.SendFile(requester, filePath, FileTransferCategory.CustomItem);
        }

        static void SendUnavailable(long requester, string title) {
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ItemSyncUnavailable, new ItemSyncUnavailableData { title = title });
            if (message != null)
                Node.activeNode?.SendMessage(requester, NetworkChannel.Reliable, message.GetBytes());
        }

        public static void OnItemUnavailable(long sender, string title) {
            EntangleLogger.Warn($"[ItemSync] {sender} doesn't have '{title}' either, giving up on it");
            requestedTitles.Remove(title);
            waitingSpawns.Remove(title);
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

        static void OnItemFileFailed(FileTransfer transfer) {
            EntangleLogger.Warn($"[ItemSync] Transfer failed for {transfer?.fileName ?? "unknown file"} from {transfer?.peer}");
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
                foreach (PendingSpawn spawn in pending)
                    MelonCoroutines.Start(SpawnObjectMessage.RegisterAndSpawn(title, spawn.position, spawn.rotation, spawn.objectId, spawn.userId));
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
                    catch { /* If we can't probe it, DoesItemSync will still refuse it later based on the bundle contents */ }

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

                // Hot-register the item(s) into the live pool table so PoolManager.GetPool/DynamicPools sees them immediately
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
