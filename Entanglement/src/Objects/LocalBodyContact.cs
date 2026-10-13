using System;
using System.Collections.Generic;

using UnityEngine;

using MelonLoader;

using Entanglement.Data;
using Entanglement.Network;
using Entanglement.Patching;

namespace Entanglement.Objects
{
    [RegisterTypeInIl2Cpp]
    public class LocalBodyContact : MonoBehaviour
    {
        public LocalBodyContact(IntPtr intPtr) : base(intPtr) { }

        public void OnCollisionEnter(Collision collision) {
            try {
                if (!SteamIntegration.hasLobby)
                    return;
                Rigidbody other = collision.rigidbody;
                if (!other || other.isKinematic)
                    return;
                ObjectSync.OnBodyContact(other);
            }
            catch { }
        }

        public static void AttachTo(GameObject rigRoot) {
            if (!rigRoot)
                return;
            try {
                Rigidbody[] bodies = rigRoot.GetComponentsInChildren<Rigidbody>(true);
                int added = 0;
                for (int i = 0; i < bodies.Length; i++) {
                    Rigidbody rb = bodies[i];
                    if (!rb || rb.GetComponent<LocalBodyContact>())
                        continue;
                    rb.gameObject.AddComponent<LocalBodyContact>();
                    added++;
                }
                if (added > 0)
                    EntangleLogger.Debug("[BodyContact] Watching " + added + " body part(s) for pushes.");
            }
            catch (Exception e) { EntangleLogger.Warn("[BodyContact] Could not attach: " + e.Message); }
        }
    }
}
