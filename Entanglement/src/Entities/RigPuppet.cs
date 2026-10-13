using Entanglement.Representation;

using UnityEngine;

namespace Entanglement.Entities
{
    /// <summary>
    /// Fusion RigPuppet for Boneworks: owns the physics-rig lifecycle on a remote
    /// <see cref="PlayerRepresentation"/>. Boneworks has no RigManager — PhysicsRigPuppet is the body.
    /// </summary>
    public sealed class RigPuppet
    {
        PlayerRepresentation _rep;

        public PlayerRepresentation Representation => _rep;
        public PhysicsRigPuppet Physics => _rep != null ? _rep.PhysicsPuppet : null;
        public bool HasPuppet => Physics != null && Physics.IsValid;

        public Rigidbody Pelvis => Physics != null ? Physics.Pelvis : null;
        public Rigidbody Chest => Physics != null ? Physics.Chest : null;
        public Rigidbody LeftHand => Physics != null ? Physics.LeftHand : null;
        public Rigidbody RightHand => Physics != null ? Physics.RightHand : null;
        public Rigidbody LeftFoot => Physics != null ? Physics.LeftFoot : null;
        public Rigidbody RightFoot => Physics != null ? Physics.RightFoot : null;
        public Collider HeadCollider => Physics != null ? Physics.HeadCollider : null;
        public Collider TorsoCollider => Physics != null ? Physics.TorsoCollider : null;

        public void Attach(PlayerRepresentation rep) {
            _rep = rep;
        }

        public void Detach() {
            _rep = null;
        }

        /// <summary>Create / ensure the physical puppet exists (Fusion CreatePuppet).</summary>
        public void CreatePuppet() {
            if (_rep == null)
                return;
            try { _rep.EnsureEnemyTarget(); }
            catch (System.Exception e) {
                EntangleLogger.Warn("[RigPuppet] CreatePuppet: " + e.Message);
            }
        }

        public void DestroyPuppet() {
            if (_rep == null)
                return;
            try { _rep.TeardownPhysicsPuppet(); }
            catch (System.Exception e) {
                EntangleLogger.Warn("[RigPuppet] DestroyPuppet: " + e.Message);
            }
        }

        /// <summary>Physics step: ensure puppet, PD-drive all segments from Ford pose.</summary>
        public void OnFixedUpdate() {
            if (_rep == null)
                return;
            try {
                if (!HasPuppet)
                    CreatePuppet();
                _rep.DriveEnemyTarget();
            }
            catch (System.Exception e) {
                EntangleLogger.Warn("[RigPuppet] FixedUpdate: " + e.Message);
            }
        }
    }
}
