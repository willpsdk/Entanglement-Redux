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
using Entanglement.Gamemodes;
using Entanglement.Data;
using Entanglement.Network.Layers;
using Entanglement.Utilities;

using ModThatIsNotMod;

using UnityEngine;

namespace Entanglement {
    // We can compare with peers to see if they are on a supported version
    public struct EntanglementVersion {
        public const byte versionMajor = 2;
        public const byte versionMinor = 1;
        public const short versionPatch = 0;

        // Patches don't block joins. 2.1 is the public line after Thunderstore 2.0.0.
        public const byte minVersionMajorSupported = 2;
        public const byte minVersionMinorSupported = 1;

        // 24: finished puppet/companion/combat/OwnedEntity (no half-baked stubs).
        public const byte protocolRevision = 24;

        public static ulong BuildId {
            get {
                if (buildId == 0) {
                    try { buildId = BitConverter.ToUInt64(typeof(EntanglementMod).Module.ModuleVersionId.ToByteArray(), 0); }
                    catch { buildId = 1; }
                }
                return buildId;
            }
        }
        static ulong buildId;

        /// <summary>"13:1a2b3c4d5e6f7a8b": revision and build, for the lobby's data and the log.</summary>
        public static string BuildTag => protocolRevision + ":" + BuildId.ToString("x16");

        public static bool TryParseBuildTag(string tag, out byte revision, out ulong build) {
            revision = 0;
            build = 0;
            if (string.IsNullOrEmpty(tag))
                return false;
            int colon = tag.IndexOf(':');
            if (colon <= 0)
                return false;
            return byte.TryParse(tag.Substring(0, colon), out revision)
                && ulong.TryParse(tag.Substring(colon + 1), System.Globalization.NumberStyles.HexNumber, null, out build);
        }
    }

    public class EntanglementMod : MelonMod {
        public static byte? sceneChange = null;

        // True while the host is on the loading screen. Joiners must not be sent the previous scene.
        public static bool hostLoading;
        public static Assembly entanglementAssembly;

        public static EntanglementMod Instance { get; protected set; }
        public static string VersionString { get; protected set; }

        public static bool hasUnpatched = false;

        static int menuWait = -1;
        static bool uiUnlocked;

        // Detects app suspension (headset off / OS sleep) via the realtime gap between frames
        private static float lastUpdateRealtime = 0f;
        private const float SUSPEND_GAP_SECONDS = 10f;

        static EntanglementMod() {
            Data.SteamworksEmbed.HookResolve();
        }

        public override void OnApplicationStart() {
            entanglementAssembly = Assembly.GetExecutingAssembly();
            Instance = this;

            VersionString = $"{EntanglementVersion.versionMajor}.{EntanglementVersion.versionMinor}.{EntanglementVersion.versionPatch}";

            EntangleLogger.Log($"Current Entanglement version is {VersionString} (build {EntanglementVersion.BuildTag})");
            EntangleLogger.Log($"Minimum supported Entanglement version is {EntanglementVersion.minVersionMajorSupported}.{EntanglementVersion.minVersionMinorSupported}.*");

            // The Debug Log switch as it was left, so a session can be logged from the start.
            DebugLog.Initialize();

            PersistentData.Initialize();
            // Extract again after paths are ready (static ctor already tried MelonUtils paths).
            SteamworksEmbed.EnsureSteamworksOnDisk();
            GameSDK.LoadGameSDK();

#if DEBUG
            EntangleLogger.Log("Entanglement Debug Build!", ConsoleColor.Blue);
#endif

            // Fusion ideology: pick transport first (Steam / SteamVR Horizon Link / Quest Proxy).
            NetworkLayerManager.PreferredLayerName = NetworkPrefs.ResolvedLayerTitle();
            string layerTitle = NetworkLayerManager.PreferredLayerName;
            EntangleLogger.Log($"Network layer preference: {layerTitle} ({PlatformHelper.GetPlatformName()})");

            if (NetworkLayerManager.IsProxyTitle(layerTitle)) {
                // Quest: no local Steamworks — companion on PC owns SteamVR AppID.
                SteamIntegration.EnterProxyMode(null);
            }
            else {
                uint appId = NetworkLayerManager.SteamAppIdForTitle(layerTitle);
                SteamIntegration.Initialize(appId);

                if (SteamIntegration.isInvalid) {
                    EntangleNotif.InvalidSteam();
                    return;
                }
            }

            NetworkLayerManager.Initialize();

            Patcher.Initialize();
            try {
                int patched = 0;
                foreach (MethodBase method in HarmonyInstance.GetPatchedMethods())
                    patched++;
                EntangleLogger.Log($"Patched {patched} game methods");
            }
            catch { }

            NetworkMessage.RegisterHandlersFromAssembly(entanglementAssembly);

            Client.StartClient();

            Entanglement.Sync.CustomItemSync.Initialize();
            Entanglement.Sync.PlayermodelSync.Initialize();

            PDController.Initialize();
            GamemodeManager.LoadFromAssembly(entanglementAssembly);

            MelonCoroutines.Start(CoDeferredBoot());

            BanList.PullFromFile();

            EntangleLogger.Log("Welcome to the Entanglement Redux Beta!", ConsoleColor.DarkYellow);

            UpdateCheck.Start();
        }

