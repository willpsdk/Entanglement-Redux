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
                    // Create a throwaway collider for the replay — doesn't belong to the player rig
                    GameObject phantom = new GameObject("ZoneReplayPhantom");
                    phantom.tag = "Player";
                    SphereCollider phantomCol = phantom.AddComponent<SphereCollider>();
                    phantomCol.radius = 0.01f;
                    phantomCol.isTrigger = true; // Prevents the dummy collider from pushing physical objects

                    // Move it to where the zone is so physics doesn't complain
                    phantom.transform.position = targetZone.transform.position;

                    // Temporarily bypass the patch tracking so the client actually runs the game's spawn method
                    ZoneTrackingUtilities.networkIgnore = true;

                    try
                    {
                        // Fire the zone trigger using the phantom collider instead of the local player's pelvis
                        zone.OnTriggerEnter(phantomCol);
                    }
                    finally
                    {
                        // Ensure networkIgnore is turned off and the phantom is destroyed even if an error occurs
                        ZoneTrackingUtilities.networkIgnore = false;
                        GameObject.Destroy(phantom);
                    }
                }
            }
        }
    }

    public class ZoneTriggerMessageData : NetworkMessageData
    {
        public string zonePath;
    }
}