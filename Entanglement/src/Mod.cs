using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;

using MelonLoader;

using Entanglement.Representation;
using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Patching;
using Entanglement.UI;
using Entanglement.Objects;
using Entanglement.Compat;
using Entanglement.Extensions;
using Entanglement.Modularity;
using Entanglement.Managers;

using ModThatIsNotMod;

using UnityEngine;

namespace Entanglement {
    public struct EntanglementVersion {
        public const byte versionMajor = 0;
        public const byte versionMinor = 3;
        public const short versionPatch = 0;

        public const byte minVersionMajorSupported = 0;
        public const byte minVersionMinorSupported = 3;
    }

    public class EntanglementMod : MelonMod {
        public static byte? sceneChange = null;
        public static Assembly entanglementAssembly;

        public static EntanglementMod Instance { get; protected set; }
        public static string VersionString { get; protected set; }

        public static bool hasUnpatched = false;

        public override void OnApplicationStart() {
            entanglementAssembly = Assembly.GetExecutingAssembly();
            Instance = this;

            VersionString = $"{EntanglementVersion.versionMajor}.{EntanglementVersion.versionMinor}.{EntanglementVersion.versionPatch}";

            EntangleLogger.Log($"Current Entanglement: Redux version is {VersionString}");
            EntangleLogger.Log($"Minimum supported Entanglement: Redux version is {EntanglementVersion.minVersionMajorSupported}.{EntanglementVersion.minVersionMinorSupported}.*");

            VersionChecking.CheckModVersion(this, "https://boneworks.thunderstore.io/package/Entanglement/Entanglement/"); // update this! We don't want it to update back to the Discord Game SDK. Change to redux page when created

            PersistentData.Initialize();
            // Discord Game SDK no longer needed - Steam-based only
            // GameSDK.LoadGameSDK();

#if DEBUG
            EntangleLogger.Log("Entanglement: Redux Debug Build!", ConsoleColor.Blue);
#endif

            try {
                EntangleLogger.Log("Entanglement: Redux - Initializing Steam...");
                SteamIntegration.Initialize();
                EntangleLogger.Log("Entanglement: Redux - SteamIntegration initialized!");
            }
            catch (Exception ex) {
                EntangleLogger.Error($"Failed to initialize Steam: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try {
                EntangleLogger.Log("Entanglement: Redux - Starting Patcher...");
                Patcher.Initialize();
                EntangleLogger.Log("Entanglement: Redux - Patcher initialized!");
            }
            catch (Exception ex) {
                EntangleLogger.Error($"Failed to initialize patchers: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try {
                EntangleLogger.Log("Entanglement: Redux - Registering message handlers...");
                NetworkMessage.RegisterHandlersFromAssembly(entanglementAssembly);
                EntangleLogger.Log("Entanglement: Redux - Message handlers registered!");
            }
            catch (Exception ex) {
                EntangleLogger.Error($"Failed to register message handlers: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try {
                EntangleLogger.Log("Entanglement: Redux - Starting client...");
                Client.StartClient();
                EntangleLogger.Log("Entanglement: Redux - Client started!");
            }
            catch (Exception ex) {
                EntangleLogger.Error($"Failed to start client: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try {
                EntangleLogger.Log("Entanglement: Redux - Loading bundles...");
                PlayerRepresentation.LoadBundle();
                LoadingScreen.LoadBundle();
                EntangleLogger.Log("Entanglement: Redux - Bundles loaded!");
            }
            catch (Exception ex) {
                EntangleLogger.Error($"Failed to load bundles: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try {
                EntangleLogger.Log("Entanglement: Redux - Creating UI...");
                EntanglementUI.CreateUI();
                EntangleLogger.Log("Entanglement: Redux - UI created!");
            }
            catch (Exception ex) {
                EntangleLogger.Error($"Failed to create UI: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try {
                EntangleLogger.Log("Entanglement: Redux - Loading ban list...");
                BanList.PullFromFile();
                EntangleLogger.Log("Entanglement: Redux - Ban list loaded!");
            }
            catch (Exception ex) {
                EntangleLogger.Error($"Failed to load ban list: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            EntangleLogger.Log("Welcome to Entanglement: Redux!", ConsoleColor.DarkYellow);
        }

        public override void OnApplicationLateStart() {
            if (SteamIntegration.isInvalid) {
                HarmonyInstance.UnpatchSelf();
                hasUnpatched = true;
            }
            else {
                PlayerDeathManager.Initialize();
            }
        }

        public override void OnUpdate() {
            if (SteamIntegration.isInvalid) {
                if (!hasUnpatched) {
                    HarmonyInstance.UnpatchSelf();
                    hasUnpatched = true;
                }
                return; 
            }

            ModuleHandler.Update();

#if DEBUG
            if (Input.GetKeyDown(KeyCode.S))
                Server.StartServer();

            if (Input.GetKeyDown(KeyCode.K))
                Server.instance?.Shutdown();

            if (Input.GetKeyDown(KeyCode.R)) {
                if (PlayerRepresentation.debugRepresentation == null)
                    PlayerRepresentation.debugRepresentation = new PlayerRepresentation("Dummy", 0);
                else
                    PlayerRepresentation.debugRepresentation.CreateRagdoll();
            }
#endif

            StatsUI.UpdateUI();
            PlayerRepresentation.SyncPlayerReps();
            DataTransaction.Process();
        }

        public override void OnFixedUpdate() {
            if (SteamIntegration.isInvalid) return;

            ModuleHandler.FixedUpdate();
            PlayerRepresentation.UpdatePlayerReps();
        }

        public override void OnLateUpdate() {
            if (SteamIntegration.isInvalid) return;
            
            ModuleHandler.LateUpdate();

            Client.instance?.Tick();
            Server.instance?.Tick();

            SteamIntegration.Tick();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) {
            if (SteamIntegration.isInvalid) return;

            ModuleHandler.OnSceneWasInitialized(buildIndex, sceneName);

            SpawnableData.GetData();
            PlayerScripts.GetPlayerScripts();
            PlayerRepresentation.GetPlayerTransforms();

            foreach (var rep in PlayerRepresentation.representations.Values)
                rep.RecreateRepresentations();

            Client.instance.currentScene = (byte)buildIndex;
            sceneChange = (byte)buildIndex;

            SteamIntegration.targetScene = sceneName.ToLower();
            SteamIntegration.UpdateActivity();
        }

        public override void BONEWORKS_OnLoadingScreen() {
            if (SteamIntegration.isInvalid) return;

            ModuleHandler.OnLoadingScreen();
            LoadingScreen.OverrideScreen();

            ObjectSync.OnCleanup();
            ObjectSync.poolPairs.Clear();

#if DEBUG
            PlayerRepresentation.debugRepresentation = null;
#endif
        }

        public override void OnApplicationQuit() {
            if (SteamIntegration.isInvalid) return;

            ModuleHandler.OnApplicationQuit();

            Node.activeNode.Shutdown();
            SteamIntegration.Shutdown();
        }
    }
}