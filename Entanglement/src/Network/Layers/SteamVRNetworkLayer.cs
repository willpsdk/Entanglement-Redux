namespace Entanglement.Network.Layers
{
    /// <summary>
    /// PC Meta Horizon Link path (Fusion SteamVRNetworkLayer).
    /// Uses SteamVR's Steam AppID (250820) so Meta PCVR can run Steamworks networking
    /// and crossplay with Quest Proxy SteamVR users.
    /// </summary>
    public sealed class SteamVRNetworkLayer : SteamNetworkLayer
    {
        public override string Title => "SteamVR";

        public override uint ApplicationID => SteamAppIds.SteamVR;

        public override void Initialize() {
            EntangleLogger.Log("[NetLayer] Active transport: SteamVR / Meta Horizon Link (AppID "
                + ApplicationID + ")");
        }
    }
}
