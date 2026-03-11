using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

using UnityEngine;

using StressLevelZero.Combat;
using StressLevelZero.Pool;

using Entanglement.Data;
using Entanglement.Representation;
using Entanglement.Extensions;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class GunShotMessageHandler : NetworkMessageHandler<GunShotMessageData> {
        public override byte? MessageIndex => BuiltInMessageType.GunShot;

        public override NetworkMessage CreateMessage(GunShotMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(byte) * 4 + sizeof(float) * 3 + SimplifiedTransform.size];

            int index = 0;
            // User
            message.messageData[index++] = SteamIntegration.GetByteId(data.userId);
            // Ammo Variables
            AmmoVariables variables = data.bulletObject.ammoVariables;
            // Cartridge
            message.messageData[index++] = (byte)variables.cartridgeType;
            // Type
            message.messageData[index++] = (byte)variables.AttackType;
            // Damage
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(variables.AttackDamage), ref index);
            // Mass
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(variables.ProjectileMass), ref index);
            // Tracer
            message.messageData[index++] = Convert.ToByte(variables.Tracer);
            // Velocity
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(variables.ExitVelocity), ref index);
            // Transform
            message.messageData = message.messageData.AddBytes(data.bulletTransform.GetBytes(), ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            // User
            ulong userId = SteamIntegration.GetLongId(message.messageData[index++]);
            //Cartridge
            Cart cartridgeType = (Cart)message.messageData[index++];
            // Type
            AttackType attackType = (AttackType)message.messageData[index++];
            // Damage
            float attackDamage = BitConverter.ToSingle(message.messageData, index);
            index += sizeof(float);
            // Mass
            float projectileMass = BitConverter.ToSingle(message.messageData, index);
            index += sizeof(float);
            // Tracer
            bool tracer = Convert.ToBoolean(message.messageData[index++]);
            // Velocity
            float exitVelocity = BitConverter.ToSingle(message.messageData, index);
            index += sizeof(float);
            // Get Bullet Object
            BulletObject bulletObj = new BulletObject();
            AmmoVariables ammoVariables = new AmmoVariables();
            ammoVariables.cartridgeType = cartridgeType;
            ammoVariables.AttackType = attackType;
            ammoVariables.AttackDamage = attackDamage;
            ammoVariables.ProjectileMass = projectileMass;
            ammoVariables.Tracer = tracer;
            ammoVariables.ExitVelocity = exitVelocity;
            bulletObj.ammoVariables = ammoVariables;
            // Get Transform
            byte[] transformBytes = new byte[SimplifiedTransform.size];
            for (int i = 0; i < transformBytes.Length; i++)
                transformBytes[i] = message.messageData[index++];
            SimplifiedTransform bulletTransform = SimplifiedTransform.FromBytes(transformBytes);
            // Spawn Bullets
            Vector3 position = bulletTransform.position;
            Quaternion rotation = bulletTransform.rotation.ExpandQuat();
            PoolSpawner.SpawnProjectile(position, rotation, bulletObj, "1911", null);
            PoolSpawner.SpawnMuzzleFlare(position, rotation, PoolSpawner.MuzzleFlareType.Default);

            // Fallback deterministic NPC hit application for remote peers (Nullbody/Omni cases).
            ApplyNpcDamageFallback(position, rotation, attackDamage);
            // Play Sound
            if (PlayerRepresentation.representations.ContainsKey(userId)) {
                PlayerRepresentation rep = PlayerRepresentation.representations[userId];
                bulletTransform.Apply(rep.repGunSFX.transform);
                rep.repGunSFX.GunShot();
            }

            if (Server.instance != null)
            {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Attack, msgBytes, userId);
            }
        }

        private static void ApplyNpcDamageFallback(Vector3 position, Quaternion rotation, float attackDamage)
        {
            try
            {
                if (!Physics.Raycast(position, rotation * Vector3.forward, out RaycastHit hit, 400f))
                    return;

                if (hit.collider == null || hit.collider.transform == null)
                    return;

                Transform root = hit.collider.transform.root;
                if (root == null || root.gameObject == null)
                    return;

                string name = root.gameObject.name.ToLowerInvariant();
                if (!(name.Contains("null") || name.Contains("omni") || name.Contains("projector")))
                    return;

                Component[] components = root.gameObject.GetComponentsInChildren<Component>(true);
                foreach (Component component in components)
                {
                    if (component == null)
                        continue;

                    Type type = component.GetType();

                    MethodInfo takeDamage = type.GetMethod("TAKEDAMAGE", new[] { typeof(float) })
                                            ?? type.GetMethod("TakeDamage", new[] { typeof(float) })
                                            ?? type.GetMethod("ApplyDamage", new[] { typeof(float) });

                    if (takeDamage != null)
                    {
                        takeDamage.Invoke(component, new object[] { attackDamage });
                        return;
                    }
                }
            }
            catch { }
        }
    }

    public class GunShotMessageData : NetworkMessageData {
        public ulong userId;
        public BulletObject bulletObject;
        public SimplifiedTransform bulletTransform;
    }
}
