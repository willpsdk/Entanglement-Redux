namespace Entanglement.Network.Layers
{
    /// <summary>
    /// PC Meta Horizon Link: Steamworks with SteamVR AppID 250820.
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