        static System.Collections.IEnumerator CoDeferredBoot() {
            EntangleLogger.Log("Waiting for Unity before loading embedded bundles...");
            for (int i = 0; i < 60; i++)
                yield return null;

            try {
                EntangleLogger.Log("Loading playerrep bundle...");
                PlayerRepresentation.LoadBundle();
                EntangleLogger.Log("playerrep ready.");
            }
            catch (Exception e) {
                EntangleLogger.Error("playerrep bundle failed: " + e);
            }

            try {
                LoadingScreen.LoadBundle();
                EntangleLogger.Log("logo bundle ready.");
            }
            catch (Exception e) {
                EntangleLogger.Error("logo bundle failed: " + e);
            }

        }

        // Unpatch methods if steam isn't found
        public override void OnApplicationLateStart() {
            if (SteamIntegration.isInvalid) {
                HarmonyInstance.UnpatchSelf();
                hasUnpatched = true;
            }
            // PlayerDeathManager waits for a real scene - Player_Health is not safe here.
        }

        public override void OnUpdate() {
            if (SteamIntegration.isInvalid) {
                if (!hasUnpatched) {
                    HarmonyInstance.UnpatchSelf();
                    hasUnpatched = true;
                }
                return;
            }

            Entanglement.Network.NetStats.BeginFrame();
            Entanglement.Patching.HitSource.BeginFrame();

            float nowRealtime = Time.realtimeSinceStartup;
            if (lastUpdateRealtime > 0f && nowRealtime - lastUpdateRealtime > SUSPEND_GAP_SECONDS) {
                EntangleLogger.Debug($"App was suspended for {nowRealtime - lastUpdateRealtime:F1}s, draining the stale network backlog...");
                Node.activeNode?.ClearMessageBuffer();
            }
            lastUpdateRealtime = nowRealtime;

            SafeTick("TransformSyncBatcher", TransformSyncBatcher.Flush);

            SafeTick("VoiceManager", Entanglement.Voice.VoiceManager.Tick);
            SafeTick("LargePropSync", Entanglement.Objects.LargePropSync.Tick);
            SafeTick("SpawnCatchUp", Entanglement.Objects.SpawnCatchUp.Tick);
            SafeTick("NpcDespawnSync", Entanglement.Objects.NpcDespawnSync.Tick);
            SafeTick("NpcOwnerWatch", Entanglement.Objects.NpcOwnerWatch.Tick);
            SafeTick("RemoteZonePresence", Entanglement.Objects.RemoteZonePresence.Tick);
            SafeTick("RepTargetEmulator", Entanglement.Objects.RepTargetEmulator.Tick);
            SafeTick("SceneNpcSync", Entanglement.Objects.SceneNpcSync.Tick);
            SafeTick("NpcConfigSync", Entanglement.Objects.NpcConfigSync.Tick);
            SafeTick("TurretSync", Entanglement.Objects.TurretSync.Tick);

            SafeTick("ModuleHandler", ModuleHandler.Update);

            SafeTick("NetStats", Entanglement.Network.NetStats.Tick);
            SafeTick("PhysicsCatchUp", Entanglement.Network.PhysicsCatchUp.Tick);
            SafeTick("DownloadUI", DownloadUI.Update);
            if (!uiUnlocked) {
                if (menuWait > 0)
                    menuWait--;
                else if (menuWait == 0) {
                    uiUnlocked = true;
                    menuWait = -1;
                }
            }
            if (uiUnlocked) {
                SafeTick("EntanglementRadial", EntanglementRadial.Tick);
                SafeTick("GameRadial", GameRadial.Tick);
                SafeTick("GameNotify", GameNotify.Tick);
                SafeTick("UpdateCheck", UpdateCheck.Tick);
            }
            SafeTick("GamemodeManager", GamemodeManager.Tick);
            SafeTick("NetworkLayer", NetworkLayerManager.Tick);
            SafeTick("SyncPlayerReps", PlayerRepresentation.SyncPlayerReps);
            SafeTick("GripRadius", Entanglement.Patching.GripRadiusPatch.Flush);
            SafeTick("UpdatePlayerReps.Frame", PlayerRepresentation.UpdatePlayerRepsPerFrame);
            SafeTick("FileTransfer", Entanglement.Sync.FileTransferManager.Tick);
            SafeTick("Thunderstore", Entanglement.Sync.Thunderstore.Tick);
            SafeTick("DownloadGate", Entanglement.Network.DownloadGate.Tick);
            SafeTick("SlowMo", Entanglement.Network.SlowMoSync.Tick);
            SafeTick("Lethality", PlayerDeathManager.CheckLethality);
            SafeTick("HeldGripGuard", Entanglement.Objects.HeldGripGuard.Tick);
            SafeTick("CorpseCleanup", Entanglement.Objects.CorpseCleanup.Tick);
            SafeTick("NpcPoolGuard", Entanglement.Objects.NpcPoolGuard.Tick);
            SafeTick("LocalZoneWatch", Entanglement.Objects.LocalZoneWatch.Tick);
            SafeTick("BossClawSync", Entanglement.Objects.BossClawSync.Tick);
            SafeTick("ManipulatorSync", Entanglement.Patching.ManipulatorSync.Tick);
            SafeTick("RepCollisionGuard", Entanglement.Objects.RepCollisionGuard.Tick);
        }

