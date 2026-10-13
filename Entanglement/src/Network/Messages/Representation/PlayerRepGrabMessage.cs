using System;

using Entanglement.Representation;

using StressLevelZero.Interaction;

using UnityEngine;

namespace Entanglement.Network
{
    /// <summary>
    /// Fusion-style networked grab of another player. Attacker holds victim; remotes soft-follow
    /// the grabber's hand so everyone sees the hold, not only the local grabber.
    /// </summary>
    [Net.SkipHandleOnLoading]
    public class PlayerRepGrabMessageHandler : NetworkMessageHandler<PlayerRepGrabMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PlayerRepGrab;

        public override NetworkMessage CreateMessage(PlayerRepGrabMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[] {
                SteamIntegration.GetByteId(data.grabberId),
                data.handedness,
                SteamIntegration.GetByteId(data.victimId),
                data.attachPoint,
            };
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < 4)
                return;

            long grabber = SteamIntegration.GetLongId(message.messageData[0]);
            if (grabber == 0)
                grabber = sender;
            byte hand = message.messageData[1];
            long victim = SteamIntegration.GetLongId(message.messageData[2]);
            byte attach = message.messageData[3];

            if (victim == 0 || grabber == 0)
                return;

            if (PlayerRepresentation.representations.TryGetValue(victim, out PlayerRepresentation rep) && rep != null)
                rep.ApplyRemoteGrab(grabber, hand, attach);
            else if (victim == SteamIntegration.currentUserId)
                PlayerRepresentation.ApplyRemoteGrabOnLocal(grabber, hand, attach);

            if (Node.isServer && Server.instance != null)
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, message.GetBytes(), sender);
        }
    }

    public class PlayerRepGrabMessageData : NetworkMessageData {
        public long grabberId;
        public byte handedness; // 0 left, 1 right
        public long victimId;
        public byte attachPoint; // 0 root, 1 head, 2 left hand, 3 right hand
    }

    [Net.SkipHandleOnLoading]
    public class PlayerRepReleaseMessageHandler : NetworkMessageHandler<PlayerRepReleaseMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.PlayerRepRelease;

        public override NetworkMessage CreateMessage(PlayerRepReleaseMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[] {
                SteamIntegration.GetByteId(data.grabberId),
                data.handedness,
                SteamIntegration.GetByteId(data.victimId),
            };
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < 3)
                return;

            long grabber = SteamIntegration.GetLongId(message.messageData[0]);
            if (grabber == 0)
                grabber = sender;
            byte hand = message.messageData[1];
            long victim = SteamIntegration.GetLongId(message.messageData[2]);

            if (PlayerRepresentation.representations.TryGetValue(victim, out PlayerRepresentation rep) && rep != null)
                rep.ClearRemoteGrab(grabber, hand);
            else if (victim == SteamIntegration.currentUserId)
                PlayerRepresentation.ClearRemoteGrabOnLocal(grabber, hand);

            if (Node.isServer && Server.instance != null)
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, message.GetBytes(), sender);
        }
    }

    public class PlayerRepReleaseMessageData : NetworkMessageData {
        public long grabberId;
        public byte handedness;
        public long victimId;
    }

    /// <summary>Local hooks that emit grab/release when you hold another PlayerRep.</summary>
    public static class PlayerRepGrabNet
    {
        static bool leftHeld;
        static bool rightHeld;
        static long leftVictim;
        static long rightVictim;

        public static void Tick() {
            if (!SteamIntegration.hasLobby || Node.activeNode == null)
                return;

            CheckHand(PlayerScriptsOrNullLeft(), true);
            CheckHand(PlayerScriptsOrNullRight(), false);
        }

        static Hand PlayerScriptsOrNullLeft() {
            try { return Entanglement.Data.PlayerScripts.playerLeftHand; }
            catch { return null; }
        }

        static Hand PlayerScriptsOrNullRight() {
            try { return Entanglement.Data.PlayerScripts.playerRightHand; }
            catch { return null; }
        }

        static void CheckHand(Hand hand, bool left) {
            bool held = false;
            long victim = 0;
            byte attach = 0;

            try {
                if (hand && hand.m_CurrentAttachedObject) {
                    Transform root = hand.m_CurrentAttachedObject.transform.root;
                    if (root && root.name.StartsWith("PlayerRep.", StringComparison.Ordinal)) {
                        string[] parts = root.name.Split('.');
                        if (parts.Length >= 2 && long.TryParse(parts[1], out victim) && victim != 0) {
                            held = true;
                            string n = hand.m_CurrentAttachedObject.name ?? "";
                            if (n.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0) attach = 1;
                            else if (n.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0) attach = 2;
                            else if (n.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0) attach = 3;
                        }
                    }
                }
            }
            catch { held = false; }

            bool was = left ? leftHeld : rightHeld;
            long wasVictim = left ? leftVictim : rightVictim;
            byte handByte = left ? (byte)0 : (byte)1;

            if (held && (!was || wasVictim != victim)) {
                if (was && wasVictim != 0 && wasVictim != victim)
                    SendRelease(handByte, wasVictim);
                SendGrab(handByte, victim, attach);
            }
            else if (!held && was && wasVictim != 0) {
                SendRelease(handByte, wasVictim);
            }

            if (left) { leftHeld = held; leftVictim = victim; }
            else { rightHeld = held; rightVictim = victim; }
        }

        static void SendGrab(byte hand, long victim, byte attach) {
            var msg = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerRepGrab, new PlayerRepGrabMessageData {
                grabberId = SteamIntegration.currentUserId,
                handedness = hand,
                victimId = victim,
                attachPoint = attach,
            });
            if (msg != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msg.GetBytes());
        }

        static void SendRelease(byte hand, long victim) {
            var msg = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerRepRelease, new PlayerRepReleaseMessageData {
                grabberId = SteamIntegration.currentUserId,
                handedness = hand,
                victimId = victim,
            });
            if (msg != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msg.GetBytes());
        }
    }
}
