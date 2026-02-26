using System.Text;
using UnityEngine;
using StressLevelZero.Zones;
using Entanglement.Patching;
using Entanglement.Network;
using Entanglement.Data; // Added the correct namespace for PlayerScripts

namespace Entanglement.Network
{
    public class ZoneTriggerMessageHandler : NetworkMessageHandler<ZoneTriggerMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ZoneTrigger;

        public override NetworkMessage CreateMessage(ZoneTriggerMessageData data)
        {
            NetworkMessage message = new NetworkMessage();
            message.messageData = Encoding.UTF8.GetBytes(data.zonePath);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            string zonePath = Encoding.UTF8.GetString(message.messageData);
            GameObject targetZone = GameObject.Find(zonePath);

            if (targetZone != null)
            {
                SceneZone zone = targetZone.GetComponent<SceneZone>();
                if (zone != null)
                {
                    // Temporarily bypass the patch tracking so the client actually runs the game's spawn method
                    ZoneTrackingUtilities.networkIgnore = true;

                    // FIX: Changed from StressLevelZero.Player.PlayerScripts to Entanglement.Data.PlayerScripts
                    if (PlayerScripts.playerRig != null)
                    {
                        Collider playerCol = PlayerScripts.playerRig.physicsRig.m_pelvis.GetComponent<Collider>();
                        zone.OnTriggerEnter(playerCol);
                    }

                    ZoneTrackingUtilities.networkIgnore = false;
                }
            }
        }
    }

    public class ZoneTriggerMessageData : NetworkMessageData
    {
        public string zonePath;
    }
}