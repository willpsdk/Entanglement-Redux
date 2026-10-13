using Entanglement.Representation;

using UnityEngine;

namespace Entanglement.Entities
{
    /// <summary>
    /// Boneworks-shaped Fusion RigPuppet: wraps <see cref="PhysicsRigPuppet"/> (PD pelvis/hands/feet)
    /// on a remote <see cref="PlayerRepresentation"/>. Boneworks has no RigManager — this is the equivalent.
    /// </summary>
    public sealed class RigPuppet
    {
        PlayerRepresentation _rep;

        public PlayerRepresentation Representation => _rep;
        public PhysicsRigPuppet Physics => _rep != null ? _rep.PhysicsPuppet : null;
        public bool HasPuppet => Physics != null && Physics.IsValid;

        public Rigidbody Pelvis => Physics != null ? Physics.Pelvis : null;
        public Rigidbody LeftHand => Physics != null ? Physics.LeftHand : null;
        public Rigidbody RightHand => Physics != null ? Physics.RightHand : null;
        public Rigidbody LeftFoot => Physics != null ? Physics.LeftFoot : null;
        public Rigidbody RightFoot => Physics != null ? Physics.RightFoot : null;

        public void Attach(PlayerRepresentation rep) {
            _rep = rep;
        }

        public void Detach() {
            _rep = null;
        }

        /// <summary>Physics step: ensure the enemy-target puppet exists, then PD-drive it.</summary>
        public void OnFixedUpdate() {
            if (_rep == null)
                return;
            try {
                _rep.EnsureEnemyTarget();
                _rep.DriveEnemyTarget();
            }
            catch (System.Exception e) {
                EntangleLogger.Warn("[RigPuppet] FixedUpdate: " + e.Message);
            }
        }
    }
}
