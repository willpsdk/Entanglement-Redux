namespace Entanglement.Network.Layers
{
    /// <summary>
    /// Steam application IDs used by transport layers.
    /// SteamVR (250820) is Fusion's Meta PCVR / Horizon Link identity so Meta Link
    /// users share the same Steamworks networking space as SteamVR.
    /// </summary>
    public static class SteamAppIds
    {
        /// <summary>SteamVR — used for Meta Horizon Link (PC).</summary>
        public const uint SteamVR = 250820;
    }
}
