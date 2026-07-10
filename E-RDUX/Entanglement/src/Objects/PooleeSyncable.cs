using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using StressLevelZero.Pool;

using Entanglement.Extensions;
using Entanglement.Data;

using MelonLoader;

namespace Entanglement.Objects
{
    [RegisterTypeInIl2Cpp]
    public class PooleeSyncable : MonoBehaviour {
        public static CustomComponentCache<PooleeSyncable> _Cache = new CustomComponentCache<PooleeSyncable>();

        public static Dictionary<ushort, PooleeSyncable> _PooleeLookup = new Dictionary<ushort, PooleeSyncable>(new UnityComparer());

        public PooleeSyncable(IntPtr intPtr) : base(intPtr) { }


        public Poolee Poolee;

        public ushort id;

        public TransformSyncable[] transforms;

        public void Awake() {
            Poolee = GetComponent<Poolee>();
            _Cache.Add(gameObject, this);
        }

        public void Start() {
            // Indexer instead of Add: a host running an older build can broadcast duplicate
            // spawn ids for debris bursts, which must not throw inside the il2cpp trampoline
            _PooleeLookup[id] = this;
        }

        public void OnDestroy() {
            _Cache.Remove(gameObject);

            // Only remove the entry if it still points at us, a duplicate id may have overwritten it
            if (_PooleeLookup.TryGetValue(id, out PooleeSyncable current) && current == this)
                _PooleeLookup.Remove(id);
        }

        public void OnSpawn(long ownerId, SimplifiedTransform simplifiedTransform) {
            MelonCoroutines.Start(CoOnSpawn(ownerId, simplifiedTransform));
        }

        public void SetOwner(long ownerId) {
            foreach (TransformSyncable sync in transforms)
                sync.ForceOwner(ownerId, false);
        }

        public IEnumerator CoOnSpawn(long ownerId, SimplifiedTransform simplifiedTransform) {
            gameObject.SetActive(false);
            yield return null;
            simplifiedTransform.Apply(transform);
            gameObject.SetActive(true);

            SetOwner(ownerId);
        }
    }
}
