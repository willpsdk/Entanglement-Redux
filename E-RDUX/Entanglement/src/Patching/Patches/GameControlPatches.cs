using HarmonyLib;

using System;

using UnityEngine.SceneManagement;

using StressLevelZero.Utilities;

using Entanglement.Network;

namespace Entanglement.Patching {
    // This patch removes annoying scene reloads which break Entanglement until level reload
    [HarmonyPatch(typeof(GameControl), "RELOADLEVEL")]
    public static class ReloadLevelPatch {
        public static bool Prefix() {
            if (SteamIntegration.hasLobby)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Announces the host's level change the moment loading STARTS instead of after it finishes,
    /// so clients load in parallel with the host and the total transition takes one load, not two.
    /// </summary>
    public static class LevelChangeAnnouncer {
        public static bool levelAnnounced = false;
        public static int lastAnnouncedIndex = -1;

        public static void Announce(int sceneBuildIndex) {
            if (!SteamIntegration.hasLobby || !Node.isServer)
                return;

            // LoadScene(string) funnels into LoadScene(int), don't announce the same load twice
            if (levelAnnounced && lastAnnouncedIndex == sceneBuildIndex)
                return;

            LevelChangeMessageData levelChangeData = new LevelChangeMessageData() {
                sceneIndex = (byte)sceneBuildIndex,
                sceneReload = sceneBuildIndex == BoneworksSceneManager.currentSceneIndex
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.LevelChange, levelChangeData);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            levelAnnounced = true;
            lastAnnouncedIndex = sceneBuildIndex;

            EntangleLogger.Log($"Announced level change to scene {sceneBuildIndex} early, clients now load in parallel!");
        }

        // Returns true when this scene load was already broadcast early, so the old post-load send is skipped
        public static bool ConsumeAnnounce(int buildIndex) {
            bool announced = levelAnnounced && lastAnnouncedIndex == buildIndex;

            levelAnnounced = false;
            lastAnnouncedIndex = -1;

            return announced;
        }

        public static int ResolveSceneIndex(string sceneName) {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++) {
                string path = SceneUtility.GetScenePathByBuildIndex(i);

                if (!string.IsNullOrEmpty(path) && path.EndsWith($"/{sceneName}.unity", StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }

    [HarmonyPatch(typeof(BoneworksSceneManager), "LoadScene", new Type[] { typeof(int) })]
    public static class LoadSceneIndexPatch {
        public static void Prefix(int sceneBuildIndex) => LevelChangeAnnouncer.Announce(sceneBuildIndex);
    }

    [HarmonyPatch(typeof(BoneworksSceneManager), "LoadScene", new Type[] { typeof(string) })]
    public static class LoadSceneNamePatch {
        public static void Prefix(string sceneName) {
            int index = LevelChangeAnnouncer.ResolveSceneIndex(sceneName);

            if (index >= 0)
                LevelChangeAnnouncer.Announce(index);
        }
    }
}
