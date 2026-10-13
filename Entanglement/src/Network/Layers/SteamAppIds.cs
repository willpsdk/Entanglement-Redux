namespace Entanglement.Network.Layers
{
    /// <summary>
    /// Steam application IDs used by transport layers.
    /// SteamVR (250820) is Fusion's Meta PCVR / Horizon Link identity so Quest proxy
    /// and PC Meta users share the same Steamworks networking space.
    /// </summary>
    public static class SteamAppIds
    {
        /// <summary>SteamVR — used for Meta Horizon Link (PC) and Quest proxy companion.</summary>
        public const uint SteamVR = 250820;
    }
}
