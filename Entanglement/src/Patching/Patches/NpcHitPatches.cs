using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using UnityEngine;

using HarmonyLib;

using UnhollowerBaseLib;

using StressLevelZero.Combat;
using StressLevelZero.SFX;

using Entanglement.Data;
using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    // Whose doing a hit on an NPC is. The game builds an attack inside the striking object's own
    // collision code (a fist, a bat, a blade, an explosion) and hands it straight to the NPC, so the
    // patches below mark the striker while that code runs. The player who struck sends their hits
    // to whoever runs the NPC; that machine ignores what its copies of other players' things do.
    public static class HitSource
    {
        public enum Side : byte { Unknown, Local, Remote }

        public static Side current;

        static float nextFlush;

        public static void BeginFrame() {
            // A patched method that threw never reached its postfix.
            current = Side.Unknown;
            // Players join and leave mid-level; their bodies are worked out again now and then.
            float now = Time.unscaledTime;
            if (now >= nextFlush) {
                nextFlush = now + 20f;
                repBodies.Clear();
            }
        }

        public static void Begin(Component striker) {
            current = Side.Unknown;
            if (!SteamIntegration.hasLobby || !striker)
                return;
            try {
                Rigidbody body = striker.GetComponentInParent<Rigidbody>();
                current = body ? SideOf(body) : Side.Unknown;
            }
            catch { current = Side.Unknown; }
        }

        public static void End() => current = Side.Unknown;

        static readonly Dictionary<int, bool> repBodies = new Dictionary<int, bool>();

        public static void OnSceneCleanup() => repBodies.Clear();

        /// <summary>Part of another player's rep. Asked per contact, so worked out once per body.</summary>
        public static bool IsRepBody(Rigidbody body) {
            int key = body.GetInstanceID();
            bool rep;
            if (!repBodies.TryGetValue(key, out rep)) {
                rep = RepCollisionGuard.IsRepOwned(body);
                repBodies[key] = rep;
            }
            return rep;
        }

        /// <summary>Which player is behind a body: us, another player, or nobody (the level, an NPC).</summary>
        public static Side SideOf(Rigidbody body) {
            if (!body)
                return Side.Unknown;
            if (IsRepBody(body))
                return Side.Remote;

            TransformSyncable sync = TransformSyncable.cache.Get(body.gameObject);
            if (sync && !sync.aiPart && sync.staleOwner != 0L)
                return sync.staleOwner == SteamIntegration.currentUserId ? Side.Local : Side.Remote;

            StressLevelZero.Rig.RigManager rig = PlayerScripts.playerRig;
            if (rig && body.transform.IsChildOf(rig.transform))
                return Side.Local;
            return Side.Unknown;
        }

        /// <summary>An attack's proxy: ours, another player's, or neither.</summary>
        public static Side SideOf(IntPtr proxy) {
            if (proxy == IntPtr.Zero)
                return Side.Unknown;
            try {
                StressLevelZero.AI.TriggerRefProxy wrapped = new StressLevelZero.AI.TriggerRefProxy(proxy);
                if (RemoteShot.FromRep(wrapped))
                    return Side.Remote;
                if (BossClawSync.OwnerOf(wrapped) == SteamIntegration.currentUserId)
                    return Side.Local;
            }
            catch { }
            return Side.Unknown;
        }
    }

    // Where the fields of MuscleHit and Attack sit, read from the game's own class data.
    public static class HitLayout
    {
        public static int hitSize, muscleIndex, attack;
        public static int damage, normal, origin, force, collider, attackType, proxy;
        static bool read, ok;

        public static bool Ok {
            get {
                if (!read) {
                    read = true;
                    try {
                        IntPtr hitClass = Il2CppClassPointerStore<PuppetMasta.MuscleHit>.NativeClassPtr;
                        IntPtr attackClass = Il2CppClassPointerStore<Attack>.NativeClassPtr;
                        uint align = 0;
                        hitSize = IL2CPP.il2cpp_class_value_size(hitClass, ref align);
                        muscleIndex = GameCalls.FieldOffset(hitClass, "muscleIndex");
                        attack = GameCalls.FieldOffset(hitClass, "attack");
                        damage = GameCalls.FieldOffset(attackClass, "damage");
                        normal = GameCalls.FieldOffset(attackClass, "normal");
                        origin = GameCalls.FieldOffset(attackClass, "origin");
                        force = GameCalls.FieldOffset(attackClass, "force");
                        collider = GameCalls.FieldOffset(attackClass, "collider");
                        attackType = GameCalls.FieldOffset(attackClass, "attackType");
                        proxy = GameCalls.FieldOffset(attackClass, "proxy");
                        ok = hitSize > 0 && hitSize <= 256 && muscleIndex >= 0 && attack >= 0 && damage >= 0 && normal >= 0
                            && origin >= 0 && force >= 0 && collider >= 0 && attackType >= 0 && proxy >= 0
                            && attack + proxy + IntPtr.Size <= hitSize;
                    }
                    catch { ok = false; }
                    if (!ok)
                        EntangleLogger.Warn("[NpcHit] Could not read the attack layout; NPC hits stay local");
                }
                return ok;
            }
        }
    }

    // BehaviourBase.OnMuscleHit: every attack on an NPC passes through here (bullets, punches,
    // blades, blunt props, explosions) before its behaviour turns it into damage, a flinch and a
    // grudge against whoever did it. MuscleHit holds an Attack, so this is a native hook.
    public static class NpcHitHook
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void HitFn(IntPtr behaviour, IntPtr hit, IntPtr method);

        static readonly NativeHook hook = new NativeHook("BehaviourBase.OnMuscleHit");

        public static void Attach() => hook.Attach(typeof(PuppetMasta.BehaviourBase), "OnMuscleHit", new HitFn(OnMuscleHit));

        static unsafe void OnMuscleHit(IntPtr behaviour, IntPtr hit, IntPtr method) {
            try {
                if (SteamIntegration.hasLobby && hit != IntPtr.Zero && !RunsHere(behaviour, hit))
                    return;
            }
            catch (Exception e) { PatchGuard.Report("NpcHitHook", e); }
            Run(behaviour, hit);
        }

        static unsafe bool RunsHere(IntPtr behaviour, IntPtr hit) {
            PooleeSyncable npc = PooleeSyncable.OfBehaviour(behaviour);
            // Not synced: remote GunShot FX must not damage NPCs either.
            if (npc == null)
                return !RemoteShot.active;
            if (!HitLayout.Ok)
                return true;

            Entanglement.Entities.NpcEntity entity = Entanglement.Entities.NpcEntity.Of(npc);
            HitSource.Side side = RemoteShot.active ? HitSource.Side.Remote : HitSource.current;
            if (side == HitSource.Side.Unknown)
                side = HitSource.SideOf(*(IntPtr*)((byte*)hit + HitLayout.attack + HitLayout.proxy));

            // Someone else owns the entity: forward and skip local PuppetMaster damage.
            if (entity == null || !entity.RunsLocally) {
                if (side == HitSource.Side.Local)
                    npc.ForwardAttack(hit);
                return false;
            }

            return side != HitSource.Side.Remote;
        }

        /// <summary>The game's own OnMuscleHit, past the checks above.</summary>
        public static unsafe void Run(IntPtr behaviour, IntPtr hit) {
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = hit;
            hook.Invoke(behaviour, args);
        }
    }

    // BehaviourBase.OnMuscleCollision: an NPC body part touched something. The behaviour turns hard
    // contacts into stun and lost health (SubBehaviourHealth.Stun, which kills at zero) and an
    // active swing into melee damage. MuscleCollision holds a Collision, so this is a native hook.
    public static class NpcCollisionHook
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void CollisionFn(IntPtr behaviour, IntPtr collision, IntPtr method);

        static readonly NativeHook hook = new NativeHook("BehaviourBase.OnMuscleCollision");

        static int collisionField = -1;

        public static void Attach() => hook.Attach(typeof(PuppetMasta.BehaviourBase), "OnMuscleCollision", new CollisionFn(OnMuscleCollision));

        static unsafe void OnMuscleCollision(IntPtr behaviour, IntPtr collision, IntPtr method) {
            try {
                if (SteamIntegration.hasLobby && collision != IntPtr.Zero && !RunsHere(behaviour, collision))
                    return;
            }
            catch (Exception e) { PatchGuard.Report("NpcCollisionHook", e); }
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = collision;
            hook.Invoke(behaviour, args);
        }

        static unsafe bool RunsHere(IntPtr behaviour, IntPtr muscleCollision) {
            PooleeSyncable npc = PooleeSyncable.OfBehaviour(behaviour);
            if (npc == null)
                return true;
            // A copy that follows its owner: the owner's game has this contact.
            if (!npc.RunsHere)
                return false;

            if (collisionField < 0) {
                collisionField = GameCalls.FieldOffset(Il2CppClassPointerStore<PuppetMasta.MuscleCollision>.NativeClassPtr, "collision");
                if (collisionField < 0)
                    collisionField = int.MaxValue;
            }
            if (collisionField == int.MaxValue)
                return true;

            IntPtr contact = *(IntPtr*)((byte*)muscleCollision + collisionField);
            if (contact == IntPtr.Zero)
                return true;

            // Another player's held or thrown thing: their game sends the hit it made. Their body is
            // here, though, so its bumps and the NPC's swings at it play out as usual.
            Rigidbody other = new Collision(contact).rigidbody;
            if (!other)
                return true;
            // Another player's body brushing it. A rep's body is driven by forces and carries a hard
            // impulse for the lightest touch, which the contact handler turned into stun and lost
            // health: clients killed NPCs by touching them. Their real blows come from their own game
            // (NpcHit), so only the NPC's own swing at them is kept here.
            if (HitSource.IsRepBody(other))
                return npc.MeleeSwinging();
            return HitSource.SideOf(other) != HitSource.Side.Remote;
        }
    }

    // The game code that builds attacks, so the hit hook knows whose they are.
    [HarmonyPatch(typeof(ImpactSFX), "BluntAttack")]
    public static class BluntAttackSourcePatch
    {
        public static void Prefix(ImpactSFX __instance) => HitSource.Begin(__instance);
        public static void Postfix() => HitSource.End();
    }

    [HarmonyPatch(typeof(StabSlash), "ProcessCollision")]
    public static class StabSlashSourcePatch
    {
        public static void Prefix(StabSlash __instance) => HitSource.Begin(__instance);
        public static void Postfix() => HitSource.End();
    }

    [HarmonyPatch(typeof(StressLevelZero.Interaction.PowerPuncher), "OnCollisionEnter")]
    public static class PowerPuncherSourcePatch
    {
        public static void Prefix(StressLevelZero.Interaction.PowerPuncher __instance) => HitSource.Begin(__instance);
        public static void Postfix() => HitSource.End();
    }

    [HarmonyPatch(typeof(AttackExplosion), "Explosion")]
    public static class ExplosionSourcePatch
    {
        public static void Prefix(AttackExplosion __instance) => HitSource.Begin(__instance);
        public static void Postfix() => HitSource.End();
    }

    [HarmonyPatch(typeof(HeadSFX), "OnCollisionEnter")]
    public static class HeadbuttSourcePatch
    {
        public static void Prefix(HeadSFX __instance) => HitSource.Begin(__instance);
        public static void Postfix() => HitSource.End();
    }
}
