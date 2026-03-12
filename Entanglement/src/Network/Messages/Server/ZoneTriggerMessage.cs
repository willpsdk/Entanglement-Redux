using System.Text;
using UnityEngine;
using StressLevelZero.Zones;
using Entanglement.Patching;
using Entanglement.Network;
using Entanglement.Representation;
using Entanglement.Data; // Added the correct namespace for PlayerScripts
using Entanglement.Extensions;

namespace Entanglement.Network
{
    public class ZoneTriggerMessageHandler : NetworkMessageHandler<ZoneTriggerMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ZoneTrigger;

        public override NetworkMessage CreateMessage(ZoneTriggerMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            // format: isPlayerTrigger|isEnter|zoneStateId|fullPath
            string payload = $"{(data.isPlayerTrigger ? 1 : 0)}|{(data.isEnter ? 1 : 0)}|{data.zoneStateId}|{data.zonePath}";
            message.messageData = Encoding.UTF8.GetBytes(payload);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            string payload = Encoding.UTF8.GetString(message.messageData);

            bool isPlayerTrigger = false;
            bool isEnter = true;
            byte zoneStateId = (byte)Entanglement.Managers.ZoneSyncManager.ZoneStateId.Active;
            string zonePath = payload;

            string[] split = payload.Split(new[] { '|' }, 4);
            if (split.Length == 4)
            {
                isPlayerTrigger = split[0] == "1";
                isEnter = split[1] == "1";
                byte.TryParse(split[2], out zoneStateId);
                zonePath = split[3];
            }
            else if (split.Length == 3)
            {
                isPlayerTrigger = split[0] == "1";
                isEnter = split[1] == "1";
                zonePath = split[2];
            }

            Entanglement.Managers.ZoneSyncManager.ApplyRemoteZoneState(zonePath, isPlayerTrigger, zoneStateId);

            Transform targetTransform = zonePath.GetFromFullPath();
            GameObject targetZone = targetTransform != null ? targetTransform.gameObject : GameObject.Find(zonePath);

            if (targetZone != null)
            {
                SceneZone zone = targetZone.GetComponent<SceneZone>();
                PlayerTrigger trigger = targetZone.GetComponent<PlayerTrigger>();

                if (isPlayerTrigger && trigger == null)
                    return;
                if (!isPlayerTrigger && zone == null)
                    return;

                if (zone != null || trigger != null)
                {
                    if (PlayerScripts.playerRig != null && PlayerScripts.playerRig.physicsRig != null)
                    {
                        Collider playerCol = null;

                        if (PlayerScripts.playerRig.physicsRig.m_pelvis != null)
                            playerCol = PlayerScripts.playerRig.physicsRig.m_pelvis.GetComponent<Collider>();

                        if (playerCol == null && PlayerScripts.playerRig.physicsRig.m_head != null)
                            playerCol = PlayerScripts.playerRig.physicsRig.m_head.GetComponent<Collider>();

                        if (playerCol != null)
                        {
                            ZoneTrackingUtilities.networkReplay = true;
                            try
                            {
                                if (isPlayerTrigger)
                                {
                                    if (isEnter)
                                        trigger.OnTriggerEnter(playerCol);
                                    else
                                        trigger.OnTriggerExit(playerCol);
                                }
                                else if (zone != null)
                                {
                                    if (isEnter)
                                        zone.OnTriggerEnter(playerCol);
                                    else
                                        zone.OnTriggerExit(playerCol);
                                }
                            }
                            finally
                            {
                                ZoneTrackingUtilities.networkReplay = false;
                            }
                        }
                    }

                    PlayerRepresentation.ForceRefreshAllRemoteRepresentations();
                }
            }
        }
    }

    public class ZoneTriggerMessageData : NetworkMessageData
    {
        public string zonePath;
        public bool isPlayerTrigger;
        public bool isEnter;
        public byte zoneStateId = (byte)Entanglement.Managers.ZoneSyncManager.ZoneStateId.Active;
    }
}