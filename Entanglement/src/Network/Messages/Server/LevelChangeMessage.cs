using System;

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
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            byte index = message.messageData[0];
            bool reload = Convert.ToBoolean(message.messageData[1]);

            if (index == Client.instance.currentScene && !reload)
                return;

            // FIX: Ensure the client updates their tracked scene so they don't get stuck in transition
            Client.instance.currentScene = index;

            StressLevelZero.Utilities.BoneworksSceneManager.LoadScene(index);
        }
    }

    public class LevelChangeMessageData : NetworkMessageData
    {
        public byte sceneIndex;
        public bool sceneReload;
    }
}