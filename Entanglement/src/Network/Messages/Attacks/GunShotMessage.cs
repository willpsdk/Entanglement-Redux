using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using StressLevelZero.Combat;
using StressLevelZero.Pool;

using Entanglement.Data;
using Entanglement.Representation;
using Entanglement.Extensions;

namespace Entanglement.Network
{
    /// <summary>
    /// Visual gun FX only (projectile, muzzle, SFX). Player damage is unified through
    /// <see cref="Combat.NetworkCombat"/> / PlayerAttack — never applied from this message.
    /// </summary>
    [Net.SkipHandleOnLoading]
    public class GunShotMessageHandler : NetworkMessageHandler<GunShotMessageData> {
        public override byte? MessageIndex => BuiltInMessageType.GunShot;

        public override NetworkMessage CreateMessage(GunShotMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(byte) * 4 + sizeof(short) * 3 + SimplifiedTransform.size];

            int index = 0;
            // User
            message.messageData[index++] = SteamIntegration.GetByteId(data.userId);
            // Ammo Variables
            AmmoVariables variables = data.bulletObject.ammoVariables;
            // Cartridge
            message.messageData[index++] = (byte)variables.cartridgeType;
            // Type
            message.messageData[index++] = (byte)variables.AttackType;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes((ushort)Math.Min(variables.AttackDamage * 100f, ushort.MaxValue)), ref index);
            // Mass
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes((short)(variables.ProjectileMass * 10000f)), ref index);
            // Tracer
            message.messageData[index++] = Convert.ToByte(variables.Tracer);
            // Velocity
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes((short)variables.ExitVelocity), ref index);
            // Transform
            message.messageData = message.messageData.AddBytes(data.bulletTransform.GetBytes(), ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            // User
            long userId = SteamIntegration.GetLongId(message.messageData[index++]);
            //Cartridge
            Cart cartridgeType = (Cart)message.messageData[index++];
            // Type
            AttackType attackType = (AttackType)message.messageData[index++];
            // Damage - matches the unsigned-short encoding above
            float attackDamage = (float)BitConverter.ToUInt16(message.messageData, index) / 100f;
            index += sizeof(ushort);
            // Mass
            float projectileMass = (float)BitConverter.ToInt16(message.messageData, index) / 10000f;
            index += sizeof(short);
            // Tracer
            bool tracer = Convert.ToBoolean(message.messageData[index++]);
            // Velocity
            float exitVelocity = BitConverter.ToInt16(message.messageData, index);
            index += sizeof(short);
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
            PlayerRepresentation rep;
            PlayerRepresentation.representations.TryGetValue(userId, out rep);
            StressLevelZero.AI.TriggerRefProxy shooter = null;
            try { shooter = rep != null ? rep.EnemyProxy : null; }
            catch { shooter = null; }

            PoolSpawner.SpawnProjectile(position, rotation, bulletObj, "1911", shooter);
            PoolSpawner.SpawnMuzzleFlare(position, rotation, PoolSpawner.MuzzleFlareType.Default);
            // Play Sound
            if (rep != null) {
                try {
                    if (rep.repGunSFX) {
                        bulletTransform.Apply(rep.repGunSFX.transform);
                        rep.repGunSFX.GunShot();
                    }
                }
                catch (Exception e) {
                    // Sound is cosmetic; never let it cost the shot or the host relay below.
                    EntangleLogger.Warn("[GunShot] Rep gun SFX failed: " + e.Message);
                }
            }

            if (Server.instance != null)
            {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Attack, msgBytes, userId);
            }
        }
    }

    public class GunShotMessageData : NetworkMessageData {
        public long userId;
        public BulletObject bulletObject;
        public SimplifiedTransform bulletTransform;
    }
}
