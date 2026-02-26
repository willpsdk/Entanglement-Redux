using System.Text;
using UnityEngine;
using StressLevelZero.Props;
using Entanglement.Network;

namespace Entanglement.Network
{
    public class MapObjectDestroyMessageHandler : NetworkMessageHandler<MapObjectDestroyMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.MapObjectDestroy;

        public override NetworkMessage CreateMessage(MapObjectDestroyMessageData data)
        {
            NetworkMessage message = new NetworkMessage();
            byte[] pathBytes = Encoding.UTF8.GetBytes(data.objectPath);
            message.messageData = pathBytes;
            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            string objectPath = Encoding.UTF8.GetString(message.messageData);
            GameObject targetObject = GameObject.Find(objectPath);

            if (targetObject != null)
            {
                // Destroy the map prop locally (like a lock or plank)
                Prop_Health health = targetObject.GetComponent<Prop_Health>();
                if (health != null) health.DESTROYED();

                ObjectDestructable destructable = targetObject.GetComponent<ObjectDestructable>();
                if (destructable != null && !destructable._isDead) destructable.TakeDamage(Vector3.zero, 9999f, false, StressLevelZero.Combat.AttackType.Piercing);
            }
        }
    }

    public class MapObjectDestroyMessageData : NetworkMessageData
    {
        public string objectPath;
    }
}