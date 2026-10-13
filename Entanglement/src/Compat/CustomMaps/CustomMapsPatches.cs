using System;
using System.Reflection;
using System.IO;

using Entanglement.Patching;
using Entanglement.Network;
using Entanglement.Data;
using Entanglement.UI;

using MelonLoader;

using Entanglement.Compat;

using HarmonyLib;

using UnityEngine;

namespace Entanglement.Compat.CustomMaps {
    [OptionalAssemblyTarget("CustomMaps")]
    public class CustomMapsPatch : OptionalAssemblyPatch {
        public static MethodInfo queueMapInfo;
        public static MethodInfo queueMapAssetBundle;
        public static MethodInfo queueMapArchive;
        public static bool isNewCustomMaps;

        public static string customMapsPath = Path.Combine(MelonLoader.MelonUtils.UserDataDirectory, "CustomMaps");

        public override void DoPatches(Assembly target) {
            Type customMaps = target.GetType("CustomMaps.CustomMaps");
            Type mapLoader = target.GetType("CustomMaps.MapLoader");

            queueMapInfo = customMaps.GetMethod("QueueMap", BindingFlags.Public | BindingFlags.Static);
            if (mapLoader != null)
            {
                isNewCustomMaps = true;
                queueMapAssetBundle = mapLoader.GetMethod("LoadMapBundle", BindingFlags.Public | BindingFlags.Static);
                queueMapArchive = mapLoader.GetMethod("LoadArchiveMap", BindingFlags.Public | BindingFlags.Static);
            }

            if (!isNewCustomMaps)
            {
                // Custom Maps 1.8.0
                MethodInfo prefixMethod = typeof(CustomMapsPatch).GetMethod("QueueMap_Prefix", BindingFlags.Public | BindingFlags.Static);

                Type mapLoading = target.GetType("CustomMaps.MapLoading");
                MethodInfo loadMapInfo = mapLoading.GetMethod("LoadMap", BindingFlags.Public | BindingFlags.Static);
                MethodInfo loadMapPostfix = typeof(CustomMapsPatch).GetMethod("LoadMap_Postfix", BindingFlags.Public | BindingFlags.Static);

                Patcher.Patch(queueMapInfo, new HarmonyMethod(prefixMethod));
                Patcher.Patch(loadMapInfo, null, new HarmonyMethod(loadMapPostfix));
            }
            else
            {
                MethodInfo hostPrefixMethod = typeof(CustomMapsPatch).GetMethod("HostLoadingMapPrefix", BindingFlags.Public | BindingFlags.Static);
                HarmonyMethod hostPrefix = new HarmonyMethod(hostPrefixMethod);

                if (queueMapAssetBundle != null)
                    Patcher.Patch(queueMapAssetBundle, hostPrefix);
                if (queueMapArchive != null)
                    Patcher.Patch(queueMapArchive, hostPrefix);

                MethodInfo postMapLoadInfo = mapLoader.GetMethod("PostScenePass", BindingFlags.Public | BindingFlags.Static);
                MethodInfo postMapLoadPostfix = typeof(CustomMapsPatch).GetMethod("PostScenePass_Postfix", BindingFlags.Public | BindingFlags.Static);
                if (postMapLoadInfo != null)
                    Patcher.Patch(postMapLoadInfo, null, new HarmonyMethod(postMapLoadPostfix));

                if (customMaps != null) {
                    EventInfo beforeLoad = customMaps.GetEvent("BeforeCustomMapLoad", BindingFlags.Public | BindingFlags.Static);
                    if (beforeLoad != null)
                        beforeLoad.AddEventHandler(null, (Action<string>)HostLoadingMap);
                }
            }

            NetworkMessage.RegisterHandler<LoadCustomMapMessageHandler>();
        }

        #region CM 1.8.0

        // Update our list of spawnables
        public static void LoadMap_Postfix(string path) => SpawnableData.GetData();

        // Only works if we're the host
        public static void QueueMap_Prefix(string mapToLoad) => HostLoadingMap(mapToLoad);

        #endregion

        #region CM 2.0.0+

        // Update our list of spawnables
        public static void PostScenePass_Postfix() => SpawnableData.GetData();