        static readonly Dictionary<string, int> tickFailures = new Dictionary<string, int>();

        static void SafeTick(string name, Action tick) {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { tick(); }
            catch (Exception e) {
                int count;
                tickFailures.TryGetValue(name, out count);
                count++;
                tickFailures[name] = count;

                if (count <= 3 || count % 600 == 0)
                    EntangleLogger.Warn("[Tick] " + name + " failed (" + count + "x): " + e.Message);
            }
            Entanglement.Network.NetStats.AddCost(name, System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }

        public override void OnFixedUpdate() {
            if (SteamIntegration.isInvalid) return;

            Entanglement.Network.NetStats.MarkFixed();

            // PD gains must refresh before any Drive/InterpolateRemote this step.
            SafeTick("PDController", PDController.OnFixedUpdate);

            SafeTick("ModuleHandler.Fixed", ModuleHandler.FixedUpdate);

            // Updates the VRIK of all the players
            SafeTick("UpdatePlayerReps", PlayerRepresentation.UpdatePlayerReps);

            SafeTick("DriveRepPhysics", PlayerRepresentation.DriveRepPhysics);

            SafeTick("SoloDebug", Entanglement.Objects.SoloDebug.FixedTick);

        }

        public override void OnLateUpdate() {
            if (SteamIntegration.isInvalid) return;
            
            SafeTick("ModuleHandler.Late", ModuleHandler.LateUpdate);

            SafeTick("Client.Tick", () => Client.instance?.Tick());
            SafeTick("Server.Tick", () => Server.instance?.Tick());

            // This will run steam's callbacks and pump incoming packets
            SafeTick("SteamIntegration", SteamIntegration.Tick);

            SafeTick("PresentPlayerReps", PlayerRepresentation.PresentPlayerReps);
            // Other players' holstered items go where their rep's body is now.
            SafeTick("HolsterSync", Entanglement.Objects.HolsterSync.Tick);
            SafeTick("SlowMo.Apply", Entanglement.Network.SlowMoSync.Apply);
            SafeTick("GunSlide", Entanglement.Patching.GunSlideSync.Tick);

            Entanglement.Network.NetStats.MarkLateEnd();
        }

        static int loadsWhileWaiting;

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) {
            if (SteamIntegration.isInvalid) return;

            // Loading is over, give the frame budget back to rendering
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.BelowNormal;
            QualitySettings.asyncUploadTimeSlice = 2;
            QualitySettings.asyncUploadBufferSize = 4;

            if (EntangleLogger.verbose)
                EntangleLogger.Debug("[Scene] ---- " + sceneName + " (" + buildIndex + ") loaded ---- " + DebugLog.Snapshot());

            ModuleHandler.OnSceneWasInitialized(buildIndex, sceneName);

            if (SteamIntegration.hasLobby)
                SpawnableData.EnsureLobbyUnlocks();
            else
                SpawnableData.GetData();

            PlayerScripts.GetPlayerScripts();
            try { PlayerDeathManager.Initialize(); }
            catch (Exception e) { EntangleLogger.Warn("PlayerDeathManager init: " + e.Message); }
            Entanglement.Objects.CorpseCleanup.Clear();
            Entanglement.Objects.TurretSync.OnSceneCleanup();
            Entanglement.Patching.ManipulatorSync.OnSceneCleanup();
            Entanglement.Patching.TurretSight.OnSceneCleanup();
            Entanglement.Objects.LargePropSync.OnSceneCleanup();
            Entanglement.Objects.BossClawSync.OnSceneCleanup();
            Entanglement.Objects.RepCollisionGuard.OnSceneCleanup();
            Entanglement.Patching.TransformMirrorPatch.OnSceneCleanup();
            Entanglement.Objects.HeldGripGuard.OnSceneCleanup();
            Entanglement.Patching.ZoneTrackingUtilities.OnSceneCleanup();
            Entanglement.Patching.InstantiatePatch.OnSceneCleanup();
            Entanglement.Sync.CustomItemSync.OnSceneCleanup();
            Entanglement.Objects.SpawnCatchUp.OnSceneCleanup();
            Entanglement.Patching.BreakSync.OnSceneCleanup();
            Entanglement.Objects.NpcTrace.OnSceneCleanup();
            Entanglement.Objects.RemoteZonePresence.OnSceneCleanup();
            Entanglement.Objects.LocalZoneWatch.OnSceneCleanup();
            Entanglement.Objects.NpcDespawnSync.OnSceneCleanup();
            Entanglement.Objects.NpcConfigSync.OnSceneCleanup();
            Entanglement.Objects.NpcPoolGuard.OnSceneCleanup();
            Entanglement.Objects.RepTargetEmulator.OnSceneCleanup();
            Entanglement.Objects.SoloDebug.OnSceneCleanup();
            Entanglement.Patching.StoryInteractSync.OnSceneCleanup();
            Entanglement.Network.SlowMoSync.OnSceneLoaded();
            SceneEventSync.OnSceneReady();
            // Before anyone has knocked anything about; see BreakPieces.
            try { Entanglement.Objects.BreakPieces.CaptureHomes(); }
            catch (Exception e) { EntangleLogger.Warn("BreakPieces.CaptureHomes: " + e.Message); }

            PlayerRepresentation.GetPlayerTransforms();
            menuWait = 180;
            uiUnlocked = false;
            EntanglementRadial.OnSceneWasInitialized();
            GameRadial.OnSceneWasInitialized();
            DownloadUI.OnSceneWasInitialized();
            GameNotify.OnSceneWasInitialized();
            GamemodeManager.OnSceneLoad();

            foreach (var rep in PlayerRepresentation.representations.Values)
                rep.RecreateRepresentations();

            Client.instance.currentScene = (byte)buildIndex;

            bool waitingForTarget = Client.instance.pendingScene >= 0 && Client.instance.pendingScene != buildIndex;
            bool hostOverlay = Node.isServer
                && Patching.LevelChangeAnnouncer.levelAnnounced
                && Patching.LevelChangeAnnouncer.lastAnnouncedIndex != buildIndex;
            bool overlay = waitingForTarget || hostOverlay;

            if (overlay) {
                loadsWhileWaiting++;
                if (loadsWhileWaiting >= 2) {
                    EntangleLogger.Warn("[Scene] Still waiting for scene " + Client.instance.pendingScene
                        + " after " + loadsWhileWaiting + " loads; syncing to " + buildIndex + " instead.");
                    Client.instance.pendingScene = -1;
                    Patching.LevelChangeAnnouncer.levelAnnounced = false;
                    overlay = false;
                }
            }
            else
                loadsWhileWaiting = 0;

            if (!overlay) {
                Client.instance.pendingScene = -1;
                Patching.ClientSceneGate.Clear();
                hostLoading = false;
                if (!Patching.LevelChangeAnnouncer.ConsumeAnnounce(buildIndex)
                    && !Entanglement.Sync.CustomMapSync.HasCurrentMap)
                    sceneChange = (byte)buildIndex;

                if (SteamIntegration.hasLobby && !Node.isServer && Node.activeNode != null) {
                    NetworkMessage readyMessage = NetworkMessage.CreateMessage(BuiltInMessageType.ClientReady, new EmptyMessageData());
                    if (readyMessage != null)
                        Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, readyMessage.GetBytes());
                }
            }

