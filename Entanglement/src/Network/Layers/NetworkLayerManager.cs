using System;

namespace Entanglement.Network.Layers
{
    /// <summary>
    /// Picks and owns the active <see cref="INetworkLayer"/>. Today: Steam.
    /// Tomorrow: Meta Quest / Oculus / proxy layers without rewriting gameplay.
    /// </summary>
    public static class NetworkLayerManager
    {
        public static INetworkLayer Layer { get; private set; }

        public static string ActiveTitle => Layer != null ? Layer.Title : "None";
        public static string ActivePlatform => Layer != null ? Layer.Platform : "None";

        /// <summary>Preferred layer name from prefs / launch args. "Steam" default; "Meta" reserved.</summary>
        public static string PreferredLayerName = "Steam";

        public static void Initialize() {
            Layer = CreatePreferred();
            try { Layer.Initialize(); }
            catch (Exception e) {
                EntangleLogger.Error("[NetLayer] Initialize failed: " + e.Message);
            }
        }

        static INetworkLayer CreatePreferred() {
            string name = PreferredLayerName ?? "Steam";
            if (string.Equals(name, "Meta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Oculus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Quest", StringComparison.OrdinalIgnoreCase)) {
                EntangleLogger.Warn("[NetLayer] Meta/Quest transport is not implemented yet — falling back to Steam. "
                    + "Gameplay already talks through INetworkLayer so a Meta layer can drop in.");
            }
            return new SteamNetworkLayer();
        }

        /// <summary>Swap transport at runtime (e.g. after a Meta SDK loads). Hosts should not swap mid-lobby.</summary>
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
