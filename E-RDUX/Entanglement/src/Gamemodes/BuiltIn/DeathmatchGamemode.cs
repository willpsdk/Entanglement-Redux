using UnityEngine;

namespace Entanglement.Gamemodes.BuiltIn
{
    public class DeathmatchGamemode : EntanglementGamemode
    {
        public override string Id => "deathmatch";
        public override string DisplayName => "Deathmatch";
        public override Color MenuColor => Color.red;

        public override float DefaultRoundSeconds => 10f * 60f;

        public override void OnPlayerKilled(long killerId, long victimId) {
            if (killerId == victimId || killerId == 0) return;
            AddScore(killerId, 1);
        }

        public override void OnRoundEnd() {
            EntangleLogger.Log("[Deathmatch] Round over");
        }
    }
}
