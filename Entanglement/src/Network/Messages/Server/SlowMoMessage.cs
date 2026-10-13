using System;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

using Steamworks;

using Entanglement.Data;
using Entanglement.Managers;

namespace Entanglement.Network
{
    public enum SlowMoSyncMode {
        Off,
        HostOnly,
        Everyone
    }

    public static class SlowMoSync {
        public const string LobbyKey = "slowmo";

        public static SlowMoSyncMode Mode => mode;
        public static bool Applying => applying;

        static SlowMoSyncMode mode = SlowMoSyncMode.Off;
        static Control_GlobalTime clock;

        static int owned;
        static int lastSent = -1;
        static float lastSend = -10f;
        static bool applying;

        static int hostStep;
        static bool haveHost;

        static int risingStep = -1;
        static int risingFrame;

        // Last target Apply worked out, so a change someone else made is heard once (-1: none yet).
        static int lastWorld = -1;

        static readonly Dictionary<long, int> remotes = new Dictionary<long, int>();
        static readonly Dictionary<long, float> remoteTimes = new Dictionary<long, float>();

        public static string Label() {
            if (mode == SlowMoSyncMode.HostOnly)
                return "Slow Mo Host";
            if (mode == SlowMoSyncMode.Everyone)
                return "Slow Mo Everyone";
            return "Slow Mo Off";
        }

        public static void Cycle() {
            int next = ((int)mode + 1) % 3;
            SetMode((SlowMoSyncMode)next);
        }

        public static void SetMode(SlowMoSyncMode next) {
            if (SteamIntegration.hasLobby && !SteamIntegration.isHost)
                return;

            mode = next;
            lastSent = -1;
            if (SteamIntegration.isHost)
                Server.instance?.UpdateLobbyConfig();
        }

        public static void OnSceneLoaded() {
            clock = null;
            remotes.Clear();
            remoteTimes.Clear();
            // Keep hostStep/haveHost and host-owned step across loads so slow-mo doesn't snap to 1x.
            if (!SteamIntegration.isHost)
                owned = 0;
            applying = false;
            lastSent = -1;
            risingStep = -1;
            lastWorld = -1;
            speaker = null;
            dyingSince = -1f;
        }

        public static bool BlocksLocalInput() {
            if (!SteamIntegration.hasLobby || mode != SlowMoSyncMode.HostOnly)
                return false;

            // Your own death slows your own clock, whoever runs slow-mo.
            return !SteamIntegration.isHost && !LocalDying();
        }

        public static void NoteLocalStep(int step) {
            if (applying || BlocksLocalInput())
                return;

            if (step < 0)
                step = 0;
            if (step > 8)
                step = 8;

            // Letting go counts at once, even while dying: a button held into a death is released.
            if (step <= owned || !SteamIntegration.hasLobby) {
                owned = step;
                risingStep = -1;
                return;
            }

            if (LocalDying())
                return;
            risingStep = step;
            risingFrame = Time.frameCount;
        }

        // ---- Your own death ------------------------------------------------------------------------

        static float deathStartedAt = -10f;
        static float dyingSince = -1f;
        const float DeathLead = 0.5f;
        const float DyingLimit = 20f;

        public static void NoteDeathStarting() {
            deathStartedAt = Time.realtimeSinceStartup;
        }

        public static bool LocalDying() {
            if (!SteamIntegration.hasLobby)
                return false;

            bool dying = PlayerDeathManager.hasDied
                || Time.realtimeSinceStartup - deathStartedAt < DeathLead;
            if (!dying) {
                Player_Health health = PlayerScripts.playerHealth;
                try { dying = health != null && (!health.alive || health.deathIsImminent || health.curr_Health <= 0f); }
                catch { dying = false; }
            }

            if (!dying) {
                dyingSince = -1f;
                return false;
            }
            if (dyingSince < 0f)
                dyingSince = Time.realtimeSinceStartup;
            return Time.realtimeSinceStartup - dyingSince < DyingLimit;
        }

        public static float NormalFixedDeltaTime() {
            Control_GlobalTime instance = Clock();
            if (instance == null)
                return 0f;
            try { return instance.def_FixedDeltaTime; }
            catch { return 0f; }
        }

        public static void AfterDeath() {
            risingStep = -1;
            Control_GlobalTime instance = Clock();
            if (instance == null)
                return;

            int world = mode == SlowMoSyncMode.Off || !SteamIntegration.hasLobby ? 0 : WorldStep();
            int current;
            try { current = instance.cur_timeScaleStep; }
            catch { return; }
            if (current == world)
                return;

            applying = true;
            try { instance.STEP_TIMESCALE(world); }
            catch { }
            applying = false;
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby) {
                remotes.Clear();
                remoteTimes.Clear();
                haveHost = false;
                return;
            }

            RefreshFromLobby();

