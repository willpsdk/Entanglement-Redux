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

        // Credits to https://answers.unity.com/questions/8500/how-can-i-get-the-full-path-to-a-gameobject.html
        // Modified for different child indexes
        public static string GetFullPath(this Transform current, string otherRootName = null)
        {
            if (current.parent == null)
                return otherRootName != null ? otherRootName : current.name;

            return current.parent.GetFullPath(otherRootName) + "/" + Array.FindIndex(GetChildrenWithName(current.parent, current.name), o => o == current) + "/" + current.name;
        }


        // GameObject.Find skips inactive objects, so zone-culled story props (Runoff buttons, etc.)
        // fail to resolve on remotes. Walk loaded scene roots instead so inactive hierarchies match.
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

            if (spawnIndex < 0)
                rootObject = FindRootIncludingInactive(rootName);
            else { 
                // Try and find the pooled object
                // Register the pool if not already
                Pool pool = SpawnableData.GetSpawnablePool(rootName);

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
       

        public static Transform GetChildWithName(this Transform transform, int index, string name) => transform.GetChildrenWithName(name)[index];

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
