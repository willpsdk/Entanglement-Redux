using System.Collections.Generic;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.Objects
{
    public static class NpcOwnerWatch
    {
        const float CheckInterval = 1f;

        // A living NPC's owner sends its bones at 6 Hz or more while it moves, and again every 2s while
        // it stands still (TransformSyncable.NpcKeepAlive), so this is two of those missed.
        const float SilenceSeconds = 5f;

        // Right after an owner change, bones still on their way from the previous owner are normal.
        const float SettleSeconds = 1.5f;
        // Bones from the wrong player have to keep coming this long before anything is done.
        const float SustainSeconds = 1f;
        const float ReassertCooldown = 2f;

        static float nextCheck;
        static int reclaims;
        static int reasserts;
        static readonly List<PooleeSyncable> scratch = new List<PooleeSyncable>();

        public static void OnSceneCleanup() {
            nextCheck = 0f;
            scratch.Clear();
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby || Server.instance == null)
                return;
            if (PlayerRepresentation.RemoteCount == 0)
                return;

            float now = Time.realtimeSinceStartup;
            if (now < nextCheck)
                return;
            nextCheck = now + CheckInterval;

            scratch.Clear();
            foreach (PooleeSyncable poolee in PooleeSyncable._PooleeLookup.Values)
                scratch.Add(poolee);

            for (int i = 0; i < scratch.Count; i++) {
                try {
                    PooleeSyncable poolee = scratch[i];
                    if (!ObjectSync.Live(poolee))
                        continue;
                    if (!poolee.OwnerSilentFor(SilenceSeconds))
                        continue;

                    poolee.ReclaimForHost();
                    reclaims++;
                    NpcTrace.Log("owner watch: took #" + poolee.id + " back, its owner sent nothing for " + SilenceSeconds + "s");
                }
                catch { }
            }
            scratch.Clear();
        }

        public static void NotePose(TransformSyncable sync, long sender) {
            if (Server.instance == null || ReferenceEquals(sync, null))
                return;
            if (sync.staleOwner == sender)
                return;
            PooleeSyncable npc = sync.npcRoot;
            if (ReferenceEquals(npc, null))
                return;

            try {
                if (!ObjectSync.Live(npc))
                    return;
                if (npc.ReassertOwner(sender, SettleSeconds, SustainSeconds, ReassertCooldown)) {
                    reasserts++;
                    NpcTrace.Log("owner watch: #" + npc.id + " was also being run by " + sender + ", owner restated");
                }
            }
            catch { }
        }

        /// <summary>For the state log.</summary>
        public static string Summary() => "owner watch: " + reclaims + " reclaimed, " + reasserts + " restated";
    }
}
