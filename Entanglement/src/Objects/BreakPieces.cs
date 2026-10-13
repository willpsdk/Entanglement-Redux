using System;
using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.Props;

namespace Entanglement.Objects {
    public static class BreakPieces {
        static readonly HashSet<int> pieces = new HashSet<int>();

        static readonly HashSet<int> notPieces = new HashSet<int>();

        // Prop_Health instances that have broken here and not been reset since.
        static readonly HashSet<int> broken = new HashSet<int>();

        public static void OnSceneCleanup() {
            pieces.Clear();
            notPieces.Clear();
            broken.Clear();
            homes.Clear();
            brokenHomes.Clear();
        }

        static readonly Dictionary<int, Vector3> homes = new Dictionary<int, Vector3>();
        static readonly List<Vector3> brokenHomes = new List<Vector3>();

        public const float HomeMatch = 0.2f;

        /// <summary>Level loaded: note where every placed breakable stands, switched off or not.</summary>
        public static void CaptureHomes() {
            homes.Clear();
            brokenHomes.Clear();
            Capture(UnhollowerRuntimeLib.Il2CppType.Of<Prop_Health>());
            Capture(UnhollowerRuntimeLib.Il2CppType.Of<ObjectDestructable>());
        }

        static void Capture(Il2CppSystem.Type type) {
            UnhollowerBaseLib.Il2CppReferenceArray<UnityEngine.Object> found;
            try { found = Resources.FindObjectsOfTypeAll(type); }
            catch { return; }
            if (found == null)
                return;

            for (int i = 0; i < found.Length; i++) {
                try {
                    Component component = found[i] ? found[i].TryCast<Component>() : null;
                    if (!component)
                        continue;
                    GameObject go = component.gameObject;
                    // Prefabs and assets have no scene; pooled ones are addressed by spawn id.
                    if (!go.scene.IsValid() || !go.scene.isLoaded || Pooled(component.transform))
                        continue;
                    homes[go.GetInstanceID()] = component.transform.position;
                }
                catch { }
            }
        }

        static bool Pooled(Transform t) {
            for (Transform current = t; current; current = current.parent) {
                if (current.GetComponent<StressLevelZero.Pool.Poolee>() || current.name == "Pool Manager")
                    return true;
            }
            return false;
        }

        /// <summary>Where a placed breakable stood when the level loaded.</summary>
        public static bool TryGetHome(Transform t, out Vector3 home) {
            home = Vector3.zero;
            try { return t && homes.TryGetValue(t.gameObject.GetInstanceID(), out home); }
            catch { return false; }
        }

        /// <summary>It broke here; a break for it that arrives after it is gone has nothing to do.</summary>
        public static void NoteBroken(Transform t) {
            Vector3 home;
            if (TryGetHome(t, out home))
                brokenHomes.Add(home);
        }

        /// <summary>The placed breakable that started at this spot has already broken here.</summary>
        public static bool BrokeAt(Vector3 spot) {
            for (int i = 0; i < brokenHomes.Count; i++) {
                if ((brokenHomes[i] - spot).sqrMagnitude <= HomeMatch * HomeMatch)
                    return true;
            }
            return false;
        }

        public static bool IsBroken(Prop_Health health) {
            try { return health && broken.Contains(health.GetInstanceID()); }
            catch { return false; }
        }

        /// <summary>DESTROYED has run on it here.</summary>
        public static void MarkBroken(Prop_Health health) {
            if (!health)
                return;
            if (broken.Add(health.GetInstanceID()))
                NoteBroken(health.transform);
            Record(health);
        }

        /// <summary>Whole again (SETPROP, or a pooled crate coming back out).</summary>
        public static void MarkWhole(Prop_Health health) {
            if (!health)
                return;
            broken.Remove(health.GetInstanceID());
        }

        static void Record(Prop_Health health) {
            try { Record(health.prop_Bits); } catch { }
            try { Record(health.prop_Hero); } catch { }
        }

        static void Record(UnhollowerBaseLib.Il2CppReferenceArray<GameObject> group) {
            if (group == null)
                return;
            for (int i = 0; i < group.Length; i++) {
                GameObject piece = group[i];
                if (piece)
                    pieces.Add(piece.GetInstanceID());
            }
        }

        public static bool IsPiece(Transform t) {
            if (!t)
                return false;
            try {
                int self = t.gameObject.GetInstanceID();
                if (notPieces.Contains(self))
                    return false;

                int depth = 0;
                for (Transform current = t; current && depth < 12; current = current.parent, depth++) {
                    if (pieces.Contains(current == t ? self : current.gameObject.GetInstanceID()))
                        return true;
                    if (current == t)
                        continue;

                    Prop_Health health = current.GetComponent<Prop_Health>();
                    if (health) {
                        if (InGroup(health.prop_Bits, t) || InGroup(health.prop_Hero, t))
                            return true;
                        break;
                    }
                }

                if (notPieces.Count > 4096)
                    notPieces.Clear();
                notPieces.Add(self);
            }
            catch { }
            return false;
        }

        static bool InGroup(UnhollowerBaseLib.Il2CppReferenceArray<GameObject> group, Transform t) {
            if (group == null)
                return false;
            for (int i = 0; i < group.Length; i++) {
                GameObject piece = group[i];
                if (!piece)
                    continue;
                Transform pieceT = piece.transform;
                if (pieceT == t || t.IsChildOf(pieceT)) {
                    pieces.Add(piece.GetInstanceID());
                    return true;
                }
            }
            return false;
        }

        public static bool IsBreakable(GameObject go) {
            if (!go)
                return false;
            try {
                return go.GetComponent<Prop_Health>() || go.GetComponent<ObjectDestructable>();
            }
            catch { return false; }
        }
    }
}
