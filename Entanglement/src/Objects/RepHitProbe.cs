using System;

using UnityEngine;

using MelonLoader;

using PuppetMasta;

using StressLevelZero.AI;
using StressLevelZero.Pool;

namespace Entanglement.Objects
{
    /// <summary>
    /// On a rep's hit body. Reports when an NPC's attack actually lands on the rep.
    /// </summary>
    [RegisterTypeInIl2Cpp]
    public class RepHitProbe : MonoBehaviour
    {
        public RepHitProbe(IntPtr intPtr) : base(intPtr) { }

        public bool isTestDummy;
        float nextReport;

        public void OnCollisionEnter(Collision collision) {
            try {
                Rigidbody rb = collision.rigidbody;
                if (!rb)
                    return;

                AIBrain brain = FindBrain(rb);
                if (!brain || brain.isDead)
                    return;

                BehaviourBaseNav nav = brain.behaviour;
                if (nav == null)
                    return;

                ReportHit(brain, nav, collision);
            }
            catch { }
        }

        static AIBrain FindBrain(Rigidbody rb) {
            Poolee poolee = rb.GetComponentInParent<Poolee>();
            if (poolee)
                return poolee.GetComponentInChildren<AIBrain>(true);

            // Scene-placed NPCs aren't pooled: the brain sits next to the PuppetMaster.
            PuppetMaster puppet = rb.GetComponentInParent<PuppetMaster>();
            if (puppet && puppet.transform.parent)
                return puppet.transform.parent.GetComponentInChildren<AIBrain>(true);
            return null;
        }

        void ReportHit(AIBrain brain, BehaviourBaseNav nav, Collision collision) {
            if (Time.time < nextReport)
                return;

            bool attacking = false;
            try { attacking = nav.meleeAttackActive; }
            catch { }
            if (!attacking)
                return;

            nextReport = Time.time + 1f;
            string npc = brain.transform.root.name;
            EntangleLogger.Debug("[Rep] " + npc + " landed an attack on " + transform.root.name
                + " (impulse " + collision.impulse.magnitude.ToString("F1") + ")");

            if (isTestDummy)
                Entanglement.UI.GameNotify.Show(npc + " hit the dummy.", 2f);
        }
    }
}
