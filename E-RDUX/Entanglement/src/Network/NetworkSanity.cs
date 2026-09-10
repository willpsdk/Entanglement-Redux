using UnityEngine;

namespace Entanglement.Network
{
    /// <summary>
    /// Clamps and validates networked physics state. Bad packets (NaN, insane speeds,
    /// far-out positions) are a common cause of "props to the moon" and rubberbanding.
    /// </summary>
    public static class NetworkSanity
    {
        public const float MaxPosition = 5000f;
        public const float MaxLinearSpeed = 80f;
        public const float MaxAngularSpeed = 80f;

        public static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        public static bool IsFinite(Quaternion q) =>
            !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
              float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));

        public static bool IsInWorld(Vector3 position) =>
            IsFinite(position) && position.magnitude <= MaxPosition;

        public static Vector3 ClampLinearVelocity(Vector3 velocity) {
            if (!IsFinite(velocity))
                return Vector3.zero;
            return Vector3.ClampMagnitude(velocity, MaxLinearSpeed);
        }

        public static Vector3 ClampAngularVelocity(Vector3 angularVelocity) {
            if (!IsFinite(angularVelocity))
                return Vector3.zero;
            return Vector3.ClampMagnitude(angularVelocity, MaxAngularSpeed);
        }

        public static bool SanitizePose(ref Vector3 position, ref Quaternion rotation, ref Vector3 velocity, ref Vector3 angularVelocity) {
            if (!IsInWorld(position) || !IsFinite(rotation))
                return false;

            // Normalize broken quaternions instead of dropping the whole packet when possible
            if (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
                rotation = Quaternion.identity;
            else
                rotation = rotation.normalized;

            velocity = ClampLinearVelocity(velocity);
            angularVelocity = ClampAngularVelocity(angularVelocity);
            return true;
        }

        /// <summary>
        /// Host validates the peer is the current owner. Clients accept owner OR host-relayed
        /// packets (client BroadcastMessage only goes to the host, who rebroadcasts).
        /// </summary>
        public static bool IsAuthorizedSyncSender(long sender, long staleOwner) {
            if (staleOwner == 0)
                return true;

            if (sender == staleOwner)
                return true;

            // Host-relayed to clients: P2P sender becomes the lobby owner
            if (Server.instance == null && sender == SteamIntegration.lobbyOwnerId)
                return true;

            return false;
        }
    }
}
