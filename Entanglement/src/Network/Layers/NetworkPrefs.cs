using MelonLoader;

using Entanglement.Utilities;

namespace Entanglement.Network.Layers
{
    /// <summary>Persisted network-layer choice (Fusion ClientSettings NetworkLayerTitle / ProxyPort).</summary>
    public static class NetworkPrefs
    {
        static readonly MelonPreferences_Category category =
            MelonPreferences.CreateCategory("EntanglementNetwork");

        public static readonly MelonPreferences_Entry<string> networkLayerTitle =
            category.CreateEntry(
                "networkLayerTitle",
                "",
                description: "Transport layer: Steam, SteamVR (Meta Horizon Link), or Proxy SteamVR (Quest). Empty = platform default.");

        public static readonly MelonPreferences_Entry<int> proxyPort =
            category.CreateEntry(
                "proxyPort",
                28340,
                description: "UDP port for Quest proxy discovery (Entanglement Helper on PC). Default 28340 like Fusion.");

        static NetworkPrefs() {
            category.SaveToFile(false);
        }

        public static string ResolvedLayerTitle() {
            string saved = networkLayerTitle.Value;
            if (!string.IsNullOrEmpty(saved))
                return saved;
            return PlatformHelper.IsAndroid ? "Proxy SteamVR" : "Steam";
        }

        public static int ResolvedProxyPort() {
            int port = proxyPort.Value;
            if (port < 1024 || port > 65535)
                return 28340;
            return port;
        }
    }
}
