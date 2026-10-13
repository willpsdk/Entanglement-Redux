using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using UnityEngine;

using HarmonyLib;

using StressLevelZero.AI;

using Entanglement.Network;
using Entanglement.Representation;

using MelonLoader;

namespace Entanglement.Objects
{
    public static class NpcConfigSync
    {
        const float TickInterval = 1f;
        const float ApplyWindow = 10f;

        // Host: brain instance id -> the config it was given ("" for none).
        static readonly Dictionary<int, string> configByBrain = new Dictionary<int, string>();
        // Host: spawn id -> what was last sent for it.
        static readonly Dictionary<ushort, string> sent = new Dictionary<ushort, string>();
        // Client: copy instance id -> what was last applied to it.
        static readonly Dictionary<int, string> applied = new Dictionary<int, string>();
        // Client: config assets by name.
        static readonly Dictionary<string, PuppetMasta.BaseEnemyConfig> configs = new Dictionary<string, PuppetMasta.BaseEnemyConfig>();
        // Client: configs the host named that are not loaded here, said once each.
        static readonly HashSet<string> missing = new HashSet<string>();
        static float nextConfigScan;
        static float nextTick;
        static int appliedCount;

        public static void OnSceneCleanup() {
            configByBrain.Clear();
            sent.Clear();
            applied.Clear();
            configs.Clear();
            missing.Clear();
            nextConfigScan = 0f;
            nextTick = 0f;
        }

        // ---- Host ---------------------------------------------------------------------------------

        // Noted with or without a lobby: NPCs set up before the lobby was made still count.
        public static void OnConfigSet(AIBrain brain, PuppetMasta.BaseEnemyConfig config) {
            if (!brain)
                return;
            try {
                configByBrain[brain.GetInstanceID()] = config ? config.name : "";
                nextTick = 0f;
            }
            catch { }
        }

        /// <summary>Host: this id is being sent as a new spawn, so its config goes out again too.</summary>
        public static void Forget(ushort id) {
            sent.Remove(id);
            nextTick = 0f;
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || !Node.isServer || Node.activeNode == null)
                return;
            if (PlayerRepresentation.RemoteCount == 0)
                return;
            float now = Time.realtimeSinceStartup;
            if (now < nextTick)
                return;
            nextTick = now + TickInterval;

            foreach (PooleeSyncable poolee in PooleeSyncable._PooleeLookup.Values.ToArray()) {
                try {
                    if (!ObjectSync.Live(poolee) || poolee.id == 0 || !poolee.gameObject.activeInHierarchy)
                        continue;
                    string signature = Signature(poolee);
                    if (signature == null)
                        continue;
                    string last;
                    if (sent.TryGetValue(poolee.id, out last) && last == signature)
                        continue;
                    sent[poolee.id] = signature;
                    SceneEventSync.SendEventPath(SceneEventType.NpcConfig, signature, poolee.id, false);
                }
                catch { }
            }
        }

