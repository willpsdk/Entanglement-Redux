using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StressLevelZero.Props;
using StressLevelZero.Combat;

using Entanglement.Objects;
using Entanglement.Network;

using MelonLoader;

using HarmonyLib;

using UnityEngine;

namespace Entanglement.Patching {
    public static class BreakSync {
        // Set while a break from someone else is being applied, so it isn't sent straight back.
        public static bool applying;
        static readonly HashSet<int> sent = new HashSet<int>();

        public static void OnSceneCleanup() {
            sent.Clear();
            BreakPieces.OnSceneCleanup();
        }

        /// <summary>Whole again (a pooled crate reused): its next break is a new one.</summary>
        public static void Forget(Component whole) {
            try {
                if (whole)
                    sent.Remove(whole.gameObject.GetInstanceID());
            }
            catch { }
        }

        public static void Send(Component broken) {
            if (applying || !SteamIntegration.hasLobby || broken == null)
                return;

            // TakeDamage keeps being called on a dead object as debris hits it.
            if (!sent.Add(broken.gameObject.GetInstanceID()))
                return;

            SceneEventSync.SendEvent(SceneEventType.ObjectBreak, broken.transform, 0, true, SceneEventSync.SpotSuffix(broken.transform));
        }

        public static bool Apply(SceneEventType type, ushort arg, Transform target, bool replay = false) {
            if (type != SceneEventType.ObjectBreak)
                return false;

            applying = true;
            try {
                sent.Add(target.gameObject.GetInstanceID());

                ObjectDestructable destructable = target.GetComponent<ObjectDestructable>();
                if (destructable) {
                    if (!destructable._isDead) {
                        if (replay || !target.gameObject.activeInHierarchy) {
                            try { destructable.DestroyNoDebris(); }
                            catch { target.gameObject.SetActive(false); }
                        }
                        else {
                            destructable._health = 0f;
                            destructable.TakeDamage(Vector3.up, 10f, true, AttackType.None);
                        }
                        BreakPieces.NoteBroken(target);
                    }
                    return true;
                }

                Prop_Health health = target.GetComponent<Prop_Health>();
                if (health) {
                    if (BreakPieces.IsBroken(health))
                        return true;

                    if (replay || !target.gameObject.activeInHierarchy) {
                        if (!target.GetComponentInParent<StressLevelZero.Pool.Poolee>()) {
                            BreakPieces.MarkBroken(health);
                            try {
                                if (health.prop_Whole)
                                    health.prop_Whole.SetActive(false);
                            }
                            catch { }
                            target.gameObject.SetActive(false);
                        }
                        return true;
                    }

                    health.TIMEDKILL();
                    return true;
                }

                return true;
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Break] Could not break " + target.name + ": " + e.Message);
                return true;
            }
            finally {
                applying = false;
            }
        }
    }

    [HarmonyPatch(typeof(Prop_Health), "DESTROYED")]
    public static class PropHealthPatch {
        public static bool Prefix(Prop_Health __instance) {
            if (!__instance.impactSFX)
                return false;
            if (BreakPieces.IsBroken(__instance))
                return false;
            return true;
        }

        public static void Postfix(Prop_Health __instance) {
            try {
                BreakPieces.MarkBroken(__instance);
                BreakSync.Send(__instance);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("DestructablePatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Prop_Health), "SETPROP")]
    public static class PropHealthResetPatch {
        public static void Postfix(Prop_Health __instance) {
            try {
                BreakPieces.MarkWhole(__instance);
                BreakSync.Forget(__instance);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("DestructablePatches.ResetPostfix", e); }
        }
    }

    [HarmonyPatch(typeof(Prop_Health), "OnEnable")]
    public static class PropHealthEnablePatch {
        public static void Postfix(Prop_Health __instance) {
            try {
                if (!BreakPieces.IsBroken(__instance) || !__instance.GetComponentInParent<StressLevelZero.Pool.Poolee>())
                    return;
                BreakPieces.MarkWhole(__instance);
                BreakSync.Forget(__instance);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("DestructablePatches.EnablePostfix", e); }
        }
    }

    public static class RemoteShot {
        public static bool active;

        public static bool FromRep(StressLevelZero.AI.TriggerRefProxy proxy) {
            if (proxy == null)
                return false;
            foreach (Entanglement.Representation.PlayerRepresentation rep in Entanglement.Representation.PlayerRepresentation.representations.Values) {
                if (rep != null && rep.EnemyProxy == proxy)
                    return true;
            }
            return false;
        }
    }

    // The bullet moves and hits things in its own Update.
    [HarmonyPatch(typeof(Projectile), "Update")]
    public static class ProjectileUpdatePatch {
        public static void Prefix(Projectile __instance) {
            RemoteShot.active = false;
            try {
                if (SteamIntegration.hasLobby)
                    RemoteShot.active = RemoteShot.FromRep(__instance._proxy);
            }
            catch { RemoteShot.active = false; }
        }

        public static void Postfix() {
            RemoteShot.active = false;
        }
    }

    public static class FollowerImpacts {
        public static bool Ignore(Component target, Collision impact) {
            if (!SteamIntegration.hasLobby || !target)
                return false;
            try {
                Rigidbody body = target.GetComponentInParent<Rigidbody>();
                if (!body)
                    return false;
                TransformSyncable sync = TransformSyncable.cache.Get(body.gameObject);
                if (!sync || !sync.isValid || sync.IsOwner() || sync.staleOwner == 0)
                    return false;
                return !ByLocalPlayer(impact);
            }
            catch { return false; }
        }

        static bool ByLocalPlayer(Collision impact) {
            try {
                Rigidbody other = impact != null ? impact.rigidbody : null;
                if (!other)
                    return false;
                Transform t = other.transform;
                if (Entanglement.Data.PlayerScripts.playerRig && t.IsChildOf(Entanglement.Data.PlayerScripts.playerRig.transform))
                    return true;
                return Held(Entanglement.Data.PlayerScripts.playerLeftHand, t)
                    || Held(Entanglement.Data.PlayerScripts.playerRightHand, t);
            }
            catch { return false; }
        }

        static bool Held(StressLevelZero.Interaction.Hand hand, Transform t) {
            if (!hand)
                return false;
            Rigidbody held = Entanglement.Data.PlayerScripts.GetHeldObject(hand);
            if (!held)
                return false;
            Transform h = held.transform;
            return h == t || t.IsChildOf(h) || h.IsChildOf(t);
        }
    }

    [HarmonyPatch(typeof(ObjectDestructable), "OnSignificantCollision")]
    public static class DestructableImpactPatch {
        public static bool Prefix(ObjectDestructable __instance, Collision c) => !FollowerImpacts.Ignore(__instance, c);
    }

    [HarmonyPatch(typeof(Prop_Health), "OnReceivedCollision")]
    public static class PropHealthImpactPatch {
        public static bool Prefix(Prop_Health __instance, Collision impact) => !FollowerImpacts.Ignore(__instance, impact);
    }

    // OnReceivedAttack takes an Attack (see NativeHook). All it does is work out the damage and call
    // this, and nothing else reaches this during a bullet's update.
    [HarmonyPatch(typeof(Prop_Health), "TAKEDAMAGE")]
    public static class PropHealthRemoteShotPatch {
        public static bool Prefix() => !(SteamIntegration.hasLobby && RemoteShot.active);
    }

    [HarmonyPatch(typeof(ObjectDestructable), "TakeDamage")]
    public static class DestructablePatch {
        public static bool Prefix(ObjectDestructable __instance) {
            try {
                if (!SteamIntegration.hasLobby)
                    return true;
                if (RemoteShot.active)
                    return false;
                if (__instance._isDead || !__instance.gameObject.activeInHierarchy)
                    return false;
            }
            catch { }
            return true;
        }

        public static void Postfix(ObjectDestructable __instance, Vector3 normal, float damage, bool crit = false, AttackType attackType = default) {
            try {
                if (!SteamIntegration.hasLobby) return;

                if (!__instance._isDead) return;

                BreakPieces.NoteBroken(__instance.transform);
                BreakSync.Send(__instance);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("DestructablePatches.Postfix", e); }
        }
    }
}
