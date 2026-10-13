using System;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

using StressLevelZero.Interaction;
using StressLevelZero.Props.Weapons;

using FieldGravityGun = StressLevelZero.Interaction.GravityGun;
using ToolGravityGun = StressLevelZero.Props.Weapons.GravityGun;

using Entanglement.Network;
using Entanglement.Objects;
using Entanglement.Extensions;

namespace Entanglement.Patching
{
    public static class ManipulatorSync
    {
        const float PollInterval = 0.1f;
        static float nextPoll;

        class Held
        {
            public Component tool;
            // What the tool has hold of: the one object it grips, and anything held in its field.
            public readonly Dictionary<int, Rigidbody> grabbed = new Dictionary<int, Rigidbody>();
            public readonly Dictionary<int, Rigidbody> now = new Dictionary<int, Rigidbody>();
            public readonly List<int> gone = new List<int>();
        }

        const int MaxFieldObjects = 24;

        static readonly List<Held> held = new List<Held>();

        // Objects the range tools are currently holding, so they can be released again.
        static readonly Dictionary<GameObject, int> claimed = new Dictionary<GameObject, int>(new UnityComparer());

        public static void Register(Component tool) {
            if (tool == null)
                return;
            for (int i = 0; i < held.Count; i++) {
                if (ReferenceEquals(held[i].tool, tool))
                    return;
            }
            held.Add(new Held { tool = tool });
        }

        public static void Unregister(Component tool) {
            if (tool == null)
                return;
            for (int i = held.Count - 1; i >= 0; i--) {
                if (!ReferenceEquals(held[i].tool, tool))
                    continue;
                ReleaseAll(held[i]);
                held.RemoveAt(i);
            }
        }

        static void ReleaseAll(Held entry) {
            foreach (Rigidbody rb in entry.grabbed.Values)
                Release(rb);
            entry.grabbed.Clear();
        }

        static void Add(Held entry, Rigidbody rb) {
            try {
                if (rb != null && rb.gameObject != null)
                    entry.now[rb.GetInstanceID()] = rb;
            }
            catch { }
        }

        // The Dev Manipulator and the Gravity Staff hold things with a GravityManipulatorJob: one object
        // locked to it, and others hovering in its field. Only the old gravity gun's m_GrabbedRigidbody was
        // read before, so those two synced only when the object happened to be in that field.
        static void Collect(Held entry, Component tool) {
            GravityManipulatorJob job = null;
            ToolGravityGun gravity = tool.TryCast<ToolGravityGun>();
            if (gravity != null)
                Add(entry, gravity.m_GrabbedRigidbody);
            DevManipulatorGun dev = tool.TryCast<DevManipulatorGun>();
            if (dev != null) {
                Add(entry, dev.m_GrabbedRigidbody);
                job = dev.gravityManipulator;
            }
            GravityStaff staff = tool.TryCast<GravityStaff>();
            if (staff != null)
                job = staff.gravityManipulator;

            if (job == null)
                return;
            Add(entry, job.m_LockedRigidbody);
            try {
                var field = job.m_RigidbodyHashSet;
                if (field == null)
                    return;
                int taken = 0;
                var enumerator = field.GetEnumerator();
                while (enumerator.MoveNext() && taken++ < MaxFieldObjects)
                    Add(entry, enumerator.Current);
            }
            catch { }
        }

        static readonly List<GameObject> fieldClaims = new List<GameObject>();

        public static void ClaimField(Rigidbody rb) {
            if (rb == null)
                return;
            try {
                GameObject go = rb.gameObject;
                if (go == null)
                    return;
                for (int i = 0; i < fieldClaims.Count; i++) {
                    if (ReferenceEquals(fieldClaims[i], go))
                        return;
                }
                fieldClaims.Add(go);
                Claim(rb);
            }
            catch { }
        }

        public static void ReleaseField() {
            for (int i = 0; i < fieldClaims.Count; i++)
                Release(fieldClaims[i]);
            fieldClaims.Clear();
        }

        public static void OnSceneCleanup() {
            held.Clear();
            claimed.Clear();
            fieldClaims.Clear();
            nextPoll = 0f;
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || held.Count == 0)
                return;

            if (Time.time < nextPoll)
                return;
            nextPoll = Time.time + PollInterval;