            // A slower step from last frame with no death behind it: the button.
            if (risingStep >= 0 && Time.frameCount > risingFrame) {
                if (!LocalDying())
                    owned = risingStep;
                risingStep = -1;
            }

            if (mode == SlowMoSyncMode.Off)
                return;

            if (mode == SlowMoSyncMode.HostOnly && !SteamIntegration.isHost)
                return;

            if (Time.realtimeSinceStartup - lastSend < 1f && owned == lastSent)
                return;

            Send(owned);
        }

        public static void Apply() {
            if (!SteamIntegration.hasLobby || mode == SlowMoSyncMode.Off)
                return;

            if (mode == SlowMoSyncMode.HostOnly && SteamIntegration.isHost)
                return;

            // Your death slows your clock and nobody else's; leave it be until you are back.
            if (LocalDying())
                return;

            Control_GlobalTime instance = Clock();
            if (instance == null)
                return;

            int world = WorldStep();

            // Someone else's slow-mo moved the target. Your own button already made its sound.
            int mine = Mine();
            bool theirs = lastWorld >= 0 && world != lastWorld && world != mine;
            bool released = lastWorld >= 0 && world < lastWorld;
            lastWorld = world;

            int current = 0;
            try { current = instance.cur_timeScaleStep; }
            catch { return; }

            if (current == world)
                return;

            bool cue = theirs || (released && world == mine);
            if (EntangleLogger.verbose)
                EntangleLogger.Debug("[SlowMo] Clock " + current + " -> " + world + " (" + SlowMoSyncModeText()
                    + ", ours " + mine + ", others' highest " + RemoteMax() + ")" + (cue ? ", with the whoosh" : ", silent"));
            if (cue)
                PlayCue(instance, world > current);

            applying = true;
            try { instance.STEP_TIMESCALE(world); }
            catch { }
            applying = false;
        }

        static string SlowMoSyncModeText() => mode == SlowMoSyncMode.HostOnly ? "host only" : "everyone";

        static int Mine() {
            return risingStep > owned ? risingStep : owned;
        }

        static int WorldStep() {
            if (mode == SlowMoSyncMode.HostOnly) {
                if (SteamIntegration.isHost)
                    return Mine();
                return haveHost ? hostStep : 0;
            }

            int world = Mine();
            int remote = RemoteMax();
            if (remote > world)
                world = remote;
            return world;
        }

        // ---- The button's sound, for everyone else -----------------------------------------------

        static AudioSource speaker;
        static float lastCue = -10f;
        const float CueGap = 0.15f;

        static void PlayCue(Control_GlobalTime instance, bool slowing) {
            if (Time.realtimeSinceStartup - lastCue < CueGap)
                return;

            AudioClip clip = null;
            try { clip = slowing ? instance.decelTime : instance.accelTime; }
            catch { }
            if (clip == null)
                return;

            try {
                if (!speaker) {
                    GameObject go = new GameObject("Entanglement SlowMo Cue");
                    speaker = go.AddComponent<AudioSource>();
                    speaker.playOnAwake = false;
                    speaker.loop = false;
                    // In your head, the way the button's own sound is: time is slowing everywhere.
                    speaker.spatialBlend = 0f;
                    speaker.dopplerLevel = 0f;
                }

                // The game's slow-mo mixer group, so it is levelled and pitched like the real one.
                try {
                    UnityEngine.Audio.AudioMixerGroup group = instance.mixerGroup;
                    if (group != null)
                        speaker.outputAudioMixerGroup = group;
                }
                catch { }

                speaker.Stop();
                speaker.clip = clip;
                speaker.Play();
                lastCue = Time.realtimeSinceStartup;
            }
            catch (Exception e) {
                EntangleLogger.Debug("[SlowMo] Could not play the slow-mo sound: " + e.Message);
            }
        }

        public static void OnRemote(long sender, int step) {
            RefreshFromLobby();
            if (mode == SlowMoSyncMode.Off)
                return;

            if (mode == SlowMoSyncMode.HostOnly && sender != SteamIntegration.lobbyOwnerId)
                return;

            if (step < 0)
                step = 0;
            if (step > 8)
                step = 8;

            int before;
            if (!remotes.TryGetValue(sender, out before) || before != step)
                EntangleLogger.Debug("[SlowMo] " + sender + " is on step " + step + (remotes.ContainsKey(sender) ? " (was " + before + ")" : ""));

            remotes[sender] = step;
            remoteTimes[sender] = Time.realtimeSinceStartup;

            if (sender == SteamIntegration.lobbyOwnerId) {
                hostStep = step;
                haveHost = true;
            }
        }

        static Control_GlobalTime Clock() {
            if (clock != null)
                return clock;

            try { clock = UnityEngine.Object.FindObjectOfType<Control_GlobalTime>(); }
            catch { clock = null; }

            return clock;
        }

