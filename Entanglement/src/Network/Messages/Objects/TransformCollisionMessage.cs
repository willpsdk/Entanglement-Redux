using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Objects;

using UnityEngine;

#if DEBUG
using MelonLoader;
#endif

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class TransformCollisionMessageHandler : NetworkMessageHandler<TransformCollisionMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.TransformCollision;

        // Objects another player has in a body slot, which have their collisions off here, and whose.
        static readonly Dictionary<ushort, long> holstered = new Dictionary<ushort, long>();

        public static void Clear() => holstered.Clear();

        public static void ReleaseHolder(long holder) {
            List<ushort> theirs = new List<ushort>();
            foreach (KeyValuePair<ushort, long> pair in holstered)
                if (pair.Value == holder)
                    theirs.Add(pair.Key);
            for (int i = 0; i < theirs.Count; i++) {
                holstered.Remove(theirs[i]);
                Syncable syncable;
                if (ObjectSync.TryGetSyncable(theirs[i], out syncable)) {
                    TransformSyncable sync = syncable as TransformSyncable;
                    if (sync && sync.rb)
                        sync.rb.detectCollisions = true;
                }
            }
            HolsterSync.ReleaseHolder(holder);
        }

        static bool Holsterable(TransformSyncable sync) {
            try {
                if (sync.GetComponentInChildren<StressLevelZero.Props.Weapons.WeaponSlot>(true) != null
                    || sync.GetComponentInParent<StressLevelZero.Props.Weapons.WeaponSlot>() != null)
                    return true;
                // A spawned item's slot can sit on another of its bodies.
                Transform root = sync.transform.root;
                return root && StressLevelZero.Pool.Poolee.Cache.Get(root.gameObject)
                    && root.GetComponentInChildren<StressLevelZero.Props.Weapons.WeaponSlot>(true) != null;
            }
            catch { return true; }
        }

        public override NetworkMessage CreateMessage(TransformCollisionMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(ushort) + sizeof(byte) + sizeof(long)];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.objectId), ref index);

            message.messageData[index++] = Convert.ToByte(data.enabled);

            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.holder), ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            ushort objectId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            bool enabled = Convert.ToBoolean(message.messageData[index++]);

            // Whose slot. What the host passes on says; a client only speaks for itself.
            long holder = sender;
            if (Server.instance == null && message.messageData.Length >= index + sizeof(long)) {
                long named = BitConverter.ToInt64(message.messageData, index);
                if (named != 0)
                    holder = named;
            }

            if (ObjectSync.TryGetSyncable(objectId, out Syncable syncable)) {
                if (syncable is TransformSyncable) {
                    TransformSyncable syncObj = (TransformSyncable)syncable;

                    if (enabled) {
                        if (syncObj.rb)
                            syncObj.rb.detectCollisions = true;
                        holstered.Remove(objectId);
                        HolsterSync.Unholstered(objectId);
                    }
                    else if (!Holsterable(syncObj)) {
                        // Ids got crossed somewhere; a door or crate losing its collisions is far worse.
                        EntangleLogger.Debug("[Holster] Ignored a slot message for " + syncObj.name + ", which can't go in a slot");
                    }
                    else {
                        if (syncObj.rb)
                            syncObj.rb.detectCollisions = false;
                        holstered[objectId] = holder;
                        HolsterSync.Holstered(syncObj, holder);
                    }
                }
            }

            if (Server.instance != null) {
                NetworkMessage relay = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCollision,
                    new TransformCollisionMessageData() { objectId = objectId, enabled = enabled, holder = holder });
                if (relay != null)
                    Server.instance.BroadcastMessageExcept(NetworkChannel.Object, relay.GetBytes(), sender);
            }
        }
    }

    public class TransformCollisionMessageData : NetworkMessageData
    {
        public ushort objectId;
        public bool enabled;     // false: it went into a body slot
        public long holder;      // whose slot
    }
}
