using System;

using UnityEngine;

using StressLevelZero.Combat;
using StressLevelZero.Interaction;

using Entanglement.Network;
using Entanglement.Network.Combat;
using Entanglement.Representation;
using Entanglement.Extensions;
using Entanglement.Gamemodes;

using HarmonyLib;

namespace Entanglement.Patching
{
    [HarmonyPatch(typeof(PowerPuncher), "OnCollisionEnter")]
    public class GadgetPatches
    {
        public static void Prefix(PowerPuncher __instance, Collision collision) {
            if (!SteamIntegration.hasLobby || !collision.rigidbody)
                return;

            Transform root = collision.gameObject.transform.root;
            string objName = root.name;
            if (!objName.Contains("PlayerRep"))
                return;
            string[] playerName = objName.Split('.');
            if (playerName.Length < 2)
                throw new IndexOutOfRangeException();
            long id = long.Parse(playerName[1]);
            if (!GamemodeManager.CanAttack(SteamIntegration.currentUserId, id))
                return;

            ContactPoint contact = collision.GetContact(0);
            Collider striker = contact.thisCollider;
            if (!striker || !striker.transform.IsChildOf(__instance.transform))
                return;
            Vector3 relative = collision.relativeVelocity;
            if (relative.sqrMagnitude < 0.01f)
                return;
            float dot = Vector3.Dot(relative.normalized, __instance.transform.TransformDirection(__instance.forward));
            dot = Mathf.Min(0f, dot);
            Vector3 force = relative * dot * __instance._triggerStartTime;
            force = Vector3.ClampMagnitude(force, 30f) * 7f;

            if (force == Vector3.zero)
                return;

            float damage = Mathf.Clamp(force.magnitude * 0.35f, 8f, 55f);
            float impulse = Mathf.Clamp(force.magnitude, 5f, 100f);

            // Unified combat path (same as punch/gun/stab).
            NetworkCombat.SendPlayerAttack(id, AttackType.Blunt, damage, 2, impulse);

            // FX-only follow-up (dust / SFX) — no second damage.
            if (PlayerRepresentation.syncedRoot) {
                NetworkMessage fx = NetworkMessage.CreateMessage((byte)BuiltInMessageType.PowerPunch, new PowerPunchMessageData()
                {
                    force = force,
                    localPosition = PlayerRepresentation.syncedRoot.InverseTransformPosition(__instance.transform.position)
                });
                if (fx != null)
                    Node.activeNode.SendMessage(id, NetworkChannel.Attack, fx.GetBytes());
            }
        }
    }
}