        public static void LoadMapBundle_Prefix(string path) => HostLoadingMap(path);

        public static void LoadMapArchive_Prefix(string archivePath) => HostLoadingMap(archivePath);

        public static void HostLoadingMapPrefix(string __0) => HostLoadingMap(__0);

        public static void HostLoadingMap(string pathOrName) {
            if (string.IsNullOrEmpty(pathOrName))
                return;

            string mapPath = Entanglement.Sync.CustomMapSync.ResolveMapFileName(pathOrName);
            if (string.IsNullOrEmpty(mapPath))
                return;

            // Always remember - hosting after a solo map load must still advertise to joiners.
            Entanglement.Sync.CustomMapSync.NoteLocalMap(mapPath);

            if (!(Node.activeNode is Server))
                return;

            LoadCustomMapMessageData mapData = new LoadCustomMapMessageData() {
                mapPath = mapPath,
            };

            Entanglement.Sync.CustomMapSync.BeginHostMap(mapData.mapPath);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.CustomMap, mapData).GetBytes());
        }

        #endregion

        // Cross your fingers and hope this works!
        public static void TryLoadMap(string path)
        {
            if (!isNewCustomMaps)
            {
                queueMapInfo.Invoke(null, new object[1] {
                    Path.Combine(customMapsPath, path),
                });
            }
            else
            {
                if (path.EndsWith("cma"))
                    queueMapArchive.Invoke(null, new object[1] {
                        Path.Combine(customMapsPath, path),
                    });
                else
                    queueMapAssetBundle.Invoke(null, new object[1] {
                        Path.Combine(customMapsPath, path),
                    });
            }
        }

        public static void AddToMenu(string fileName) {
            try {
                if (string.IsNullOrEmpty(fileName))
                    return;
                string label = Path.GetFileName(fileName);
                if (string.IsNullOrEmpty(label))
                    return;
                string full = Path.Combine(customMapsPath, label);
                if (!File.Exists(full)) {
                    EntangleLogger.Warn("Map is not in CustomMaps, so it cannot be listed: " + full);
                    return;
                }

                object mod = FindCustomMapsMod();
                if (mod == null) {
                    GameNotify.Show("Custom Maps is not loaded yet. Restart and the map will show up.", 8f);
                    return;
                }

                MethodInfo register = mod.GetType().GetMethod("RegisterMaps", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (register != null)
                    register.Invoke(mod, null);

                FieldInfo catField = mod.GetType().GetField("rootCategory", BindingFlags.Instance | BindingFlags.NonPublic);
                object category = catField != null ? catField.GetValue(mod) : null;
                if (category == null) {
                    // Fall back to BoneMenu category lookup if CustomMaps exposes one there.
                    category = BoneMenuReflect.FindCategory("Custom Maps");
                }
                if (category == null) {
                    GameNotify.Show("Saved " + label + ". Restart and it will be in the list.", 8f);
                    return;
                }
                if (BoneMenuReflect.CategoryHas(category, label))
                    return;

                string captured = label;
                bool archive = label.EndsWith(".cma", StringComparison.OrdinalIgnoreCase);
                if (!BoneMenuReflect.CreateFunction(category, label, archive ? Color.white : Color.grey, () => {
                    try { TryLoadMap(captured); }
                    catch (Exception load) {
                        EntangleLogger.Warn("Could not load " + captured + ": " + load.Message);
                        GameNotify.Show("Could not load " + captured + ".", 6f);
                    }
                })) {
                    GameNotify.Show("Saved " + label + ". Restart and it will be in the list.", 8f);
                    return;
                }
                GameNotify.Show(label + " has been downloaded & will be in Custom Maps!", 6f);
            }
            catch (Exception e) {
                EntangleLogger.Warn("Could not add map to Custom Maps: " + e.Message);
                GameNotify.Show("Map saved, but it is not listed yet. A restart should sort it.", 8f);
            }
        }

        static object FindCustomMapsMod() {
            foreach (MelonMod mod in MelonHandler.Mods) {
                if (mod != null && mod.GetType().FullName == "CustomMaps.CustomMaps")
                    return mod;
            }
            return null;
        }
    }
}
