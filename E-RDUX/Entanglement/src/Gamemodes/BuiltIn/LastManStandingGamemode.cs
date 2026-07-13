using System.Collections.Generic;

using UnityEngine;

using Entanglement.Network;

namespace Entanglement.Gamemodes.BuiltIn
{
    public class LastManStandingGamemode : EntanglementGamemode
    {
        public override string Id => "last_man_standing";
        public override string DisplayName => "Last Man Standing";
        public override Color MenuColor => new Color(1f, 0.55f, 0f);
        public override float DefaultRoundSeconds => 5f * 60f;
        public override bool EliminationMode => true;

        const int survivalBonus = 5;

        static IEnumerable<long> AllPlayers() {
            yield return SteamIntegration.currentUserId;
            if (Node.activeNode == null) yield break;
            foreach (long userId in Node.activeNode.connectedUsers)
                yield return userId;
        }

        public override void OnPlayerKilled(long killerId, long victimId) {
            if (killerId != victimId && killerId != 0)
                AddScore(killerId, 1);

            int totalPlayers = 0;
            int aliveCount = 0;
            long lastAlive = 0;

            foreach (long userId in AllPlayers()) {
                totalPlayers++;
                if (userId == victimId || GamemodeHandler.eliminated.Contains(userId)) continue;
                aliveCount++;
                lastAlive = userId;
            }

            // solo testing shouldn't auto-end the round - there's nobody left to declare a winner
            if (totalPlayers <= 1) return;

            if (aliveCount <= 1) {
                if (aliveCount == 1) AddScore(lastAlive, survivalBonus);
                EndRound();
            }
        }

        public override void OnRoundEnd() {
            EntangleLogger.Log("[Last Man Standing] Round over");
        }
    }
}
