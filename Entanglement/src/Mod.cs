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

namespace Entanglement
{
    public struct EntanglementVersion
    {
        public const byte versionMajor = 0;
        public const byte versionMinor = 3;
        public const short versionPatch = 0;

        public const byte minVersionMajorSupported = 0;
        public const byte minVersionMinorSupported = 3;
    }

    public class EntanglementMod : MelonMod
    {
        public static byte? sceneChange = null;
        public static Assembly entanglementAssembly;

        public static EntanglementMod Instance { get; protected set; }
        public static string VersionString { get; protected set; }

        public static bool hasUnpatched = false;

        // FIX: Add level change state machine to prevent race conditions
        private static bool _isLevelChanging = false;
        private static float _levelChangeTimeout = 0f;
        private const float LEVEL_CHANGE_TIMEOUT = 10f; // 10 second timeout

        public override void OnApplicationStart()
        {
            entanglementAssembly = Assembly.GetExecutingAssembly();
            Instance = this;

            VersionString = $"{EntanglementVersion.versionMajor}.{EntanglementVersion.versionMinor}.{EntanglementVersion.versionPatch}";

            EntangleLogger.Log($"Current Entanglement: Redux version is {VersionString}");
            EntangleLogger.Log($"Minimum supported Entanglement: Redux version is {EntanglementVersion.minVersionMajorSupported}.{EntanglementVersion.minVersionMinorSupported}.*");

            VersionChecking.CheckModVersion(this, "https://boneworks.thunderstore.io/package/Entanglement/Entanglement/");

            PersistentData.Initialize();

            try
            {
                EntangleLogger.Log("Entanglement: Redux - Initializing Steam...");
                SteamIntegration.Initialize();
                EntangleLogger.Log("Entanglement: Redux - SteamIntegration initialized!");
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Failed to initialize Steam: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try
            {
                EntangleLogger.Log("Entanglement: Redux - Starting Patcher...");
                Patcher.Initialize();
                EntangleLogger.Log("Entanglement: Redux - Patcher initialized!");
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Failed to initialize patchers: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try
            {
                EntangleLogger.Log("Entanglement: Redux - Registering message handlers...");
                NetworkMessage.RegisterHandlersFromAssembly(entanglementAssembly);
                EntangleLogger.Log("Entanglement: Redux - Message handlers registered!");
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Failed to register message handlers: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try
            {
                EntangleLogger.Log("Entanglement: Redux - Starting client...");
                Client.StartClient();
                EntangleLogger.Log("Entanglement: Redux - Client started!");
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Failed to start client: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try
            {
                EntangleLogger.Log("Entanglement: Redux - Loading bundles...");
                PlayerRepresentation.LoadBundle();
                LoadingScreen.LoadBundle();
                EntangleLogger.Log("Entanglement: Redux - Bundles loaded!");
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Failed to load bundles: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try
            {
                EntangleLogger.Log("Entanglement: Redux - Creating UI...");
                EntanglementUI.CreateUI();
                EntangleLogger.Log("Entanglement: Redux - UI created!");
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Failed to create UI: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            try
            {
                EntangleLogger.Log("Entanglement: Redux - Loading ban list...");
                BanList.PullFromFile();
                EntangleLogger.Log("Entanglement: Redux - Ban list loaded!");
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Failed to load ban list: {ex.Message}\nTrace: {ex.StackTrace}");
                return;
            }

            // FIX: Initialize voice chat manager
            Managers.VoiceChatManager.Initialize();
            EntangleLogger.Log("Entanglement: Redux - Voice chat initialized!");

            EntangleLogger.Log("Welcome to Entanglement: Redux!", ConsoleColor.DarkYellow);
        }

        public override void OnApplicationLateStart()
        {
            if (SteamIntegration.isInvalid)
            {
                HarmonyInstance.UnpatchSelf();
                hasUnpatched = true;
            }
            else
            {
                PlayerDeathManager.Initialize();
            }
        }

        public override void OnUpdate()
        {
            if (SteamIntegration.isInvalid)
            {
                if (!hasUnpatched)
                {
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
            PlayerRepresentation.SyncAnimationState();
            DataTransaction.Process();

            // FIX: Update voice chat proximity detection every frame
            Managers.VoiceChatManager.Tick();
        }

        public override void OnFixedUpdate()
        {
            if (SteamIntegration.isInvalid) return;

            ModuleHandler.FixedUpdate();
            PlayerRepresentation.UpdatePlayerReps();
        }

        public override void OnLateUpdate()
        {
            if (SteamIntegration.isInvalid) return;

            ModuleHandler.LateUpdate();

            Client.instance?.Tick();
            Server.instance?.Tick();

            SteamIntegration.Tick();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (SteamIntegration.isInvalid) return;

            try
            {
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Cyan);
                EntangleLogger.Log($"[LEVEL CHANGE] OnSceneWasInitialized: {sceneName} (Index: {buildIndex})", ConsoleColor.Cyan);
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Cyan);

                try
                {
                    ModuleHandler.OnSceneWasInitialized(buildIndex, sceneName);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error in ModuleHandler.OnSceneWasInitialized: {ex.Message}");
                }

                // FIX: Load SpawnableData with error handling
                try
                {
                    EntangleLogger.Log("[LEVEL CHANGE] Getting SpawnableData...", ConsoleColor.Yellow);
                    SpawnableData.GetData();
                    EntangleLogger.Log("[LEVEL CHANGE] ✓ SpawnableData loaded", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error loading SpawnableData: {ex.Message}\n{ex.StackTrace}");
                }

                // FIX: Get player scripts with null checks
                try
                {
                    EntangleLogger.Log("[LEVEL CHANGE] Getting PlayerScripts...", ConsoleColor.Yellow);
                    PlayerScripts.GetPlayerScripts();
                    EntangleLogger.Log("[LEVEL CHANGE] ✓ PlayerScripts initialized", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error initializing PlayerScripts: {ex.Message}\n{ex.StackTrace}");
                }

                // FIX: Get player transforms with error handling
                try
                {
                    EntangleLogger.Log("[LEVEL CHANGE] Getting PlayerTransforms...", ConsoleColor.Yellow);
                    PlayerRepresentation.GetPlayerTransforms();
                    EntangleLogger.Log("[LEVEL CHANGE] ✓ PlayerTransforms loaded", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error loading PlayerTransforms: {ex.Message}\n{ex.StackTrace}");
                }

                // FIX: Recreate representations with per-player error handling
                try
                {
                    EntangleLogger.Log($"[LEVEL CHANGE] Recreating {PlayerRepresentation.representations.Count} player representations...", ConsoleColor.Yellow);
                    var repsToRecreate = PlayerRepresentation.representations.Values.ToList();
                    foreach (var rep in repsToRecreate)
                    {
                        try
                        {
                            if (rep != null)
                            {
                                rep.RecreateRepresentations();
                            }
                        }
                        catch (Exception ex)
                        {
                            EntangleLogger.Error($"[LEVEL CHANGE] Error recreating representation for {rep?.playerName}: {ex.Message}");
                        }
                    }
                    EntangleLogger.Log("[LEVEL CHANGE] ✓ Player representations recreated", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error recreating player representations: {ex.Message}\n{ex.StackTrace}");
                }

                // FIX: Update client state with null check
                try
                {
                    if (Client.instance != null)
                    {
                        Client.instance.currentScene = (byte)buildIndex;
                        EntangleLogger.Log($"[LEVEL CHANGE] Client scene updated to: {buildIndex}", ConsoleColor.Yellow);
                    }
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error updating client scene: {ex.Message}");
                }

                // FIX: Set the scene change flag for the server to broadcast
                try
                {
                    sceneChange = (byte)buildIndex;
                    SteamIntegration.targetScene = sceneName.ToLower();
                    SteamIntegration.UpdateActivity();
                    EntangleLogger.Log($"[LEVEL CHANGE] Set sceneChange flag to {buildIndex}", ConsoleColor.Yellow);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error setting scene change flag: {ex.Message}");
                }

                _isLevelChanging = false;
                EntangleLogger.Log("[LEVEL CHANGE] Level change initialization complete - Scene ready for play", ConsoleColor.Green);
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Cyan);
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"[LEVEL CHANGE] Critical error in OnSceneWasInitialized: {ex.Message}\n{ex.StackTrace}");
                _isLevelChanging = false;
            }
        }

        public override void BONEWORKS_OnLoadingScreen()
        {
            if (SteamIntegration.isInvalid) return;

            try
            {
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Cyan);
                EntangleLogger.Log("[LEVEL CHANGE] BONEWORKS_OnLoadingScreen called - Beginning cleanup phase", ConsoleColor.Cyan);
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Cyan);

                _isLevelChanging = true;
                _levelChangeTimeout = LEVEL_CHANGE_TIMEOUT;

                try
                {
                    ModuleHandler.OnLoadingScreen();
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error in ModuleHandler.OnLoadingScreen: {ex.Message}");
                }

                try
                {
                    LoadingScreen.OverrideScreen();
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error overriding loading screen: {ex.Message}");
                }
                EntangleLogger.Log("[LEVEL CHANGE] Module handlers and loading screen called", ConsoleColor.Yellow);

                // FIX: More comprehensive cleanup to prevent crashes
                try
                {
                    EntangleLogger.Log("[LEVEL CHANGE] Clearing ObjectSync...", ConsoleColor.Yellow);
                    ObjectSync.OnCleanup();
                    ObjectSync.poolPairs.Clear();
                    EntangleLogger.Log("[LEVEL CHANGE] ✓ ObjectSync cleared", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error clearing ObjectSync: {ex.Message}\n{ex.StackTrace}");
                }

                try
                {
                    EntangleLogger.Log("[LEVEL CHANGE] Clearing StoryModeSync...", ConsoleColor.Yellow);
                    StoryModeSync.ClearAll();
                    EntangleLogger.Log("[LEVEL CHANGE] ✓ StoryModeSync cleared", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error clearing StoryModeSync: {ex.Message}\n{ex.StackTrace}");
                }

                try
                {
                    EntangleLogger.Log("[LEVEL CHANGE] Clearing Poolee caches...", ConsoleColor.Yellow);
                    // FIX: Clear the Poolee caches on level load to prevent ID conflicts
                    if (PooleeSyncable._Cache != null)
                        PooleeSyncable._Cache.Clear();
                    if (PooleeSyncable._PooleeLookup != null)
                        PooleeSyncable._PooleeLookup.Clear();
                    EntangleLogger.Log("[LEVEL CHANGE] ✓ Poolee caches cleared", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error clearing Poolee caches: {ex.Message}\n{ex.StackTrace}");
                }

                try
                {
                    EntangleLogger.Log("[LEVEL CHANGE] Cleaning up voice chat...", ConsoleColor.Yellow);
                    Managers.VoiceChatManager.CleanupVoiceData();
                    EntangleLogger.Log("[LEVEL CHANGE] ✓ Voice chat data cleared", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LEVEL CHANGE] Error cleaning up voice chat: {ex.Message}\n{ex.StackTrace}");
                }

#if DEBUG
                PlayerRepresentation.debugRepresentation = null;
#endif

                EntangleLogger.Log("[LEVEL CHANGE] Cleanup phase complete - Ready for new scene", ConsoleColor.Green);
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Cyan);
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"[LEVEL CHANGE] Critical error in BONEWORKS_OnLoadingScreen: {ex.Message}\n{ex.StackTrace}");
                _isLevelChanging = false;
            }
        }

        public override void OnApplicationQuit()
        {
            if (SteamIntegration.isInvalid) return;

            ModuleHandler.OnApplicationQuit();

            // FIX: Cleanup voice chat
            Managers.VoiceChatManager.CleanupAll();

            Node.activeNode.Shutdown();
            SteamIntegration.Shutdown();
        }
    }
}