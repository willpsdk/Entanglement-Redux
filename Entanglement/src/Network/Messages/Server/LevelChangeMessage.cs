using System;
using MelonLoader;

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

                // FIX: Null check to prevent crashes if client isn't fully initialized
                if (Client.instance == null)
                {
                    EntangleLogger.Warn("[LevelChange] Received level change but Client.instance is null, ignoring");
                    return;
                }

                // FIX: Only load if it's a different scene or a reload is requested
                if (index == Client.instance.currentScene && !reload)
                {
                    EntangleLogger.Verbose($"[LevelChange] Already in scene {index}, skipping load");
                    return;
                }

                EntangleLogger.Log($"[LevelChange] Client received level change to scene {index} (reload: {reload})", ConsoleColor.Cyan);

                // FIX: Update the client's tracked scene immediately to prevent race conditions
                Client.instance.currentScene = index;

                // FIX: Catch exceptions during scene load to prevent client crashes
                try
                {
                    StressLevelZero.Utilities.BoneworksSceneManager.LoadScene(index);
                }
                catch (Exception ex)
                {
                    EntangleLogger.Error($"[LevelChange] Failed to load scene {index}: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"[LevelChange] Error handling level change message: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    public class LevelChangeMessageData : NetworkMessageData
    {
        public byte sceneIndex;
        public bool sceneReload;
    }
}