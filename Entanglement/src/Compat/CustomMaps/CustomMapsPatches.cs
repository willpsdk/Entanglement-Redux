using System;
using System.Reflection;
using System.IO;
using System.Collections; // Needed for Coroutines
using UnityEngine; // Needed for GameObject and Transform
using MelonLoader; // Needed for MelonCoroutines

using Entanglement.Patching;
using Entanglement.Network;
using Entanglement.Data;

using HarmonyLib;

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
                // Custom Maps 2.0.0+
                MethodInfo abPrefixMethod = typeof(CustomMapsPatch).GetMethod("LoadMapBundle_Prefix", BindingFlags.Public | BindingFlags.Static);
                MethodInfo cmaPrefixMethod = typeof(CustomMapsPatch).GetMethod("LoadMapArchive_Prefix", BindingFlags.Public | BindingFlags.Static);

                MethodInfo postMapLoadInfo = mapLoader.GetMethod("PostScenePass", BindingFlags.Public | BindingFlags.Static);
                MethodInfo postMapLoadPostfix = typeof(CustomMapsPatch).GetMethod("PostScenePass_Postfix", BindingFlags.Public | BindingFlags.Static);

                Patcher.Patch(queueMapAssetBundle, new HarmonyMethod(abPrefixMethod));
                Patcher.Patch(queueMapArchive, new HarmonyMethod(cmaPrefixMethod));
                Patcher.Patch(postMapLoadInfo, null, new HarmonyMethod(postMapLoadPostfix));
            }

            NetworkMessage.RegisterHandler<LoadCustomMapMessageHandler>();
        }

        // FIX: The Client Spawn Coroutine
        // This waits for the map to fully render and drop, then forces the client to the spawn point
        private static IEnumerator EnsureClientSpawn()
        {
            // Give Custom Maps 1.5 seconds to fully instantiate the environment colliders
            yield return new WaitForSeconds(1.5f);

            // Locate the standard Custom Maps spawn point
            GameObject spawnObj = GameObject.Find("_SpawnPoint");
            if (spawnObj == null) spawnObj = GameObject.Find("SpawnPoint");

            if (spawnObj != null && PlayerScripts.playerRig != null)
            {
                EntangleLogger.Log("[CustomMaps] Snapping player to correct Custom Map spawn point...", ConsoleColor.Green);
                
                // Snap the RigManager to the spawn point
                PlayerScripts.playerRig.transform.position = spawnObj.transform.position;

                // Neutralize gravity/falling momentum so they don't clip through the new floor
                Rigidbody[] rigBodies = PlayerScripts.playerRig.GetComponentsInChildren<Rigidbody>();
                foreach (Rigidbody rb in rigBodies)
                {
                    rb.velocity = Vector3.zero;
                }
            }
            else
            {
                EntangleLogger.Warn("[CustomMaps] Could not find a spawn point to teleport the client to!");
            }
        }

        #region CM 1.8.0

        // Update our list of spawnables and force client spawn
        public static void LoadMap_Postfix(string path) 
        {
            SpawnableData.GetData();
            // FIX: Trigger the spawn catch if we are a client joining a custom map
            if (Node.activeNode is Client) MelonCoroutines.Start(EnsureClientSpawn());
        }

        // Only works if we're the host
        public static void QueueMap_Prefix(string mapToLoad) {
            if (Node.activeNode is Server) {
                LoadCustomMapMessageData mapData = new LoadCustomMapMessageData() {
                    mapPath = Path.GetFileName(mapToLoad),
                };

                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.CustomMap, mapData).GetBytes());
            }
        }

        #endregion

        #region CM 2.0.0+

        // Update our list of spawnables and force client spawn
        public static void PostScenePass_Postfix() 
        {
            SpawnableData.GetData();
            // FIX: Trigger the spawn catch if we are a client joining a custom map
            if (Node.activeNode is Client) MelonCoroutines.Start(EnsureClientSpawn());
        }

        // Only works if we're the host
        public static void LoadMapBundle_Prefix(string path)
        {
            if (Node.activeNode is Server)
            {
                LoadCustomMapMessageData mapData = new LoadCustomMapMessageData()
                {
                    mapPath = Path.GetFileName(path),
                };

                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.CustomMap, mapData).GetBytes());
            }
        }

        public static void LoadMapArchive_Prefix(string archivePath)
        {
            if (Node.activeNode is Server)
            {
                LoadCustomMapMessageData mapData = new LoadCustomMapMessageData()
                {
                    mapPath = Path.GetFileName(archivePath),
                };

                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.CustomMap, mapData).GetBytes());
            }
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
    }
}