        /// <summary>Host: one player gets this NPC's config (a catch-up spawn, or joining).</summary>
        public static void SendTo(long userId, PooleeSyncable poolee) {
            if (!Node.isServer || Node.activeNode == null || poolee == null || poolee.id == 0)
                return;
            try {
                string signature = Signature(poolee);
                if (signature == null)
                    return;
                SceneEventMessageData data = new SceneEventMessageData() {
                    eventType = SceneEventType.NpcConfig,
                    arg = poolee.id,
                    objectPath = signature,
                };
                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.SceneEvent, data);
                if (message != null)
                    Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
            }
            catch { }
        }

        /// <summary>Host: everything's config, for a joiner (placed NPCs don't come as spawns).</summary>
        public static int ReplayTo(long userId) {
            int count = 0;
            foreach (PooleeSyncable poolee in PooleeSyncable._PooleeLookup.Values.ToArray()) {
                try {
                    if (!ObjectSync.Live(poolee) || poolee.id == 0 || !poolee.gameObject.activeInHierarchy)
                        continue;
                    if (Signature(poolee) == null)
                        continue;
                    SendTo(userId, poolee);
                    count++;
                }
                catch { }
            }
            return count;
        }

        // "config|x,y,z", or null for an NPC with nothing to say (no config noted, normal size).
        static string Signature(PooleeSyncable poolee) {
            AIBrain brain = poolee.GetComponentInChildren<AIBrain>(true);
            if (!brain)
                return null;
            string name;
            configByBrain.TryGetValue(brain.GetInstanceID(), out name);
            Vector3 scale = poolee.transform.localScale;
            bool normal = (scale - Vector3.one).sqrMagnitude < 0.0001f;
            if (string.IsNullOrEmpty(name) && normal)
                return null;
            CultureInfo inv = CultureInfo.InvariantCulture;
            return (name ?? "") + "|" + scale.x.ToString("F3", inv) + "," + scale.y.ToString("F3", inv) + "," + scale.z.ToString("F3", inv);
        }

        // ---- Client -------------------------------------------------------------------------------

        /// <summary>A copy starting a new life: the config set on its last one no longer holds.</summary>
        public static void ForgetCopy(PooleeSyncable poolee) {
            try { applied.Remove(poolee.GetInstanceID()); }
            catch { }
        }

        public static void Apply(ushort id, string signature) {
            if (Node.isServer || string.IsNullOrEmpty(signature))
                return;
            MelonCoroutines.Start(CoApply(id, signature));
        }

        static IEnumerator CoApply(ushort id, string signature) {
            ushort scene = SceneEventSync.CurrentSceneTag();
            float waited = 0f;
            // Its copy may still be being built (the spawn went out just before this), or still be
            // dead from its last life until its respawn here restarts it.
            while (true) {
                PooleeSyncable poolee;
                if (PooleeSyncable._PooleeLookup.TryGetValue(id, out poolee) && ObjectSync.Live(poolee)) {
                    bool active = false;
                    try { active = poolee.gameObject.activeInHierarchy; }
                    catch { }
                    if (active && ApplyTo(poolee, signature))
                        yield break;
                }
                if (waited >= ApplyWindow || SceneEventSync.CurrentSceneTag() != scene)
                    yield break;
                yield return null;
                waited += Time.unscaledDeltaTime;
            }
        }

        // True once nothing is left to do for this copy; false to try again shortly.
        static bool ApplyTo(PooleeSyncable poolee, string signature) {
            try {
                int key = poolee.GetInstanceID();
                string last;
                if (applied.TryGetValue(key, out last) && last == signature)
                    return true;

                int bar = signature.LastIndexOf('|');
                string name = bar >= 0 ? signature.Substring(0, bar) : signature;
                string scaleText = bar >= 0 ? signature.Substring(bar + 1) : "";

                Vector3 scale;
                if (TryParseScale(scaleText, out scale) && (poolee.transform.localScale - scale).sqrMagnitude > 0.0001f)
                    poolee.transform.localScale = scale;

                if (string.IsNullOrEmpty(name)) {
                    applied[key] = signature;
                    return true;
                }
                AIBrain brain = poolee.GetComponentInChildren<AIBrain>(true);
                if (!brain)
                    return true;
                // Set up on a living body only, the way the host's own was.
                if (brain.isDead || NpcStateSync.IsDead(brain))
                    return false;
                PuppetMasta.BaseEnemyConfig config = FindConfig(name);
                if (config == null) {
                    if (missing.Add(name))
                        NpcTrace.Log("#" + poolee.id + " was set up as " + name + " on the host; that config isn't loaded here");
                    return false;
                }
                brain.SetBaseConfig(config);
                applied[key] = signature;
                appliedCount++;
                NpcTrace.Log("#" + poolee.id + " set up as " + name + ", as on the host");
                return true;
            }
            catch (System.Exception e) {
                EntangleLogger.Debug("[NpcConfig] #" + poolee.id + ": " + e.Message);
                return true;
            }
        }

        static bool TryParseScale(string text, out Vector3 scale) {
            scale = Vector3.one;
            string[] parts = (text ?? "").Split(',');
            if (parts.Length != 3)
                return false;
            CultureInfo inv = CultureInfo.InvariantCulture;
            float x, y, z;
            if (!float.TryParse(parts[0], NumberStyles.Float, inv, out x)
                || !float.TryParse(parts[1], NumberStyles.Float, inv, out y)
                || !float.TryParse(parts[2], NumberStyles.Float, inv, out z))
                return false;
            // Nothing a level would do; a bad number would crush or blow up the copy.
            if (x < 0.05f || y < 0.05f || z < 0.05f || x > 20f || y > 20f || z > 20f)
                return false;
            scale = new Vector3(x, y, z);
            return true;
        }

        static PuppetMasta.BaseEnemyConfig FindConfig(string name) {
            PuppetMasta.BaseEnemyConfig config;
            if (configs.TryGetValue(name, out config) && config != null)
                return config;
            if (Time.realtimeSinceStartup < nextConfigScan)
                return null;
            nextConfigScan = Time.realtimeSinceStartup + 2f;

            try {
                UnhollowerBaseLib.Il2CppReferenceArray<Object> found =
                    Resources.FindObjectsOfTypeAll(UnhollowerRuntimeLib.Il2CppType.Of<PuppetMasta.BaseEnemyConfig>());
                for (int i = 0; found != null && i < found.Length; i++) {
                    PuppetMasta.BaseEnemyConfig item = found[i] ? found[i].TryCast<PuppetMasta.BaseEnemyConfig>() : null;
                    if (item != null && !string.IsNullOrEmpty(item.name))
                        configs[item.name] = item;
                }
            }
            catch { }
            return configs.TryGetValue(name, out config) ? config : null;
        }

        public static string Summary() => "configs applied to " + appliedCount + " copies";
    }

    [HarmonyPatch(typeof(AIBrain), "SetBaseConfig")]
    public static class AIBrainSetBaseConfigPatch
    {
        public static void Postfix(AIBrain __instance, PuppetMasta.BaseEnemyConfig config) {
            try { NpcConfigSync.OnConfigSet(__instance, config); }
            catch (System.Exception e) { Entanglement.Patching.PatchGuard.Report("AIBrainSetBaseConfigPatch", e); }
        }
    }
}
