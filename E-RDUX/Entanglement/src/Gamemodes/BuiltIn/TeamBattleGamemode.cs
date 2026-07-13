using UnityEngine;

using Entanglement.Network;

namespace Entanglement.Gamemodes.BuiltIn
{
    public class TeamBattleGamemode : EntanglementGamemode
    {
        public override string Id => "team_battle";
        public override string DisplayName => "Team Battle";
        public override Color MenuColor => Color.blue;
        public override bool UsesTeams => true;
        public override int TeamCount => 2;

        public override float DefaultRoundSeconds => 10f * 60f;
        public const int scoreToWin = 25;

        static readonly Color[] teamColors = {
            new Color(1f, 0.35f, 0.3f),  // red
            new Color(0.35f, 0.55f, 1f), // blue
            new Color(0.4f, 1f, 0.4f),   // green
            new Color(1f, 0.9f, 0.3f),   // yellow
        };

        public override Color GetTeamColor(byte team) => team < teamColors.Length ? teamColors[team] : Color.white;

        byte nextTeam;

        public override void OnModeStart() {
            nextTeam = 0;

            SetTeam(SteamIntegration.currentUserId, nextTeam);
            nextTeam = (byte)((nextTeam + 1) % TeamCount);

            if (Node.activeNode != null) {
                foreach (long userId in Node.activeNode.connectedUsers) {
                    SetTeam(userId, nextTeam);
                    nextTeam = (byte)((nextTeam + 1) % TeamCount);
                }
            }
        }

        public override void OnPlayerJoined(long userId) {
            SetTeam(userId, nextTeam);
            nextTeam = (byte)((nextTeam + 1) % TeamCount);
        }

        public override void OnPlayerKilled(long killerId, long victimId) {
            if (killerId == victimId || killerId == 0) return;
            if (!GamemodeHandler.teams.TryGetValue(killerId, out byte killerTeam)) return;

            int teamScore = 0;
            foreach (var pair in GamemodeHandler.scores)
                if (GamemodeHandler.teams.TryGetValue(pair.Key, out byte t) && t == killerTeam)
                    teamScore += pair.Value;

            AddScore(killerId, 1);

            if (teamScore + 1 >= scoreToWin)
                EndRound();
        }

        public override void OnRoundEnd() {
            EntangleLogger.Log("[Team Battle] Round over");
        }
    }
}
