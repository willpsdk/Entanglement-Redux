namespace Entanglement.Gamemodes.BuiltIn
{
    /// <summary>Fusion Team Deathmatch placeholder — same PvP gate until teams land.</summary>
    public class TeamDeathmatchGamemode : Gamemode
    {
        public override string Title => "Team Deathmatch";
        public override string Description => "Team vs team. Teams wiring comes later; combat is open for now.";
        public override int MinPlayers => 2;

        public override bool CanAttack(long attackerId, long victimId) {
            if (attackerId == victimId)
                return false;
            return true;
        }
    }
}
