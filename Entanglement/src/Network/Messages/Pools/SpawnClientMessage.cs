using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;

using Entanglement.Data;
using Entanglement.Exceptions;
using Entanglement.Extensions;
using Entanglement.Objects;

using StressLevelZero.Pool;
using StressLevelZero.Data;

using UnityEngine;

using MelonLoader;

namespace Entanglement.Network
{
    [Net.HandleOnLoaded]
    public class SpawnClientMessageHandler : NetworkMessageHandler<SpawnClientMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.SpawnClient;

        public override NetworkMessage CreateMessage(SpawnClientMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] utf8 = Encoding.UTF8.GetBytes(data.title);
            message.messageData = new byte[sizeof(byte) + sizeof(ushort) + utf8.Length + SimplifiedTransform.size];

            int index = 0;
            message.messageData[index++] = data.rbCount;

            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.spawnId), ref index);

            byte[] transformBytes = data.transform.GetBytes();
            for (int i = 0; i < SimplifiedTransform.size; i++)
                message.messageData[index++] = transformBytes[i];

            for (int i = 0; i < utf8.Length; i++)
                message.messageData[index++] = utf8[i];

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            if (!Node.isServer)
            {
                int index = 0;
                byte rbCount = message.messageData[index++];

                ushort id = BitConverter.ToUInt16(message.messageData, index);
                index += sizeof(ushort);

                byte[] transformBytes = new byte[SimplifiedTransform.size];
                for (int i = 0; i < transformBytes.Length; i++)
                    transformBytes[i] = message.messageData[index++];

                int remaining = message.messageData.Length - index;
                byte[] stringBytes = new byte[remaining];
                for (int i = 0; i < remaining; i++)
                    stringBytes[i] = message.messageData[index++];

                string title = Encoding.UTF8.GetString(stringBytes);
                SimplifiedTransform transform = SimplifiedTransform.FromBytes(transformBytes);

#if DEBUG
                EntangleLogger.Log($"Received Object Spawn for Spawnable {title}!");
#endif

                NpcPending.BeginBuild(id);
                MelonCoroutines.Start(RegisterAndSpawn(title, transform.position, transform.rotation.ExpandQuat(), id, rbCount));
            }
            else
                throw new ExpectedClientException();
        }

        internal static IEnumerator RegisterAndSpawn(string title, Vector3 position, Quaternion rotation, ushort id, byte rbCount)
        {
            bool handedToReuse = false;
            bool creatingHere = false;
            try {
                PooleeSyncable existing;
                if (PooleeSyncable._PooleeLookup.TryGetValue(id, out existing)) {
                    bool usable = false;
                    try { usable = existing && existing.gameObject; }
                    catch { usable = false; }

                    if (usable) {
                        bool active = existing.gameObject.activeInHierarchy;
                        existing.OnSpawn(SteamIntegration.lobbyOwnerId, new SimplifiedTransform(position, rotation), id);
                        handedToReuse = true;
                        if (Patching.InstantiatePatch.IsSceneNpc(existing.gameObject))
                            NpcTrace.Log("spawn #" + id + " " + title + " already here, " + (active ? "reset" : "brought back"));
                        yield break;
                    }
                }

                if (!NpcPending.TryBeginCreate(id)) {
                    NpcTrace.Log("spawn #" + id + " " + title + " is already being built here, not making a second copy");
                    yield break;
                }
                creatingHere = true;

                IEnumerator build = Create(title, position, rotation, id, rbCount);
                while (build.MoveNext())
                    yield return build.Current;
            }
            finally {
                if (creatingHere)
                    NpcPending.EndCreate(id);
                if (!handedToReuse)
                    NpcPending.EndBuild(id);
            }
        }

        static IEnumerator Create(string title, Vector3 position, Quaternion rotation, ushort id, byte rbCount)
        {
            SpawnableObject spawnable = SpawnableData.TryGetSpawnable(title);

            yield return null;

            yield return null;

            if (!spawnable) {
                NpcTrace.Log("spawn #" + id + " " + title + " is not a spawnable here, asking the host for it");
                Entanglement.Sync.CustomItemSync.RequestItemForPoolSpawn(title, position, rotation, id, rbCount);
                yield break;
            }

            Vector3 scale = spawnable.prefab.transform.localScale;

            Poolee poolee;
            SpawnManager.SpawnOverride = true;
            try {
                poolee = PoolManager.DynamicPools[title].InstantiatePoolee(position, rotation);
                poolee.transform.position = position;
                poolee.transform.rotation = rotation;
                poolee.transform.localScale = scale;
                poolee.transform.parent = null;
                poolee.gameObject.SetActive(true);
            }
            finally {
                SpawnManager.SpawnOverride = false;
            }

            Rigidbody[] rbs = PooleeSyncable.SyncBodies(poolee.gameObject);

            float recount = 0f;
            while (rbs.Length != rbCount && recount < 1f) {
                yield return null;
                recount += Time.unscaledDeltaTime;
                if (!poolee || !poolee.gameObject)
                    yield break;
                rbs = PooleeSyncable.SyncBodies(poolee.gameObject);
            }
            if (Patching.InstantiatePatch.IsSceneNpc(poolee.gameObject))
                NpcTrace.Log("spawned #" + id + " " + title + " bodies here=" + rbs.Length + " host=" + rbCount);
            int bind = rbs.Length;
            if (rbCount > 0)
                bind = Math.Min(rbs.Length, rbCount);
            if (rbs.Length != rbCount)
                EntangleLogger.Warn("[Spawn] " + title + " has " + rbs.Length + " bodies here but " + rbCount
                    + " on the host; binding the first " + bind);
            for (ushort i = 0; i < bind; i++) {
                Rigidbody rb = rbs[i];
                if (!rb)
                    continue;
                GameObject go = rb.gameObject;
                ushort thisId = (ushort)(i + id);

                TransformSyncable existingSync = TransformSyncable.cache.GetOrAdd(go);
                if (existingSync)
                {
                    ObjectSync.MoveSyncable(existingSync, thisId);
                    existingSync.ClearOwner();
                    existingSync.TrySetStale(SteamIntegration.lobbyOwnerId);
                }
                else
                {
                    TransformSyncable.CreateSync(SteamIntegration.lobbyOwnerId, ComponentCacheExtensions.m_RigidbodyCache.GetOrAdd(go), thisId);
                }

                ObjectSync.lastId = thisId;
            }

            if (rbCount == 0)
                ObjectSync.lastId = id;

            GameObject spawnedObject = poolee.gameObject;
            var pooleeSyncable = PooleeSyncable.Attach(spawnedObject);
            pooleeSyncable.AssignId(id, (byte)Math.Min((int)rbCount, 255));
            pooleeSyncable.BindTransforms(spawnedObject.GetComponentsInChildren<TransformSyncable>(true));

            if (Patching.InstantiatePatch.IsSceneNpc(spawnedObject))
                pooleeSyncable.RestartLikeHost();

            if (TurretSync.IsTurret(spawnedObject))
                TurretSync.DisableRemoteAi(spawnedObject);
        }
    }

    public class SpawnClientMessageData : NetworkMessageData
    {
        // The id of the spawned object (starting at first rigidbody)
        public ushort spawnId;

        // The amount of rigidbodies in the object (So we know if the object should be ignored or not)
        public byte rbCount;

        // The title of the spawn pool
        public string title;

        // The position and rotation of the spawned object
        public SimplifiedTransform transform;
    }
}
