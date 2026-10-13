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

        public static bool applying;
        static readonly Dictionary<ushort, float> replayedAt = new Dictionary<ushort, float>();
        const float ReplayWindow = 1f;

        public static bool Replayed(ushort magId) {
            float at;
            return magId != 0 && replayedAt.TryGetValue(magId, out at)
                && UnityEngine.Time.realtimeSinceStartup - at < ReplayWindow;
        }

        // After a replayed eject, the game's exit completing on its own is not news to send on.
        const float EjectQuiet = 2f;

        static System.Collections.IEnumerator CoEject(TransformSyncable syncMag, ushort magId) {
            float until = UnityEngine.Time.realtimeSinceStartup + 1.5f;
            while (UnityEngine.Time.realtimeSinceStartup < until) {
                yield return null;
                if (!syncMag || !syncMag._CachedPlug)
                    yield break;
                MagazinePlug plug = syncMag._CachedPlug;
                if (!plug.InGun() && !plug._isExitTransition)
                    yield break;
            }
            if (!syncMag || !syncMag._CachedPlug)
                yield break;
            if (!syncMag._CachedPlug.InGun() && !syncMag._CachedPlug._isExitTransition)
                yield break;

            EntangleLogger.Debug("[Mag] #" + magId + " did not leave the socket on its own; forcing it out");
            applying = true;
            replayedAt[magId] = UnityEngine.Time.realtimeSinceStartup + EjectQuiet;
            try { syncMag._CachedPlug.ForceEject(); }
            catch { }
            finally { applying = false; }
        }

        public override NetworkMessage CreateMessage(MagazinePlugMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(ushort) * 2 + sizeof(bool)];

            int index = 0;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.magId), ref index);

            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.gunId), ref index);

            message.messageData[index++] = Convert.ToByte(data.isInsert);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
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

                        EntangleLogger.Debug("[Mag] " + (isInsert ? "Insert of #" + magId + " into" : "Eject of #" + magId + " from")
                            + " gun #" + gunId + " from another player");

                        MagazineSocket magSocket = syncGun._CachedGun.magazineSocket;

                        applying = true;
                        replayedAt[magId] = UnityEngine.Time.realtimeSinceStartup;
                        try {
                            if (isInsert) {
                                syncMag._CachedPlug.InsertPlug(magSocket);

                                // Match the inserter: a seated mag belongs to whoever owns the gun.
                                if (syncGun.staleOwner != 0)
                                    syncMag.ForceOwner(syncGun.staleOwner, false);
                            }
                            else {
                                // The game's own eject, so its exit finishes properly (joint removed, plug
                                // state reset). The abrupt ForceEject left the plug's joint behind: the mag
                                // was pulled back to the socket and shook on the holder's side. The forced
                                // way is only the fallback (CoEject).
                                replayedAt[magId] = UnityEngine.Time.realtimeSinceStartup + EjectQuiet;
                                syncMag._CachedPlug.EjectPlug();
                                MelonLoader.MelonCoroutines.Start(CoEject(syncMag, magId));
                            }
                        }
                        finally { applying = false; }
                    }
                    else
                        EntangleLogger.Debug($"[Mag] Can't apply: #{magId} {syncMag.name} has no plug or #{gunId} {syncGun.name} is no gun here.");
                }
                else
                    EntangleLogger.Debug("[Mag] Can't apply: #" + magId + " or #" + gunId + " isn't a synced body here.");
            }
            else
                EntangleLogger.Debug("[Mag] Can't apply: mag #" + magId + " or gun #" + gunId + " isn't known here.");

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
    }
}
