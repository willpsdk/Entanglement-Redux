using System;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

using StressLevelZero.Props.Weapons;

using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    // Your hand moves the slide on your own gun. On everyone else's copy nothing touches it, so the
    // owner says how far back it is and the copy is posed to match. Only the pose and the sounds:
    // the rounds and casings stay the owner's, so nothing is chambered or thrown out twice.
    public static class GunSlideSync
    {
        public enum Step : byte { Grab, Percent, Pulled, LetGo, Returned }

        const float SendEvery = 1f / 30f;
        const float AfterLetGo = 1f;        // the slide springs home on its own once let go
        const float ReturnTime = 0.08f;
        const float MirrorTimeout = 30f;    // they left with a hand on it

        class Pending {
            public TransformSyncable sync;
            public byte value;
        }

        // Guns our hand is on, or was until the time given.
        static readonly Dictionary<int, float> handOn = new Dictionary<int, float>();
        static readonly Dictionary<int, byte> lastSent = new Dictionary<int, byte>();
        static readonly Dictionary<int, float> lastSendAt = new Dictionary<int, float>();
        static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();

        class Mirror {
            public Gun gun;
            public int gunInstance;
            public Gun.AnimationStates before;
            public float perc;
            public bool returning;
            public float returnFrom;
            public float returnStart;
            public float seenAt;
        }

        static readonly Dictionary<ushort, Mirror> mirrors = new Dictionary<ushort, Mirror>();
        static readonly List<ushort> finished = new List<ushort>();
        static readonly List<int> expired = new List<int>();

        public static int MirrorCount => mirrors.Count;

        public static void Clear() {
            handOn.Clear();
            lastSent.Clear();
            lastSendAt.Clear();
            pending.Clear();
            mirrors.Clear();
        }

        static TransformSyncable OwnedSync(Gun gun) {
            if (!SteamIntegration.hasLobby || Node.activeNode == null || !gun || GunRackSync.applying)
                return null;
            TransformSyncable sync = TransformSyncable.cache.Get(gun.gameObject);
            if (!sync && gun.host && gun.host.rb)
                sync = TransformSyncable.cache.Get(gun.host.rb.gameObject);
            if (!sync || sync.objectId == 0 || !sync.IsOwner())
                return null;
            return sync;
        }

        static void Send(TransformSyncable sync, Step step, byte value) {
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.GunSlide,
                new GunSlideMessageData { gunId = sync.objectId, step = step, value = value });
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Object, message.GetBytes());
        }

        static bool InHand(int id) {
            float until;
            return handOn.TryGetValue(id, out until) && Time.realtimeSinceStartup <= until;
        }

        public static void OnGrabbed(Gun gun) {
            if (!SteamIntegration.hasLobby || GunRackSync.applying || !gun)
                return;
            int id = gun.GetInstanceID();
            handOn[id] = float.MaxValue;
            lastSent.Remove(id);
            pending.Remove(id);

            TransformSyncable sync = OwnedSync(gun);
            if (sync)
                Send(sync, Step.Grab, 0);
        }

        public static void OnPercent(Gun gun, float perc) {
            if (handOn.Count == 0 || !gun)
                return;
            int id = gun.GetInstanceID();
            float until;
            if (!handOn.TryGetValue(id, out until) || until != float.MaxValue)
                return;
            TransformSyncable sync = OwnedSync(gun);
            if (!sync)
                return;

            byte value = (byte)Mathf.Clamp(Mathf.RoundToInt(perc * 255f), 0, 255);
            byte last;
            if (lastSent.TryGetValue(id, out last) && last == value) {
                pending.Remove(id);
                return;
            }

            float now = Time.realtimeSinceStartup;
            float at;
            bool end = value == 0 || value == 255;
            if (!end && lastSendAt.TryGetValue(id, out at) && now - at < SendEvery) {
                // Sent from Tick if the hand stops here.
                pending[id] = new Pending { sync = sync, value = value };
                return;
            }

            pending.Remove(id);
            lastSent[id] = value;
            lastSendAt[id] = now;
            Send(sync, Step.Percent, value);
        }

        public static void OnPulled(Gun gun) {
            if (handOn.Count == 0 || !gun || !InHand(gun.GetInstanceID()))
                return;
            TransformSyncable sync = OwnedSync(gun);
            if (sync)
                Send(sync, Step.Pulled, 0);
        }

        public static void OnLetGo(Gun gun) {
            if (!gun)
                return;
            int id = gun.GetInstanceID();
            if (!handOn.ContainsKey(id))
                return;
            handOn[id] = Time.realtimeSinceStartup + AfterLetGo;
            pending.Remove(id);

            TransformSyncable sync = OwnedSync(gun);
            if (sync)
                Send(sync, Step.LetGo, (byte)gun.slideState);
        }

        public static void OnReturned(Gun gun) {
            if (handOn.Count == 0 || !gun || !InHand(gun.GetInstanceID()))
                return;
            TransformSyncable sync = OwnedSync(gun);
            if (sync)
                Send(sync, Step.Returned, 0);
        }

        public static void Apply(ushort gunId, Step step, byte value) {
            if (!ObjectSync.TryGetSyncable(gunId, out Syncable syncable))
                return;
            TransformSyncable sync = syncable.TryCast<TransformSyncable>();
            if (!sync || sync.IsOwner() || !sync._CachedGun)
                return;

            Gun gun = sync._CachedGun;
            try {
                int instance = gun.GetInstanceID();
                Mirror mirror;
                if (!mirrors.TryGetValue(gunId, out mirror) || mirror.gunInstance != instance) {
                    mirror = new Mirror { gun = gun, gunInstance = instance, before = gun.animationState };
                    mirrors[gunId] = mirror;
                }
                mirror.seenAt = Time.realtimeSinceStartup;

                switch (step) {
                    case Step.Grab:
                        mirror.returning = false;
                        EntangleLogger.Debug("[GunSlide] " + gun.name + ": the owner took hold of the slide");
                        break;
                    case Step.Percent:
                        mirror.returning = false;
                        mirror.perc = value / 255f;
                        Pose(gun, mirror.perc);
                        break;
                    case Step.Pulled:
                        mirror.returning = false;
                        mirror.perc = 1f;
                        Pose(gun, 1f);
                        if (gun.gunSFX)
                            gun.gunSFX.SlidePull();
                        break;
                    case Step.LetGo:
                        if ((Gun.SlideStates)value == Gun.SlideStates.LOCKED) {
                            // Out of rounds: it stays back until they send it home.
                            mirror.perc = 1f;
                            Pose(gun, 1f);
                            EntangleLogger.Debug("[GunSlide] " + gun.name + ": let go, slide locked back");
                        }
                        else {
                            StartReturn(mirror);
                            EntangleLogger.Debug("[GunSlide] " + gun.name + ": let go, slide going home");
                        }
                        break;
                    case Step.Returned:
                        if (gun.gunSFX)
                            gun.gunSFX.SlideRelease();
                        if (!mirror.returning)
                            StartReturn(mirror);
                        break;
                }
            }
            catch (Exception e) {
                EntangleLogger.Debug("[GunSlide] Could not show the slide on gun " + gunId + ": " + e.Message);
            }
        }

        static void StartReturn(Mirror mirror) {
            mirror.returning = true;
            mirror.returnFrom = mirror.perc;
            mirror.returnStart = Time.realtimeSinceStartup;
        }

        static void Pose(Gun gun, float perc) => gun.SetAnimationState(Gun.AnimationStates.SLIDEPULL, perc);

        // Forward, and the gun left thinking what it thought before.
        static void Rest(Mirror mirror) {
            Pose(mirror.gun, 0f);
            mirror.gun.animationState = mirror.before;
        }

        public static void Tick() {
            if (pending.Count > 0) {
                float now = Time.realtimeSinceStartup;
                expired.Clear();
                foreach (KeyValuePair<int, Pending> pair in pending) {
                    float at;
                    if (lastSendAt.TryGetValue(pair.Key, out at) && now - at < SendEvery)
                        continue;
                    expired.Add(pair.Key);
                    try {
                        if (pair.Value.sync && pair.Value.sync.IsOwner()) {
                            lastSent[pair.Key] = pair.Value.value;
                            lastSendAt[pair.Key] = now;
                            Send(pair.Value.sync, Step.Percent, pair.Value.value);
                        }
                    }
                    catch { }
                }
                for (int i = 0; i < expired.Count; i++)
                    pending.Remove(expired[i]);
            }

            if (handOn.Count > 0) {
                float now = Time.realtimeSinceStartup;
                expired.Clear();
                foreach (KeyValuePair<int, float> pair in handOn) {
                    if (now > pair.Value)
                        expired.Add(pair.Key);
                }
                for (int i = 0; i < expired.Count; i++) {
                    handOn.Remove(expired[i]);
                    lastSent.Remove(expired[i]);
                    lastSendAt.Remove(expired[i]);
                }
            }

            if (mirrors.Count == 0)
                return;

            float time = Time.realtimeSinceStartup;
            finished.Clear();
            foreach (KeyValuePair<ushort, Mirror> pair in mirrors) {
                Mirror mirror = pair.Value;
                try {
                    if (!mirror.gun) {
                        finished.Add(pair.Key);
                        continue;
                    }
                    if (!mirror.returning) {
                        if (time - mirror.seenAt > MirrorTimeout)
                            StartReturn(mirror);
                        continue;
                    }

                    float t = Mathf.Clamp01((time - mirror.returnStart) / ReturnTime);
                    mirror.perc = Mathf.Lerp(mirror.returnFrom, 0f, t);
                    if (t < 1f) {
                        Pose(mirror.gun, mirror.perc);
                        continue;
                    }
                    Rest(mirror);
                    finished.Add(pair.Key);
                }
                catch { finished.Add(pair.Key); }
            }
            for (int i = 0; i < finished.Count; i++)
                mirrors.Remove(finished[i]);
        }
    }

    [HarmonyPatch(typeof(Gun), "SlideGrabbed")]
    public static class GunSlideGrabPatch
    {
        public static void Postfix(Gun __instance) {
            try { GunSlideSync.OnGrabbed(__instance); }
            catch (Exception e) { PatchGuard.Report("GunSlidePatches.SlideGrabbed", e); }
        }
    }

    [HarmonyPatch(typeof(Gun), "UpdateSlidePercentage")]
    public static class GunSlidePercentPatch
    {
        public static void Postfix(Gun __instance, float perc) {
            try { GunSlideSync.OnPercent(__instance, perc); }
            catch (Exception e) { PatchGuard.Report("GunSlidePatches.UpdateSlidePercentage", e); }
        }
    }

    [HarmonyPatch(typeof(Gun), "CompleteSlidePull")]
    public static class GunSlidePulledPatch
    {
        public static void Postfix(Gun __instance) {
            try { GunSlideSync.OnPulled(__instance); }
            catch (Exception e) { PatchGuard.Report("GunSlidePatches.CompleteSlidePull", e); }
        }
    }

    [HarmonyPatch(typeof(Gun), "SlideGrabbedReleased")]
    public static class GunSlideLetGoPatch
    {
        public static void Postfix(Gun __instance) {
            try { GunSlideSync.OnLetGo(__instance); }
            catch (Exception e) { PatchGuard.Report("GunSlidePatches.SlideGrabbedReleased", e); }
        }
    }
}
