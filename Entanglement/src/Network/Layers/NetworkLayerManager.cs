using System;

namespace Entanglement.Network.Layers
{
    /// <summary>
    /// Picks Steam (Boneworks AppID) or SteamVR (Meta Horizon Link AppID 250820).
    /// </summary>
    public static class NetworkLayerManager
    {
        public static INetworkLayer Layer { get; private set; }

        public static string ActiveTitle => Layer != null ? Layer.Title : "None";
        public static string ActivePlatform => Layer != null ? Layer.Platform : "None";

        public static string PreferredLayerName;

        public static readonly string[] SelectableTitles = {
            "Steam",
            "SteamVR",
            "Meta",
        };

        public static void Initialize() {
            if (string.IsNullOrEmpty(PreferredLayerName))
                PreferredLayerName = NetworkPrefs.ResolvedLayerTitle();

            Layer = CreatePreferred();
            try { Layer.Initialize(); }
            catch (Exception e) {
                EntangleLogger.Error("[NetLayer] Initialize failed: " + e.Message);
            }
        }

        public static string GetDefaultTitle() => "Steam";

        public static bool IsSteamVRTitle(string title) {
            return string.Equals(title, "SteamVR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(title, "Meta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(title, "Horizon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(title, "Oculus", StringComparison.OrdinalIgnoreCase);
        }

        public static uint SteamAppIdForTitle(string title) {
            if (IsSteamVRTitle(title))
                return SteamAppIds.SteamVR;
            return 0;
        }

        static INetworkLayer CreatePreferred() {
            string name = PreferredLayerName ?? GetDefaultTitle();
            INetworkLayer layer = CreateByTitle(name);
            if (layer == null) {
                EntangleLogger.Warn("[NetLayer] Unknown layer '" + name + "', using Steam.");
                layer = new SteamNetworkLayer();
            }
            return layer;
        }

        static INetworkLayer CreateByTitle(string name) {
            if (string.IsNullOrEmpty(name))
                return null;
            if (IsSteamVRTitle(name))
                return new SteamVRNetworkLayer();
            if (string.Equals(name, "Steam", StringComparison.OrdinalIgnoreCase))
                return new SteamNetworkLayer();
            return null;
        }

        public static string CyclePreferredTitle() {
            string current = NetworkPrefs.networkLayerTitle.Value;
            if (string.IsNullOrEmpty(current))
                current = ActiveTitle;

            // Cycle Steam ↔ SteamVR (Meta is alias of SteamVR)
            string next = IsSteamVRTitle(current) ? "Steam" : "SteamVR";
            NetworkPrefs.networkLayerTitle.Value = next;
            PreferredLayerName = next;
            MelonLoader.MelonPreferences.Save();
            EntangleLogger.Log("[NetLayer] Preferred transport set to " + next
                + " (restart to apply AppID change)");
            return next;
        }

        public static void SetLayer(INetworkLayer layer) {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));
            Layer = layer;
            EntangleLogger.Log("[NetLayer] Switched transport to " + layer.Title + " (" + layer.Platform + ")");
        }

        public static void Tick() {
            try { Layer?.Tick(); }
            catch (Exception e) { EntangleLogger.Warn("[NetLayer] Tick: " + e.Message); }
        }

        public static void Shutdown() {
            try { Layer?.Shutdown(); }
            catch { }
        }
    }
}