        static void RefreshFromLobby() {
            if (!SteamIntegration.hasLobby || SteamIntegration.isHost)
                return;

            string raw = SteamMatchmaking.GetLobbyData(SteamIntegration.lobby, LobbyKey);
            if (string.IsNullOrEmpty(raw))
                return;

            SlowMoSyncMode parsed;
            if (!Enum.TryParse(raw, out parsed))
                return;

            if (parsed == mode)
                return;

            mode = parsed;
            lastSent = -1;
        }

        static int RemoteMax() {
            int best = 0;
            float now = Time.realtimeSinceStartup;
            List<long> stale = null;

            foreach (KeyValuePair<long, int> pair in remotes) {
                float seen;
                if (!remoteTimes.TryGetValue(pair.Key, out seen) || now - seen > 2.5f) {
                    if (stale == null)
                        stale = new List<long>();
                    stale.Add(pair.Key);
                    continue;
                }

                if (pair.Value > best)
                    best = pair.Value;
            }

            if (stale != null) {
                for (int i = 0; i < stale.Count; i++) {
                    remotes.Remove(stale[i]);
                    remoteTimes.Remove(stale[i]);
                }
            }

            return best;
        }

        static void Send(int step) {
            if (Node.activeNode == null)
                return;

            if (step < 0)
                step = 0;
            if (step > 255)
                step = 255;

            SlowMoMessageData data = new SlowMoMessageData();
            data.step = (byte)step;
            data.userId = SteamIntegration.currentUserId;

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SlowMo, data);
            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            lastSent = step;
            lastSend = Time.realtimeSinceStartup;
        }
    }

    public class SlowMoMessageHandler : NetworkMessageHandler<SlowMoMessageData> {
        public override byte? MessageIndex => BuiltInMessageType.SlowMo;

        public override NetworkMessage CreateMessage(SlowMoMessageData data) {
            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[1 + sizeof(long)];
            message.messageData[0] = data.step;
            byte[] id = BitConverter.GetBytes(data.userId);
            for (int i = 0; i < id.Length; i++)
                message.messageData[1 + i] = id[i];
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < 1)
                return;

            int step = message.messageData[0];

            if (Server.instance != null && SlowMoSync.Mode == SlowMoSyncMode.HostOnly)
                return;

            // A client speaks for itself only; what the host passes on says whose it is.
            long owner = sender;
            if (Server.instance == null && message.messageData.Length >= 1 + sizeof(long)) {
                long named = BitConverter.ToInt64(message.messageData, 1);
                if (named != 0)
                    owner = named;
            }
            if (owner == SteamIntegration.currentUserId)
                return;

            SlowMoSync.OnRemote(owner, step);

            if (Server.instance != null && SlowMoSync.Mode == SlowMoSyncMode.Everyone) {
                NetworkMessage relay = NetworkMessage.CreateMessage(BuiltInMessageType.SlowMo,
                    new SlowMoMessageData() { step = (byte)step, userId = sender });
                if (relay != null)
                    Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, relay.GetBytes(), sender);
            }
        }
    }

    public class SlowMoMessageData : NetworkMessageData {
        public byte step;
        public long userId;   // whose step it is
    }

    // Stamped before Death runs, so a slow-down it makes before it flips alive is seen as yours.
    [HarmonyPatch(typeof(Player_Health), "Death")]
    public static class SlowMoDeathPatch {
        public static void Prefix() {
            try { SlowMoSync.NoteDeathStarting(); } catch { }
        }
    }

    [HarmonyPatch(typeof(Control_GlobalTime), "DECREASE_TIMESCALE")]
    public static class SlowMoDecreasePatch {
        public static bool Prefix() => !SlowMoSync.BlocksLocalInput();

        public static void Postfix(Control_GlobalTime __instance) {
            if (__instance == null)
                return;

            SlowMoSync.NoteLocalStep(__instance.cur_timeScaleStep);
        }
    }

    [HarmonyPatch(typeof(Control_GlobalTime), "TOGGLE_TIMESCALE")]
    public static class SlowMoTogglePatch {
        public static bool Prefix() => !SlowMoSync.BlocksLocalInput();

        public static void Postfix(Control_GlobalTime __instance) {
            if (__instance == null)
                return;

            SlowMoSync.NoteLocalStep(__instance.cur_timeScaleStep);
        }
    }

    [HarmonyPatch(typeof(Control_GlobalTime), "INCREASE_TIMESCALE")]
    public static class SlowMoIncreasePatch {
        public static bool Prefix() => !SlowMoSync.BlocksLocalInput();

        public static void Postfix(Control_GlobalTime __instance) {
            if (__instance == null)
                return;

            SlowMoSync.NoteLocalStep(__instance.cur_timeScaleStep);
        }
    }
}
