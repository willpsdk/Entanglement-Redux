using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Extensions;
using Entanglement.Representation;
using Entanglement.Patching;

using StressLevelZero.Pool;
using StressLevelZero.Interaction;
using StressLevelZero.Props.Weapons;
using StressLevelZero.Props;

using Utilties;

using MelonLoader;

namespace Entanglement.Objects
{
    public partial class TransformSyncable : Syncable {
        public static CustomComponentCache<TransformSyncable> DestructCache = new CustomComponentCache<TransformSyncable>();

        public Prop_Health _CachedHealth;
        public ObjectDestructable _CachedDestructable;

        public float objectHealth = 0f;
        public bool healthCaptured;

        // Runs every physics step for every body someone else owns, and most bodies have neither.
        public void SetHealth(float health) {
            if (Has(_CachedDestructable))
                _CachedDestructable._health = health;
            if (Has(_CachedHealth))
                _CachedHealth.cur_Health = health;
        }

        public float GetHealth() {
            if (_CachedDestructable) return _CachedDestructable._health;
            if (_CachedHealth) return _CachedHealth.cur_Health;
            return 0f;
        }

        public void Destruct() {
            if (_CachedDestructable)
            {
                // Someone else broke it and said so; don't send the break back to them.
                Entanglement.Patching.BreakSync.applying = true;
                try {
                    _CachedDestructable._health = 0f;
                    _CachedDestructable.TakeDamage(Vector3.up, 10f, true, StressLevelZero.Combat.AttackType.None);
                }
                finally { Entanglement.Patching.BreakSync.applying = false; }
            }
            if (_CachedHealth) _CachedHealth.TIMEDKILL();

            timeOfDisable = Time.realtimeSinceStartup;
        }
    }
}
