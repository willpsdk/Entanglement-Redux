using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Patching;

namespace Entanglement.Objects
{
    public static class NpcDespawnSync
    {
        const float CheckInterval = 0.5f;
        static float nextCheck;

        static readonly Dictionary<ushort, bool> wasActive = new Dictionary<ushort, bool>();
        static readonly Dictionary<ushort, bool> isNpc = new Dictionary<ushort, bool>();

        public static void OnSceneCleanup() {
            wasActive.Clear();
            isNpc.Clear();
            nextCheck = 0f;
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || Server.instance == null)
                return;

            float now = Time.realtimeSinceStartup;
            if (now < nextCheck)
                return;
            nextCheck = now + CheckInterval;

            // Working out whether a new spawn is an NPC searches its whole hierarchy. A wave or a pile of
            // broken crates brings dozens at once (a 104ms hitch on Oct 8), so only a few are looked at
            // per pass; the rest wait half a second.
            int classifyBudget = ClassifyPerPass;
            scratch.Clear();
            scratch.AddRange(PooleeSyncable._PooleeLookup.Values);
            foreach (PooleeSyncable poolee in scratch) {
                try {
                    if (!poolee || poolee.id == 0 || poolee.pathAddressed)
                        continue;

                    bool npc;
                    if (!isNpc.TryGetValue(poolee.id, out npc)) {
                        if (classifyBudget <= 0)
                            continue;
                        classifyBudget--;
                        npc = InstantiatePatch.IsSceneNpc(poolee.gameObject);
                        isNpc[poolee.id] = npc;
                    }
                    if (!npc)
                        continue;

                    bool active = poolee.gameObject.activeInHierarchy;
                    bool before;
                    wasActive.TryGetValue(poolee.id, out before);
                    wasActive[poolee.id] = active;

                    // A client fizzles its copy if it is dead there and lets it go if not
                    // (PuzzlePatches.ApplyNpcEvent). One our own fizzle put away was announced already.
                    if (before && !active) {
                        SceneEventSync.SendEvent(SceneEventType.NpcDespawn, poolee.transform, poolee.id, false);
                        NpcTrace.Log("despawned " + NpcTrace.Name(poolee.gameObject) + " #" + poolee.id + ", told clients");
                    }
                }
                catch { }
            }
            scratch.Clear();
        }

        const int ClassifyPerPass = 8;
        static readonly List<PooleeSyncable> scratch = new List<PooleeSyncable>();
    }
}
