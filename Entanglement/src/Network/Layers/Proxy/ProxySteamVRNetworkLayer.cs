namespace Entanglement.Network.Layers.Proxy
{
    /// <summary>
    /// Quest path: Steamworks networking via PC companion using SteamVR AppID 250820.
    /// Same identity space as <see cref="SteamVRNetworkLayer"/> on Meta Horizon Link PC.
    /// </summary>
    public sealed class ProxySteamVRNetworkLayer : ProxyNetworkLayer
    {
        public override uint ApplicationID => SteamAppIds.SteamVR;

        public override string Title => "Proxy SteamVR";

        public override string Platform => "Steam";
    }
}