            for (int i = held.Count - 1; i >= 0; i--) {
                Held entry = held[i];

                bool alive = true;
                entry.now.Clear();
                try {
                    if (entry.tool == null)
                        alive = false;
                    else
                        Collect(entry, entry.tool);
                }
                catch { alive = false; }

                if (!alive) {
                    ReleaseAll(entry);
                    held.RemoveAt(i);
                    continue;
                }

                // Let go of what it no longer has, take hold of what is new.
                entry.gone.Clear();
                foreach (KeyValuePair<int, Rigidbody> pair in entry.grabbed) {
                    if (!entry.now.ContainsKey(pair.Key))
                        entry.gone.Add(pair.Key);
                }
                for (int g = 0; g < entry.gone.Count; g++) {
                    Release(entry.grabbed[entry.gone[g]]);
                    entry.grabbed.Remove(entry.gone[g]);
                }
                foreach (KeyValuePair<int, Rigidbody> pair in entry.now) {
                    if (entry.grabbed.ContainsKey(pair.Key))
                        continue;
                    entry.grabbed[pair.Key] = pair.Value;
                    Claim(pair.Value);
                }
            }
        }

        public static void Claim(Rigidbody rb) {
            if (rb == null || !SteamIntegration.hasLobby)
                return;

            try {
                GameObject go = rb.gameObject;
                if (go == null || go.IsBlacklisted())
                    return;

                int count;
                claimed.TryGetValue(go, out count);
                claimed[go] = count + 1;

                if (count == 0)
                    ObjectSync.OnGripAttached(go);
            }
            catch (Exception e) {
                EntangleLogger.Warn("[Manipulator] Could not claim an object: " + e.Message);
            }
        }

        public static void Release(Rigidbody rb) {
            if (rb == null)
                return;

            try { Release(rb.gameObject); }
            catch { }
        }

        public static void Release(GameObject go) {
            if (go == null)
                return;

            try {
                int count;
                if (!claimed.TryGetValue(go, out count))
                    return;

                count--;
                if (count > 0) {
                    claimed[go] = count;
                    return;
                }

                claimed.Remove(go);
                ObjectSync.OnForcePullCancelled(go);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ToolGravityGun), "OnTriggerGripAttached")]
    public static class GravityGunHeldPatch
    {
        public static void Postfix(ToolGravityGun __instance) {
            try { ManipulatorSync.Register(__instance); }
            catch (Exception e) { PatchGuard.Report("ManipulatorPatches.GravityGunHeld", e); }
        }
    }

    [HarmonyPatch(typeof(ToolGravityGun), "OnTriggerGripDetached")]
    public static class GravityGunDroppedPatch
    {
        public static void Postfix(ToolGravityGun __instance) {
            try { ManipulatorSync.Unregister(__instance); }
            catch (Exception e) { PatchGuard.Report("ManipulatorPatches.GravityGunDropped", e); }
        }
    }

    [HarmonyPatch(typeof(DevManipulatorGun), "OnTriggerGripAttached")]
    public static class DevManipulatorHeldPatch
    {
        public static void Postfix(DevManipulatorGun __instance) {
            try { ManipulatorSync.Register(__instance); }
            catch (Exception e) { PatchGuard.Report("ManipulatorPatches.DevManipulatorHeld", e); }
        }
    }

    [HarmonyPatch(typeof(DevManipulatorGun), "OnTriggerGripDetached")]
    public static class DevManipulatorDroppedPatch
    {
        public static void Postfix(DevManipulatorGun __instance) {
            try { ManipulatorSync.Unregister(__instance); }
            catch (Exception e) { PatchGuard.Report("ManipulatorPatches.DevManipulatorDropped", e); }
        }
    }

    [HarmonyPatch(typeof(GravityStaff), "OnHandAttached")]
    public static class GravityStaffHeldPatch
    {
        public static void Postfix(GravityStaff __instance) {
            try { ManipulatorSync.Register(__instance); }
            catch (Exception e) { PatchGuard.Report("ManipulatorPatches.GravityStaffHeld", e); }
        }
    }

    [HarmonyPatch(typeof(GravityStaff), "OnHandDetached")]
    public static class GravityStaffDroppedPatch
    {
        public static void Postfix(GravityStaff __instance) {
            try { ManipulatorSync.Unregister(__instance); }
            catch (Exception e) { PatchGuard.Report("ManipulatorPatches.GravityStaffDropped", e); }
        }
    }

    [HarmonyPatch(typeof(FieldGravityGun), "AddPullPoint")]
    public static class GravityFieldPullPatch
    {
        public static void Postfix(Rigidbody rigidbody) {
            try { ManipulatorSync.ClaimField(rigidbody); }
            catch (Exception e) { PatchGuard.Report("ManipulatorPatches.AddPullPoint", e); }
        }
    }

    [HarmonyPatch(typeof(FieldGravityGun), "ClearPullPoints")]
    public static class GravityFieldReleasePatch
    {
        public static void Postfix() {
            try { ManipulatorSync.ReleaseField(); }
            catch (Exception e) { PatchGuard.Report("ManipulatorPatches.ClearPullPoints", e); }
        }
    }
}
