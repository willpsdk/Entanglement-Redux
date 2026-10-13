using System.Collections.Generic;

using UnityEngine;

using StressLevelZero.Props.Weapons;

using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.Objects
{
    public static class HolsterSync
    {
        // ---- Your own slots ------------------------------------------------------------------------

        // After ReturnFromUI, while the items fly back into their slots.
        const float ReturnHold = 1f;
        const float MaxUiTime = 30f;

        static readonly List<TransformSyncable> inUi = new List<TransformSyncable>();

        static TransformSyncable SyncOf(HandWeaponSlotReciever slot) {
            try {
                if (!slot || !slot.m_WeaponHost)
                    return null;
                Rigidbody rb = slot.m_WeaponHost.rb;
                return rb ? TransformSyncable.cache.Get(rb.gameObject) : null;
            }
            catch { return null; }
        }

        static string Label(TransformSyncable sync) {
            try { return "#" + sync.objectId + " " + sync.gameObject.name; }
            catch { return "#?"; }
        }

        public static void OnSendToUI(HandWeaponSlotReciever slot) {
            if (!SteamIntegration.hasLobby)
                return;
            TransformSyncable sync = SyncOf(slot);
            if (!sync)
                return;
            if (!sync.inSlotUi)
                EntangleLogger.Debug("[Holster] Inventory took out our " + Label(sync) + ": keeping its position to ourselves");
            sync.inSlotUi = true;
            sync.slotUiUntil = Time.time + MaxUiTime;
            sync.slotUiResend = true;
            if (!inUi.Contains(sync))
                inUi.Add(sync);
        }

        public static void OnReturnFromUI(HandWeaponSlotReciever slot) {
            if (!SteamIntegration.hasLobby)
                return;
            TransformSyncable sync = SyncOf(slot);
            if (!sync)
                return;
            EntangleLogger.Debug("[Holster] Inventory putting back our " + Label(sync) + ", slotted again in " + ReturnHold + " s");
            sync.inSlotUi = true;
            sync.slotUiUntil = Time.time + ReturnHold;
            sync.slotUiResend = true;
            if (!inUi.Contains(sync))
                inUi.Add(sync);
        }

        /// <summary>Owner: whether this item's position is kept to ourselves for now.</summary>
        public static bool HiddenInUi(TransformSyncable sync) {
            if (!sync.inSlotUi)
                return false;
            if (Time.time < sync.slotUiUntil)
                return true;
            bool resend = sync.slotUiResend;
            sync.inSlotUi = false;
            sync.slotUiUntil = -1f;
            sync.slotUiResend = false;
            inUi.Remove(sync);
            if (resend) {
                EntangleLogger.Debug("[Holster] Our " + Label(sync) + " is back in its slot: sending its position again, and that it is slotted");
                SendSlotted(sync, true);
            }
            return false;
        }

        public static void OnLocalGrab(GameObject grabbed) {
            if (inUi.Count == 0 || !grabbed)
                return;
            Transform t = grabbed.transform;
            for (int i = inUi.Count - 1; i >= 0; i--) {
                TransformSyncable sync = inUi[i];
                if (!sync) {
                    inUi.RemoveAt(i);
                    continue;
                }
                if (!t.IsChildOf(sync.transform))
                    continue;
                sync.inSlotUi = false;
                sync.slotUiUntil = -1f;
                sync.slotUiResend = false;
                inUi.RemoveAt(i);
                EntangleLogger.Debug("[Holster] Took our " + Label(sync) + " from the inventory: out of its slot for everyone");
                SendSlotted(sync, false);
            }
        }

        /// <summary>Tells everyone this item went into (true) or came out of one of our slots.</summary>
        public static void SendSlotted(TransformSyncable sync, bool slotted) {
            if (!SteamIntegration.hasLobby || !sync || sync.objectId == 0 || Node.activeNode == null)
                return;
            EntangleLogger.Debug("[Holster] Our " + Label(sync) + (slotted ? " went into a slot" : " came out of its slot"));
            TransformCollisionMessageData data = new TransformCollisionMessageData() {
                objectId = sync.objectId,
                enabled = !slotted,
                holder = SteamIntegration.currentUserId,
            };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.TransformCollision, data);
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Object, message.GetBytes());
        }

        // ---- Everyone else's slots -----------------------------------------------------------------

        // How long an item takes its place from its updates before it is pinned where it is.
        const float Settle = 0.6f;
        // An item further than this from every spine bone of its holder isn't on their body.
        const float MaxReach = 1.2f;

        class Pin {
            public TransformSyncable sync;
            public long holder;
            public float since;
            public Transform anchor;
            public Vector3 localPos;
            public Quaternion localRot;
            public bool placed;
            public bool wasKinematic;
            public RigidbodyInterpolation interpolation;
        }

        static readonly Dictionary<ushort, Pin> pins = new Dictionary<ushort, Pin>();
        static readonly List<ushort> dropped = new List<ushort>();
        static readonly List<string> dropReasons = new List<string>();

        static readonly HumanBodyBones[] spine = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest
        };

        public static void Holstered(TransformSyncable sync, long holder) {
            if (!sync || holder == 0 || holder == SteamIntegration.currentUserId)
                return;
            if (!PlayerRepresentation.representations.ContainsKey(holder)) {
                EntangleLogger.Debug("[Holster] " + Label(sync) + " went into a slot of " + holder + ", who has no rep here: not pinned");
                return;
            }
            Unpin(sync.objectId, null);

            pins[sync.objectId] = new Pin() { sync = sync, holder = holder, since = Time.time };
            EntangleLogger.Debug("[Holster] " + Label(sync) + " went into a slot of " + holder + ": taking its place on their body for " + Settle + " s");
        }

        public static void Unholstered(ushort objectId) => Unpin(objectId, "out of the slot");

        /// <summary>A player left: their items stay where they were and are simulated again.</summary>
        public static void ReleaseHolder(long holder) {
            dropped.Clear();
            foreach (KeyValuePair<ushort, Pin> pair in pins)
                if (pair.Value.holder == holder)
                    dropped.Add(pair.Key);
            for (int i = 0; i < dropped.Count; i++)
                Unpin(dropped[i], "their player left");
        }

        public static void Clear() {
            pins.Clear();
            inUi.Clear();
        }

        // reason: for the log; null when it is pinned again straight away.
        static void Unpin(ushort objectId, string reason) {
            Pin pin;
            if (!pins.TryGetValue(objectId, out pin))
                return;
            pins.Remove(objectId);
            if (reason != null)
                EntangleLogger.Debug("[Holster] " + Label(pin.sync) + " unpinned: " + reason);
            try {
                Rigidbody rb = pin.sync ? pin.sync.rb : null;
                if (rb && pin.placed) {
                    rb.isKinematic = pin.wasKinematic;
                    rb.interpolation = pin.interpolation;
                }
            }
            catch { }
        }

        /// <summary>Every frame, after the reps are posed: pinned items go where their holder is.</summary>
        public static void Tick() {
            if (pins.Count == 0)
                return;
            if (!SteamIntegration.hasLobby) {
                dropped.Clear();
                dropped.AddRange(pins.Keys);
                for (int i = 0; i < dropped.Count; i++)
                    Unpin(dropped[i], "left the lobby");
                return;
            }

            dropped.Clear();
            dropReasons.Clear();
            foreach (KeyValuePair<ushort, Pin> pair in pins) {
                string why = null;
                try { why = Place(pair.Value); }
                catch (System.Exception e) { why = "error: " + e.Message; }
                if (why != null) {
                    dropped.Add(pair.Key);
                    dropReasons.Add(why);
                }
            }
            for (int i = 0; i < dropped.Count; i++)
                Unpin(dropped[i], dropReasons[i]);
        }

        // Null to keep it pinned, or why it is let go.
        static string Place(Pin pin) {
            TransformSyncable sync = pin.sync;
            if (!sync || !sync.isValid || !sync.rb)
                return "gone";
            if (sync.IsOwner())
                return "it is ours now";
            if (!sync.gameObject.activeInHierarchy)
                return null;

            PlayerRepresentation rep;
            if (!PlayerRepresentation.representations.TryGetValue(pin.holder, out rep) || rep == null || !rep.repRoot)
                return "their rep is gone";

            // Where its updates say it is, relative to the body as it is here now.
            Vector3 heardPos = sync.hasNetTarget ? sync.netPosition : sync.transform.position;
            Quaternion heardRot = sync.hasNetTarget ? sync.netRotation : sync.transform.rotation;

            bool settling = Time.time - pin.since < Settle;
            if (!pin.anchor || settling) {
                Transform anchor = NearestSpineBone(rep, heardPos);
                if (!anchor)
                    return settling ? null : "not within " + MaxReach + " m of their body";
                pin.anchor = anchor;
                pin.localPos = anchor.InverseTransformPoint(heardPos);
                pin.localRot = Quaternion.Inverse(anchor.rotation) * heardRot;
                if (settling)
                    return null;
            }

            // Taken over from the updates from here on.
            Rigidbody rb = sync.rb;
            if (!pin.placed) {
                pin.wasKinematic = rb.isKinematic;
                pin.interpolation = rb.interpolation;
                pin.placed = true;
                EntangleLogger.Debug("[Holster] " + Label(sync) + " pinned to " + pin.anchor.name + " of " + pin.holder
                    + ", " + (pin.anchor.position - pin.anchor.TransformPoint(pin.localPos)).magnitude.ToString("F2") + " m from it");
            }
            if (!rb.isKinematic)
                rb.isKinematic = true;
            if (rb.interpolation != RigidbodyInterpolation.None)
                rb.interpolation = RigidbodyInterpolation.None;

            Vector3 pos = pin.anchor.TransformPoint(pin.localPos);
            Quaternion rot = pin.anchor.rotation * pin.localRot;
            sync.transform.SetPositionAndRotation(pos, rot);
            rb.position = pos;
            rb.rotation = rot;
            return null;
        }

        static Transform NearestSpineBone(PlayerRepresentation rep, Vector3 position) {
            Animator animator = rep.activeAnimator ? rep.activeAnimator : rep.repAnimator;
            Transform best = null;
            float bestSqr = MaxReach * MaxReach;
            if (animator && animator.isHuman) {
                for (int i = 0; i < spine.Length; i++) {
                    Transform bone = animator.GetBoneTransform(spine[i]);
                    if (!bone)
                        continue;
                    float sqr = (bone.position - position).sqrMagnitude;
                    if (sqr < bestSqr) {
                        bestSqr = sqr;
                        best = bone;
                    }
                }
            }
            if (!best && rep.repRoot && (rep.repRoot.position - position).sqrMagnitude < bestSqr)
                best = rep.repRoot;
            return best;
        }

        public static int PinnedCount => pins.Count;
    }
}
