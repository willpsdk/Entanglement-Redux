using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Entanglement.Objects; // Added to access ObjectSync

namespace Entanglement.Network
{
    public class LevelChangeMessageHandler : NetworkMessageHandler<LevelChangeMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.LevelChange;

        public override NetworkMessage CreateMessage(LevelChangeMessageData data)
        {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[] { data.sceneIndex, Convert.ToByte(data.sceneReload) };
            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            try
            {
                if (message.messageData.Length < 2)
                {
                    EntangleLogger.Error("[LevelChange] Received invalid level change message - data too short");
                    throw new IndexOutOfRangeException("Level change message data is too short");
                }

                byte index = message.messageData[0];
                bool reload = Convert.ToBoolean(message.messageData[1]);

                if (Client.instance == null)
                {
                    EntangleLogger.Warn("[LevelChange] Received level change but Client.instance is null, ignoring");
                    return;
                }

                if (index == Client.instance.currentScene && !reload)
                {
                    EntangleLogger.Verbose($"[LevelChange] Already in scene {index}, skipping load");
                    return;
                }

                EntangleLogger.Log($"[LevelChange] Client received level change to scene {index} (reload: {reload})", ConsoleColor.Cyan);

                Client.instance.currentScene = index;

                // FIX 1: LOCK DOWN THE NETWORK
                // We lock the physics network and purge the cache instantly before the loading screen even appears.
                ObjectSync.isTransitioning = true;
                ObjectSync.OnCleanup();

                try
                {
                    StressLevelZero.Utilities.BoneworksSceneManager.LoadScene(index);
                    
                    // FIX 2: Start the handshake coroutine to wait for the scene to load
                    MelonCoroutines.Start(WaitForSceneLoad(index));
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LevelChange] Failed to load scene {index}: {ex.Message}\n{ex.StackTrace}");
                    ObjectSync.isTransitioning = false; // Emergency unlock if load fails
                    throw;
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"[LevelChange] Error handling level change message: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // FIX 3: The Handshake. Wait until Unity finishes unpacking the map before opening the floodgates.
        private static IEnumerator WaitForSceneLoad(byte targetScene)
        {
            yield return null; // Wait a frame for load to initiate
            
            // Wait until the actual Unity Scene index matches our target scene
            while (SceneManager.GetActiveScene().buildIndex != targetScene)
            {
                yield return null;
            }

            // Add a critical 2.5-second buffer to let Boneworks initialize the physical RigManager and object pools
            yield return new WaitForSeconds(2.5f);

            // UNLOCK THE NETWORK!
            ObjectSync.isTransitioning = false;
            EntangleLogger.Log("[LevelChange] Level loaded completely. Network lock lifted!", ConsoleColor.Green);
        }
    }

    public class LevelChangeMessageData : NetworkMessageData
    {
        public byte sceneIndex;
        public bool sceneReload;
    }
}