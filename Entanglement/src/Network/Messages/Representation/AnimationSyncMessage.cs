using System;
using System.Collections.Generic;
using System.Linq;

using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Representation;

using UnityEngine;

namespace Entanglement.Network
{
    /// <summary>
    /// Syncs animator state and animation parameters for remote player representations.
    /// This complements PlayerRepSync by sending animation-specific parameters at 20 Hz.
    /// </summary>
    [Net.SkipHandleOnLoading]
    public class AnimationSyncMessageHandler : NetworkMessageHandler<AnimationSyncData>
    {
        public override byte? MessageIndex => BuiltInMessageType.AnimationSync;

        public override NetworkMessage CreateMessage(AnimationSyncData data)
        {
            NetworkMessage message = new NetworkMessage();
            List<byte> rawBytes = new List<byte>();

            // Serialize userId
            rawBytes.Add(SteamIntegration.GetByteId(data.userId));

            // Serialize animation parameters
            rawBytes.AddRange(BitConverter.GetBytes(data.movementSpeed));
            rawBytes.AddRange(BitConverter.GetBytes(data.jumpHeight));
            rawBytes.AddRange(BitConverter.GetBytes(data.animState));
            rawBytes.Add(data.isJumping ? (byte)1 : (byte)0);
            rawBytes.Add(data.isFalling ? (byte)1 : (byte)0);

            // Serialize body velocity for physics prediction
            rawBytes.AddRange(BitConverter.GetBytes(data.bodyVelocity.x));
            rawBytes.AddRange(BitConverter.GetBytes(data.bodyVelocity.y));
            rawBytes.AddRange(BitConverter.GetBytes(data.bodyVelocity.z));

            message.messageData = rawBytes.ToArray();
            return message;
        }

        public override void HandleMessage(NetworkMessage message, ulong sender)
        {
            try
            {
                if (message.messageData == null || message.messageData.Length <= 0)
                    return;

                int index = 0;
                ulong userId = SteamIntegration.GetLongId(message.messageData[index++]);

                if (PlayerRepresentation.representations.ContainsKey(userId))
                {
                    PlayerRepresentation rep = PlayerRepresentation.representations[userId];

                    if (rep != null && rep.repFord != null && rep.repAnimator != null)
                    {
                        // Deserialize animation parameters
                        float movementSpeed = BitConverter.ToSingle(message.messageData, index);
                        index += sizeof(float);

                        float jumpHeight = BitConverter.ToSingle(message.messageData, index);
                        index += sizeof(float);

                        int animState = BitConverter.ToInt32(message.messageData, index);
                        index += sizeof(int);

                        bool isJumping = Convert.ToBoolean(message.messageData[index++]);
                        bool isFalling = Convert.ToBoolean(message.messageData[index++]);

                        // Deserialize velocity
                        Vector3 bodyVelocity = new Vector3();
                        bodyVelocity.x = BitConverter.ToSingle(message.messageData, index);
                        index += sizeof(float);
                        bodyVelocity.y = BitConverter.ToSingle(message.messageData, index);
                        index += sizeof(float);
                        bodyVelocity.z = BitConverter.ToSingle(message.messageData, index);
                        index += sizeof(float);

                        // Apply animation state to the animator
                        rep.repAnimator.SetFloat("Speed", movementSpeed);
                        rep.repAnimator.SetInteger("AnimState", animState);

                        // Set jump/fall states
                        if (isJumping && !rep.isGrounded)
                        {
                            rep.repAnimator.SetBool("IsJumping", true);
                        }
                        else
                        {
                            rep.repAnimator.SetBool("IsJumping", false);
                        }

                        if (isFalling)
                        {
                            rep.repAnimator.SetBool("IsFalling", true);
                        }
                        else
                        {
                            rep.repAnimator.SetBool("IsFalling", false);
                        }

                        // Store velocity for physics prediction (can be used by ragdoll or physics sim)
                        rep.repSavedVel = bodyVelocity;
                    }
                }

                // Server broadcasts to other clients
                if (Server.instance != null)
                {
                    byte[] msgBytes = message.GetBytes();
                    Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, msgBytes, sender);
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error Handling AnimationSyncMessage: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Data structure for animation synchronization across network.
    /// Sent at ~20 Hz to avoid excessive bandwidth while maintaining smooth animations.
    /// </summary>
    public class AnimationSyncData : NetworkMessageData
    {
        public ulong userId;
        public float movementSpeed = 0f;        // Movement magnitude (0-10)
        public float jumpHeight = 0f;           // Jump trajectory height
        public int animState = 0;               // Animation state ID
        public bool isJumping = false;          // Currently jumping
        public bool isFalling = false;          // Currently falling
        public Vector3 bodyVelocity = Vector3.zero;  // Body velocity for physics prediction
    }
}
