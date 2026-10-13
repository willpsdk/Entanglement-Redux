using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using Entanglement.Data;
using Entanglement.Representation;
using Entanglement.Extensions;

using StressLevelZero;

namespace Entanglement.Network
{
    public enum PlayerEventType {
        Death = 0,
        Respawn = 1,
        // Starting to load a level. Their rep fades out now instead of freezing until it goes quiet.
        Loading = 2,
    }

    [Net.SkipHandleOnLoading]
    public class PlayerEventMessageHandler : NetworkMessageHandler<PlayerEventMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PlayerEvent;

        /// <summary>Tells everyone something just happened to the local player.</summary>
        public static void Send(PlayerEventType type) {
            if (Node.activeNode == null || !SteamIntegration.hasLobby)
                return;

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerEvent,
                new PlayerEventMessageData() { type = type });
            if (message != null)
                Node.activeNode.BroadcastMessageP2P(NetworkChannel.Reliable, message.GetBytes());
        }

        public override NetworkMessage CreateMessage(PlayerEventMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[1] { (byte)data.type };

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            PlayerEventType type = (PlayerEventType)message.messageData[0];

            switch (type) {
                case PlayerEventType.Death:
                    // One body per death, even if the message arrives twice.
                    float last;
                    if (lastDeath.TryGetValue(sender, out last) && Time.realtimeSinceStartup - last < DeathRepeatWindow)
                        break;
                    lastDeath[sender] = Time.realtimeSinceStartup;

                    PlayerRepresentation rep;
                    if (PlayerRepresentation.representations.TryGetValue(sender, out rep) && rep != null) {
                        if (!rep.Parked && rep.CreateRagdoll())
                            rep.HideWhileDead();
                    }
                    break;

                case PlayerEventType.Respawn:
                    PlayerRepresentation back;
                    if (PlayerRepresentation.representations.TryGetValue(sender, out back) && back != null)
                        back.ShowAfterDeath();
                    break;

                case PlayerEventType.Loading:
                    PlayerRepresentation leaving;
                    if (PlayerRepresentation.representations.TryGetValue(sender, out leaving) && leaving != null)
                        leaving.FadeOutForLoad(true);
                    break;
            }
        }

        const float DeathRepeatWindow = 2f;
        static readonly System.Collections.Generic.Dictionary<long, float> lastDeath = new System.Collections.Generic.Dictionary<long, float>();
    }

    public class PlayerEventMessageData : NetworkMessageData
    {
        public PlayerEventType type;
    }
}
