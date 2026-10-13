namespace Entanglement.Gamemodes.BuiltIn
{
    /// <summary>Free-for-all sandbox PvP. Template for richer Fusion-style modes.</summary>
    public class DeathmatchGamemode : Gamemode
    {
        public override string Title => "Deathmatch";
        public override string Description => "Free-for-all. Everyone can attack everyone.";
        public override int MinPlayers => 2;

        public override bool CanAttack(long attackerId, long victimId) => true;
    }
}
