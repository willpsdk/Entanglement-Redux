using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using Entanglement.Data;
using Entanglement.Representation;
using Entanglement.Extensions;

using StressLevelZero;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class PlayerRepSyncHandler : NetworkMessageHandler<PlayerRepSyncData> {
        public override byte? MessageIndex => BuiltInMessageType.PlayerRepSync;

        // Scratch space so per-packet parsing doesn't allocate, this runs for every player every frame
        static readonly byte[] limbScratch = new byte[SimplifiedTransform.size_small];
        static readonly byte[] handScratch = new byte[SimplifiedHand.size];
        static readonly Vector3[] positionScratch = new Vector3[3];
        static readonly Quaternion[] rotationScratch = new Quaternion[3];

        public override NetworkMessage CreateMessage(PlayerRepSyncData data) {
            NetworkMessage message = new NetworkMessage();

            List<byte> rawBytes = new List<byte>();

            rawBytes.Add(SteamIntegration.GetByteId(data.userId));
            rawBytes.Add(Convert.ToByte(data.isGrounded));

            rawBytes.AddRange(data.rootPosition.GetBytes());

            for (int r = 0; r < data.simplifiedTransforms.Length; r++)
                rawBytes.AddRange(data.simplifiedTransforms[r].GetSmallBytes(data.rootPosition));

            rawBytes.AddRange(data.simplifiedLeftHand.GetBytes());
            rawBytes.AddRange(data.simplifiedRightHand.GetBytes());

            message.messageData = rawBytes.ToArray();

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            long userId = SteamIntegration.GetLongId(message.messageData[index++]);

            if (PlayerRepresentation.representations.ContainsKey(userId)) {
                PlayerRepresentation rep = PlayerRepresentation.representations[userId];

                if (rep.repFord) {

                    bool isGrounded = Convert.ToBoolean(message.messageData[index]);
                    index += sizeof(byte);
                    rep.isGrounded = isGrounded;

                    Vector3 rootPosition = new Vector3();

                    rootPosition.x = BitConverter.ToSingle(message.messageData, index);
                    index += sizeof(float);
                    rootPosition.y = BitConverter.ToSingle(message.messageData, index);
                    index += sizeof(float);
                    rootPosition.z = BitConverter.ToSingle(message.messageData, index);
                    index += sizeof(float);

                    // Buffer the targets, PlayerRepresentation.ApplyNetSmoothing glides toward them every tick
                    for (int r = 0; r < rep.repTransforms.Length; r++)
                    {
                        Buffer.BlockCopy(message.messageData, index, limbScratch, 0, SimplifiedTransform.size_small);
                        SimplifiedTransform simpleTransform = SimplifiedTransform.FromSmallBytes(limbScratch, rootPosition);
                        index += SimplifiedTransform.size_small;

                        positionScratch[r] = simpleTransform.position;
                        rotationScratch[r] = simpleTransform.rotation.ExpandQuat();
                    }

                    rep.SetNetTargets(rootPosition, positionScratch, rotationScratch);

                    Buffer.BlockCopy(message.messageData, index, handScratch, 0, SimplifiedHand.size);
                    SimplifiedHand simplifiedLeftHand = SimplifiedHand.FromBytes(handScratch);
                    index += SimplifiedHand.size;
                    Buffer.BlockCopy(message.messageData, index, handScratch, 0, SimplifiedHand.size);
                    SimplifiedHand simplifiedRightHand = SimplifiedHand.FromBytes(handScratch);

                    rep.UpdateFingers(Handedness.LEFT, simplifiedLeftHand);
                    rep.UpdateFingers(Handedness.RIGHT, simplifiedRightHand);
                }
            }

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Unreliable, msgBytes, userId);
            }
        }
    }

    public class PlayerRepSyncData : NetworkMessageData {
        public long userId;
        public bool isGrounded;
        public SimplifiedTransform[] simplifiedTransforms = new SimplifiedTransform[3];
        public Vector3 rootPosition;
        public SimplifiedHand simplifiedLeftHand;
        public SimplifiedHand simplifiedRightHand;
    }
}
