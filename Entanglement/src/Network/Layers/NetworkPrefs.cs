using MelonLoader;

namespace Entanglement.Network.Layers
{
    /// <summary>Persisted network-layer choice: Steam (Boneworks) or SteamVR (Meta Horizon Link).</summary>
    public static class NetworkPrefs
    {
        static readonly MelonPreferences_Category category =
            MelonPreferences.CreateCategory("EntanglementNetwork");

        public static readonly MelonPreferences_Entry<string> networkLayerTitle =
            category.CreateEntry(
                "networkLayerTitle",
                "",
                description: "Transport layer: Steam or SteamVR (Meta Horizon Link). Empty = Steam.");

        static NetworkPrefs() {
            category.SaveToFile(false);
        }

        public static string ResolvedLayerTitle() {
            string saved = networkLayerTitle.Value;
            if (!string.IsNullOrEmpty(saved)) {
                // Migrate removed Quest/Proxy prefs to SteamVR (Meta) or Steam.
                if (saved.StartsWith("Proxy", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(saved, "Quest", System.StringComparison.OrdinalIgnoreCase)) {
                    networkLayerTitle.Value = "SteamVR";
                    MelonPreferences.Save();
                    return "SteamVR";
                }
                return saved;
            }
            return "Steam";
        }
    }
}
