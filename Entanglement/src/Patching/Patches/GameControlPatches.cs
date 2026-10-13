using HarmonyLib;

using System;

using UnityEngine;
using UnityEngine.SceneManagement;

using StressLevelZero.Utilities;

using Entanglement.Network;
using Entanglement.UI;

namespace Entanglement.Patching {
    [HarmonyPatch(typeof(GameControl), "RELOADLEVEL")]
    public static class ReloadLevelPatch {
        public static bool Prefix() => !SteamIntegration.hasLobby;
    }

    [HarmonyPatch(typeof(GameControl), "NEXTLEVEL")]
    public static class NextLevelPatch {
        public static bool Prefix() {
            if (!SteamIntegration.hasLobby || Node.isServer)
                return true;
            GameNotify.Show("Only the host can change the map.", 3f);
            return false;
        }
    }

    public static class ClientSceneGate {
        static bool networkLoadPending;

        public static bool IsNetworkLoadPending => networkLoadPending;

        public static void AllowNetworkLoad() {
            networkLoadPending = true;
        }

        public static void Clear() {
            networkLoadPending = false;
        }

        public static bool CanLoadScene() {
            if (!SteamIntegration.hasLobby || Node.isServer)
                return true;

            if (networkLoadPending)
                return true;

            GameNotify.Show("Only the host can change the map.", 3f);
            return false;
        }
    }

    public static class LevelChangeAnnouncer {
        public static bool levelAnnounced = false;
        public static int lastAnnouncedIndex = -1;

        public static void Announce(int sceneBuildIndex) {
            if (!SteamIntegration.hasLobby || !Node.isServer)
                return;

            if (Entanglement.Sync.CustomMapSync.ConsumeMapShellAnnounce())
                return;

            Entanglement.Sync.CustomMapSync.OnHostSceneAnnounced();

            if (levelAnnounced && lastAnnouncedIndex == sceneBuildIndex)
                return;

            levelAnnounced = true;
            lastAnnouncedIndex = sceneBuildIndex;
            BroadcastNow(sceneBuildIndex);
        }

        static void BroadcastNow(int sceneBuildIndex) {
            if (Server.instance == null)
                return;

            try {
                LevelChangeMessageData data = new LevelChangeMessageData() {
                    sceneIndex = (byte)sceneBuildIndex,
                    sceneReload = true,
                };
                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.LevelChange, data);
                if (message == null)
                    return;

                byte[] msgBytes = message.GetBytes();
                foreach (long user in Server.instance.connectedUsers)
                    Server.instance.SendMessage(user, NetworkChannel.Reliable, msgBytes);

                EntangleLogger.Log("Notifying clients of scene change to " + sceneBuildIndex + "...");
            }
            catch (Exception e) {
                EntangleLogger.Warn("Failed to notify clients of scene change: " + e.Message);
            }
        }

        public static bool ConsumeAnnounce(int buildIndex) {
            if (!levelAnnounced)
                return false;

            if (lastAnnouncedIndex != buildIndex)
                return true;

            levelAnnounced = false;
            lastAnnouncedIndex = -1;
            return true;
        }

        public static int ResolveSceneIndex(string sceneName) {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++) {
                string path = SceneUtility.GetScenePathByBuildIndex(i);

                if (!string.IsNullOrEmpty(path) && path.EndsWith("/" + sceneName + ".unity", StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }

    [HarmonyPatch(typeof(BoneworksSceneManager), "LoadScene", new Type[] { typeof(int) })]
    public static class LoadSceneIndexPatch {
        public static bool Prefix(int sceneBuildIndex) {
            if (!ClientSceneGate.CanLoadScene())
                return false;

            LevelChangeAnnouncer.Announce(sceneBuildIndex);
            LevelFade.Starting();
            return true;
        }
    }

    [HarmonyPatch(typeof(BoneworksSceneManager), "LoadScene", new Type[] { typeof(string) })]
    public static class LoadSceneNamePatch {
        public static bool Prefix(string sceneName) {
            if (!ClientSceneGate.CanLoadScene())
                return false;

            int index = LevelChangeAnnouncer.ResolveSceneIndex(sceneName);

            if (index >= 0)
                LevelChangeAnnouncer.Announce(index);

            LevelFade.Starting();
            return true;
        }
    }

    [HarmonyPatch(typeof(BoneworksSceneManager), "LoadNext")]
    public static class LoadNextPatch {
        public static bool Prefix() {
            if (!SteamIntegration.hasLobby || Node.isServer) {
                LevelFade.Starting();
                return true;
            }
            if (ClientSceneGate.IsNetworkLoadPending) {
                LevelFade.Starting();
                return true;
            }
            GameNotify.Show("Only the host can change the map.", 3f);
            return false;
        }
    }

    public static class LevelFade {
        public static void Starting() {
            try { Entanglement.Representation.PlayerRepresentation.OnLocalLoadStarting(); }
            catch (Exception e) { EntangleLogger.Debug("[Rep] Level fade: " + e.Message); }
        }
    }
}
