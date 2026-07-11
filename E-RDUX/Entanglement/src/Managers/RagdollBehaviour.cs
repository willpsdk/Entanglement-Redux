using System;
using System.Collections;

using UnityEngine;

using MelonLoader;

namespace Entanglement.Managers
{
    [RegisterTypeInIl2Cpp]
    public class RagdollBehaviour : MonoBehaviour
    {
        public RagdollBehaviour(IntPtr intPtr) : base(intPtr) { }

        public Rigidbody[] rbs;

        public const float idleDespawnSeconds = 120f;
        public const float activitySpeed = 0.25f;

        private float lastActivityTime;
        private float nextActivityCheck;
        private bool isDespawning;

        public void Start() {
            rbs = GetComponentsInChildren<Rigidbody>(true);
            lastActivityTime = Time.time;
        }

        public void FixedUpdate() {
            if (isDespawning)
                return;

            // Any body moving (grabbed, dragged, punched, shot) resets the idle timer,
            // the corpse only despawns after two minutes of lying untouched
            if (Time.time >= nextActivityCheck) {
                nextActivityCheck = Time.time + 0.5f;

                foreach (Rigidbody rb in rbs) {
                    if (!rb)
                        continue;

                    if (rb.velocity.sqrMagnitude > activitySpeed * activitySpeed || rb.angularVelocity.sqrMagnitude > 1f) {
                        lastActivityTime = Time.time;
                        break;
                    }
                }
            }

            if (Time.time - lastActivityTime >= idleDespawnSeconds) {
                isDespawning = true;
                MelonCoroutines.Start(Despawn());
            }
        }

        public IEnumerator Despawn() {
            transform.position = transform.GetChild(0).position;
            transform.GetChild(0).localPosition = Vector3.zero;

            foreach (Rigidbody rb in rbs) {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            float elapsed = 0f;
            while (elapsed < 1f) {
                elapsed += Time.deltaTime;

                transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, elapsed);

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
