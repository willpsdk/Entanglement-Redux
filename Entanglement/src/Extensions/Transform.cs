using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;

using StressLevelZero.Pool;
using StressLevelZero.Data;

using Entanglement.Patching;
using Entanglement.Data;
using Entanglement.Objects;

namespace Entanglement.Extensions
{
    public static class TransformExtensions {
        public static bool InHierarchyOf(this Transform t, string parentName) {
            if (t.name == parentName)
                return true;

            if (t.parent == null)
                return false;

            t = t.parent;

            return InHierarchyOf(t, parentName);
        }

        public static string GetPathToRoot(this Transform t, Transform root) {
            string path = "/" + t.name;
            while (t.parent != null && t != root)
            {
                t = t.parent;
                path = "/" + t.name + path;
            }
            return path;
        }

        public static void ForceActivate(this Transform transform) {
            transform.gameObject.SetActive(true);
            if (transform.parent != null)
                transform.parent.ForceActivate();
        }

        public static string GetFullPath(this Transform current, string otherRootName = null)
        {
            if (current.parent == null)
                return otherRootName != null ? otherRootName : RootKey(current);

            return current.parent.GetFullPath(otherRootName) + "/" + Array.FindIndex(GetChildrenWithName(current.parent, current.name), o => o == current) + "/" + current.name;
        }

        static string RootKey(Transform root) {
            string name = root.name;
            if (!HasNamesake(root))
                return name;

            Vector3 p = root.position;
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            return name + "@" + p.x.ToString("F1", inv) + "," + p.y.ToString("F1", inv) + "," + p.z.ToString("F1", inv);
        }

        // Listing every root object of the scene took 15-20ms in a big level, and it ran twice for every
        // path built, so a handful of NPC despawn events in one pass cost 80ms (Oct 9 host log). The answer
        // for a root is remembered for a few seconds.
        const float NamesakeKeep = 5f;
        static readonly Dictionary<int, KeyValuePair<float, bool>> namesakes = new Dictionary<int, KeyValuePair<float, bool>>();

        static bool HasNamesake(Transform root) {
            try {
                int key = root.GetInstanceID();
                float now = UnityEngine.Time.realtimeSinceStartup;
                KeyValuePair<float, bool> known;
                if (namesakes.TryGetValue(key, out known) && now - known.Key < NamesakeKeep)
                    return known.Value;

                bool found = false;
                Scene scene = root.gameObject.scene;
                if (scene.IsValid()) {
                    GameObject self = root.gameObject;
                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int i = 0; i < roots.Length; i++) {
                        if (roots[i] && roots[i] != self && roots[i].name == self.name) {
                            found = true;
                            break;
                        }
                    }
                }
                if (namesakes.Count > 2048)
                    namesakes.Clear();
                namesakes[key] = new KeyValuePair<float, bool>(now, found);
                return found;
            }
            catch { }
            return false;
        }

        // "Pool name [3]" and "Pool name(Clone)" are both "Pool name".
        public static string PoolBaseName(string name) {
            if (string.IsNullOrEmpty(name))
                return name;
            string trimmed = name;
            if (trimmed.EndsWith("]")) {
                int open = trimmed.LastIndexOf(" [");
                if (open > 0)
                    trimmed = trimmed.Substring(0, open);
            }
            if (trimmed.EndsWith("(Clone)"))
                trimmed = trimmed.Substring(0, trimmed.Length - "(Clone)".Length);
            return trimmed.TrimEnd();
        }

        // A pooled object each machine spawned for itself matches within this of the spot.
        const float PlaceMatch = 3f;

