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

// This mod is not a rewrite of the multiplayer mod!
// It is another MP mod made by an ex developer of the MP mod that was unsatisfied with the original mod's codebase
// There is no shared code between the two projects and any similar code is accidental / coincidental

namespace Entanglement {
    // We can compare with peers to see if they are on a supported version
    public struct EntanglementVersion {
        public const byte versionMajor = 0;
        public const byte versionMinor = 4;
        public const short versionPatch = 0;

        // Patches don't matter too much when supporting old versions
        // Although we don't support anything newer than the current version, just in case
        // 0.4.0 changed the TransformSync wire format (velocities) and added SceneEvent, so older peers are incompatible
        public const byte minVersionMajorSupported = 0;
        public const byte minVersionMinorSupported = 4;
    }

    public class EntanglementMod : MelonMod {
        public static byte? sceneChange = null;
        public static Assembly entanglementAssembly;

        public static EntanglementMod Instance { get; protected set; }
        public static string VersionString { get; protected set; }

        public static bool hasUnpatched = false;

        // Detects app suspension (headset removed / OS sleep) via the realtime gap between
        // frames. No frames run while suspended, so the first frame after resume sees the
        // whole gap - drain the buffered Steam P2P backlog before the node Ticks process it.
        private static float lastUpdateRealtime = 0f;
        private const float SUSPEND_GAP_SECONDS = 3f;

        // Runs before any of our methods are JIT compiled, so the embedded Steamworks.NET.dll
        // resolves even if type loading pulls it in before OnApplicationStart executes
        static EntanglementMod() {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                if (new AssemblyName(args.Name).Name == "Steamworks.NET")
                    return Assembly.Load(Data.EmbeddedResource.LoadFromAssembly(Assembly.GetExecutingAssembly(), "Entanglement.resources.Steamworks.NET.dll"));

                return null;
            };
        }

        public override void OnApplicationStart() {
            entanglementAssembly = Assembly.GetExecutingAssembly();
            Instance = this;

            VersionString = $"{EntanglementVersion.versionMajor}.{EntanglementVersion.versionMinor}.{EntanglementVersion.versionPatch}";

            EntangleLogger.Log($"Current Entanglement version is {VersionString}");
            EntangleLogger.Log($"Minimum supported Entanglement version is {EntanglementVersion.minVersionMajorSupported}.{EntanglementVersion.minVersionMinorSupported}.*");

            // ModThatIsNotMod version checking tools, so people know when to update!
            VersionChecking.CheckModVersion(this, "https://boneworks.thunderstore.io/package/Entanglement/Entanglement/");

            PersistentData.Initialize();
            GameSDK.LoadGameSDK();

#if DEBUG
            EntangleLogger.Log("Entanglement Debug Build!", ConsoleColor.Blue);
#endif

            SteamIntegration.Initialize();

            // This checks if Steam has an invalid instance, so that the game can proceed without freezing
            if (SteamIntegration.isInvalid) {
                EntangleNotif.InvalidSteam();
                return; 
            }

            Patcher.Initialize();

            NetworkMessage.RegisterHandlersFromAssembly(entanglementAssembly);

            Client.StartClient();

            PlayerRepresentation.LoadBundle();
            LoadingScreen.LoadBundle();

            EntanglementUI.CreateUI();

            BanList.PullFromFile();

            // TODO: Remove this upon full release
            EntangleLogger.Log("Welcome to the Entanglement pre-release!", ConsoleColor.DarkYellow);

        }

        // Unpatch methods if steam isn't found
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

            // Suspend recovery: OnUpdate runs before the node Ticks in OnLateUpdate, so the
            // backlog is gone before any of it can be handled
            float nowRealtime = Time.realtimeSinceStartup;
            if (lastUpdateRealtime > 0f && nowRealtime - lastUpdateRealtime > SUSPEND_GAP_SECONDS) {
                EntangleLogger.Log($"App was suspended for {nowRealtime - lastUpdateRealtime:F1}s, draining the stale network backlog...");
                Node.activeNode?.ClearMessageBuffer();
            }
            lastUpdateRealtime = nowRealtime;

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

            // Updates the VRIK of all the players
            PlayerRepresentation.UpdatePlayerReps();
        }

        public override void OnLateUpdate() {
            if (SteamIntegration.isInvalid) return;
            
            ModuleHandler.LateUpdate();

            Client.instance?.Tick();
            Server.instance?.Tick();

            // This will run steam's callbacks and pump incoming packets
            SteamIntegration.Tick();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) {
            if (SteamIntegration.isInvalid) return;

            // Loading is over, give the frame budget back to rendering
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.BelowNormal;
            QualitySettings.asyncUploadTimeSlice = 2;
            QualitySettings.asyncUploadBufferSize = 4;

            ModuleHandler.OnSceneWasInitialized(buildIndex, sceneName);

            SpawnableData.GetData();

            PlayerScripts.GetPlayerScripts();

            PlayerRepresentation.GetPlayerTransforms();

            foreach (var rep in PlayerRepresentation.representations.Values)
                rep.RecreateRepresentations();

            Client.instance.currentScene = (byte)buildIndex;

            // Skip the old post-load notify when the change was already announced at load start
            if (!Patching.LevelChangeAnnouncer.ConsumeAnnounce(buildIndex))
                sceneChange = (byte)buildIndex;

            SteamIntegration.targetScene = sceneName.ToLower();
            SteamIntegration.UpdateActivity();
        }

        public override void BONEWORKS_OnLoadingScreen() {
            if (SteamIntegration.isInvalid) return;

            // Nobody needs a smooth framerate on the loading screen, let Unity load as fast as it can.
            // Unity throttles async scene loading to protect the framerate by default (BelowNormal),
            // and only spends 2ms/frame uploading textures/meshes - raise both for the duration of the load.
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
            QualitySettings.asyncUploadTimeSlice = 8;
            QualitySettings.asyncUploadBufferSize = 16;

            ModuleHandler.OnLoadingScreen();

            LoadingScreen.OverrideScreen();

            ObjectSync.OnCleanup();
            ObjectSync.poolPairs.Clear();
            SceneEventSync.OnSceneCleanup();

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
