using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using Utilties;

using Entanglement.Objects;

namespace Entanglement.Extensions {
    public class CustomComponentCache<T> where T : UnityEngine.Object {
        public Dictionary<GameObject, T> m_Cache = new Dictionary<GameObject, T>(new UnityComparer());
        public Dictionary<GameObject, T[]> m_ChildrenCache = new Dictionary<GameObject, T[]>(new UnityComparer());

        public T Get(GameObject go) {
            if (m_Cache.ContainsKey(go)) return m_Cache[go];
            return null;
        }

        public T GetOrAdd(GameObject go) {
            try {
                T get = Get(go);
                if (get) return get;
            }
            catch {
                try { m_Cache.Remove(go); }
                catch { }
            }

            try {
                T comp = go.GetComponent<T>();
                Add(go, comp);
                return comp;
            }
            catch { return null; }
        }

        public T[] GetChildren(GameObject go) {
            if (m_ChildrenCache.ContainsKey(go)) {
                T[] cache = m_ChildrenCache[go];
                if (HasNull(cache)) return null;
                return cache; 
            }
            return null;
        }

        static bool HasNull(T[] items) {
            for (int i = 0; i < items.Length; i++) {
                if (items[i] == null)
                    return true;
            }
            return false;
        }

        public T[] GetOrAddChildren(GameObject go) {
            T[] get = GetChildren(go);
            if (get != null) return get;
            T[] comps = go.GetComponentsInChildren<T>();
            AddChildren(go, comps);
            return comps;
        }

        public void Add(GameObject go, T value) {
            if (m_Cache.ContainsKey(go)) m_Cache.Remove(go);
            m_Cache.Add(go, value);
        }

        public void AddChildren(GameObject go, T[] values) {
            if (m_ChildrenCache.ContainsKey(go)) m_ChildrenCache.Remove(go);
            m_ChildrenCache.Add(go, values);
        }

        public void Remove(GameObject go) {
            m_Cache.Remove(go);
        }
    }

    public static class ComponentCacheExtensions {
        public static CustomComponentCache<Rigidbody> m_RigidbodyCache = new CustomComponentCache<Rigidbody>();
    }
}
