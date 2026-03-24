using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Objects;
using Entanglement.Patching;

using StressLevelZero.Pool;
using StressLevelZero.Interaction;

using UnityEngine;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class MagazinePlugMessageHandler : NetworkMessageHandler<MagazinePlugMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.MagazinePlug;

        public override NetworkMessage CreateMessage(MagazinePlugMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            // FIX: Expand the byte array to hold an integer for the ammo count (4 bytes)
            message.messageData = new byte[sizeof(ushort) * 2 + sizeof(bool) + sizeof(int)];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.magId), ref index);

            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.gunId), ref index);

            message.messageData[index++] = Convert.ToByte(data.isInsert);

            // FIX: Serialize the exact ammo count
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.ammoCount), ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
#if DEBUG
            EntangleLogger.Log("Received mag sync message!");
#endif

            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            ushort magId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            ushort gunId = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            if (ObjectSync.TryGetSyncable(magId, out Syncable magazine) && ObjectSync.TryGetSyncable(gunId, out Syncable gun)) {
                TransformSyncable syncMag = magazine.TryCast<TransformSyncable>();
                TransformSyncable syncGun = gun.TryCast<TransformSyncable>();

                if (syncMag && syncGun) {

                    if (syncMag._CachedPlug && syncGun._CachedGun) {

                        bool isInsert = Convert.ToBoolean(message.messageData[index++]);
                        
                        // FIX: Deserialize the exact ammo count
                        int ammoCount = BitConverter.ToInt32(message.messageData, index);
                        index += sizeof(int);

                        MagazineSocket magSocket = syncGun._CachedGun.magazineSocket;

                        if (isInsert) {
                            // FIX: Force overwrite the physical bullet count before inserting!
                            // This ensures late-joiners or dropped guns don't generate ghost bullets.
                            // Use reflection to set cartridgeStates
                            Entanglement.Objects.MagazineReflectionHelper.SetCartridgeStates(syncMag._CachedPlug.magazine, ammoCount);
                            syncMag._CachedPlug.InsertPlug(magSocket);

#if DEBUG
                            EntangleLogger.Log($"Inserting a magazine with exactly {ammoCount} bullets!");
#endif
                        }
                        else {
                            syncMag._CachedPlug.ForceEject();

#if DEBUG
                            EntangleLogger.Log("Trying to eject a magazine!");
#endif
                        }
                    }
                }
            }

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
            }
        }
    }

    public class MagazinePlugMessageData : NetworkMessageData
    {
        public ushort magId;
        public ushort gunId;
        public bool isInsert = true;
        // FIX: Add ammo count variable to message data
        public int ammoCount = 0; 
    }
}