            Client.TryCommandLineJoin();

            SteamIntegration.targetScene = sceneName.ToLower();
            SteamIntegration.UpdateActivity();

            if (SteamIntegration.hasLobby) {
                Entanglement.Objects.TurretSync.OnSceneReady();
                Entanglement.Objects.NpcPoolGuard.OnSceneReady();
                Entanglement.Objects.LargePropSync.OnSceneReady();
            }
        }

        public override void BONEWORKS_OnLoadingScreen() {
            if (SteamIntegration.isInvalid) return;

            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
            QualitySettings.asyncUploadTimeSlice = 8;
            QualitySettings.asyncUploadBufferSize = 16;

            ModuleHandler.OnLoadingScreen();

            hostLoading = SteamIntegration.hasLobby && Node.isServer;

            LoadingScreen.OverrideScreen();

            ObjectSync.OnCleanup();
            ObjectSync.poolPairs.Clear();
            SceneEventSync.OnSceneCleanup();
            Entanglement.Objects.TurretSync.OnSceneCleanup();
            Entanglement.Patching.ManipulatorSync.OnSceneCleanup();
            Entanglement.Patching.TurretSight.OnSceneCleanup();
            Entanglement.Objects.LargePropSync.OnSceneCleanup();
            Entanglement.Objects.BossClawSync.OnSceneCleanup();
            Entanglement.Objects.RepCollisionGuard.OnSceneCleanup();
            Entanglement.Patching.TransformMirrorPatch.OnSceneCleanup();
            Entanglement.Objects.HeldGripGuard.OnSceneCleanup();
            Entanglement.Patching.ZoneTrackingUtilities.OnSceneCleanup();
            Entanglement.Patching.InstantiatePatch.OnSceneCleanup();
            Entanglement.Sync.CustomItemSync.OnSceneCleanup();
            Entanglement.Objects.SpawnCatchUp.OnSceneCleanup();
            Entanglement.Patching.BreakSync.OnSceneCleanup();
            Entanglement.Objects.NpcTrace.OnSceneCleanup();
            Entanglement.Objects.RemoteZonePresence.OnSceneCleanup();
            Entanglement.Objects.LocalZoneWatch.OnSceneCleanup();
            Entanglement.Objects.NpcDespawnSync.OnSceneCleanup();
            Entanglement.Objects.NpcConfigSync.OnSceneCleanup();
            Entanglement.Objects.NpcPoolGuard.OnSceneCleanup();
            Entanglement.Objects.RepTargetEmulator.OnSceneCleanup();
            Entanglement.Objects.SoloDebug.OnSceneCleanup();
            Entanglement.Patching.StoryInteractSync.OnSceneCleanup();
            Entanglement.Patching.TriggerRelay.OnSceneCleanup();
            Entanglement.Objects.SceneNpcSync.OnSceneCleanup();
            Entanglement.Objects.PooleeSyncable.OnSceneCleanup();
            Entanglement.Objects.NpcPending.OnSceneCleanup();
            Entanglement.Objects.NpcOwnerWatch.OnSceneCleanup();
            TransformSyncBatcher.Clear();
            Server.instance?.replayedUsers.Clear();
            // Do not Clear file transfers here - downloads must survive door/level loads.

        }

        public override void OnApplicationQuit() {
            if (SteamIntegration.isInvalid) return;

            ModuleHandler.OnApplicationQuit();

            Node.activeNode.Shutdown();
            NetworkLayerManager.Shutdown();
            SteamIntegration.Shutdown();
        }
    }
}