        static GameObject FindNearestRoot(string key) {
            int at = key.LastIndexOf('@');
            bool byPlace = key[0] == '~';
            string name = byPlace ? key.Substring(1, at - 1) : key.Substring(0, at);
            string[] parts = key.Substring(at + 1).Split(',');
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            float x, y, z;
            if (parts.Length != 3
                || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out x)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out y)
                || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out z))
                return byPlace ? null : FindRootIncludingInactive(key);

            Vector3 spot = new Vector3(x, y, z);
            GameObject best = null;
            float bestDistance = byPlace ? PlaceMatch * PlaceMatch : float.MaxValue;
            for (int s = 0; s < SceneManager.sceneCount; s++) {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++) {
                    if (!roots[r])
                        continue;
                    if (byPlace) {
                        if (!roots[r].activeInHierarchy || PoolBaseName(roots[r].name) != name)
                            continue;
                    }
                    else if (roots[r].name != name)
                        continue;
                    float distance = (roots[r].transform.position - spot).sqrMagnitude;
                    if (distance < bestDistance) {
                        bestDistance = distance;
                        best = roots[r];
                    }
                }
            }
            return best;
        }


        public static GameObject FindRootIncludingInactive(string rootName) {
            if (string.IsNullOrEmpty(rootName))
                return null;

            GameObject active = GameObject.Find("/" + rootName);
            if (active)
                return active;

            for (int s = 0; s < SceneManager.sceneCount; s++) {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++) {
                    if (roots[r] && roots[r].name == rootName)
                        return roots[r];
                }
            }

            return null;
        }

        public static Transform FindRelativeIncludingInactive(Transform root, string relativePath) {
            if (!root || string.IsNullOrEmpty(relativePath))
                return root;

            Transform current = root;
            string[] parts = relativePath.Split('/');
            for (int i = 0; i < parts.Length; i++) {
                if (string.IsNullOrEmpty(parts[i]))
                    continue;
                if (!current)
                    return null;

                Transform next = null;
                for (int c = 0; c < current.childCount; c++) {
                    Transform child = current.GetChild(c);
                    if (child && child.name == parts[i]) {
                        next = child;
                        break;
                    }
                }
                current = next;
            }
            return current;
        }

        // Tries to get the closest transform to path
        public static Transform GetFromFullPath(this string path, int spawnIndex = -1, float spawnTime = -1f) {
            string[] transformPaths = path.Split('/');

            int index = 0;
            string rootName = transformPaths[index++];

            GameObject rootObject = null;

            if (rootName.Length > 1 && rootName[0] == '#') {
                ushort pooleeId;
                if (!ushort.TryParse(rootName.Substring(1), out pooleeId))
                    return null;

                PooleeSyncable poolee;
                if (!PooleeSyncable._PooleeLookup.TryGetValue(pooleeId, out poolee) || poolee == null)
                    return null;

                try { rootObject = poolee.gameObject; }
                catch { return null; }
            }
            else if (spawnIndex < 0)
                rootObject = rootName.IndexOf('@') > 0 ? FindNearestRoot(rootName) : FindRootIncludingInactive(rootName);
            else { 
                Pool pool = SpawnableData.GetSpawnablePool(rootName);

                if (pool && Patching.InstantiatePatch.IsSceneNpc(pool.Prefab))
                    return null;

                if (pool)
                {
                    if (pool)
                    {
                        bool instantiateObject = ObjectSync.CheckForInstantiation(pool.Prefab, rootName);

                        if (instantiateObject)
                            rootObject = GameObject.Instantiate(pool.Prefab);
                        else
                        {
                            Poolee thisPoolee = pool.GetAccuratePoolee(spawnIndex, spawnTime);
                            if (thisPoolee)
                                rootObject = thisPoolee.gameObject;
                        }
                    }
                }
            }

            if (!rootObject)
                return null;

            // Custom map fixer
            Transform current = rootObject.transform;
            if (rootObject.name == "CUSTOM_MAP_ROOT") {
                string currentName = "/CUSTOM_MAP_ROOT";
                for (int i = 1; i < transformPaths.Length; i++) {
                    i++;
                    currentName += $"/{transformPaths[i]}";
                }
                // Prefer an inactive-aware walk under the custom map root; Find() misses culled props
                string relative = currentName.StartsWith("/CUSTOM_MAP_ROOT/")
                    ? currentName.Substring("/CUSTOM_MAP_ROOT/".Length)
                    : currentName.TrimStart('/');
                Transform foundT = FindRelativeIncludingInactive(rootObject.transform, relative);
                if (foundT)
                    current = foundT;
                else {
                    GameObject found = GameObject.Find(currentName);
                    current = found ? found.transform : null;
                }
            }
            else {
                for (int i = index; i < transformPaths.Length; i++) {
                    if (!current)
                        current = rootObject.transform.GetChildWithName(int.Parse(transformPaths[i++]), transformPaths[i]);
                    else
                        current = current.GetChildWithName(int.Parse(transformPaths[i++]), transformPaths[i]);
                }
            }
            return current;
        }
       

        public static Transform GetChildWithName(this Transform transform, int index, string name) {
            Transform[] matches = transform.GetChildrenWithName(name);
            if (matches == null)
                return null;
            if (index >= 0 && index < matches.Length && matches[index])
                return matches[index];

            Transform only = null;
            for (int i = 0; i < matches.Length; i++) {
                if (!matches[i])
                    continue;
                if (only)
                    return null;
                only = matches[i];
            }
            return only;
        }

        public static Transform[] GetChildrenWithName(this Transform t, string name) {
            Transform[] children = new Transform[t.childCount];
            for (int i = 0; i < t.childCount; i++) {
                Transform child = t.GetChild(i);
                if (child.name == name) children[i] = child;
            }
            return children;
        }

        public static Transform[] GetGrandChildren(this Transform t) {
            List<Transform> transforms = new List<Transform>(t.childCount * t.childCount);
            for (var i = 0; i < t.childCount; i++) {
                Transform child = t.GetChild(i);
                transforms.Add(child);
                transforms.AddRange(child.GetGrandChildren());
            }

            return transforms.ToArray();
        }

        public static Vector3 TransformPosition(this Transform t, Vector3 position) => position + t.position;

        public static Vector3 TransformPosition(this Vector3 v, Vector3 position) => position + v;

        public static Vector3 InverseTransformPosition(this Transform t, Vector3 position) => position - t.position;

        public static Vector3 InverseTransformPosition(this Vector3 v, Vector3 position) => position - v;

    }
}
