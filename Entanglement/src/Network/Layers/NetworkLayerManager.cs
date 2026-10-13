using System;

using Entanglement.Network.Layers.Proxy;
using Entanglement.Utilities;

namespace Entanglement.Network.Layers
{
    /// <summary>
    /// Fusion NetworkLayerDeterminer + Manager: picks Steam, SteamVR (Meta Horizon Link),
    /// or Proxy SteamVR (Quest) without rewriting gameplay messages.
    /// </summary>
    public static class NetworkLayerManager
    {
        public static INetworkLayer Layer { get; private set; }

        public static string ActiveTitle => Layer != null ? Layer.Title : "None";
        public static string ActivePlatform => Layer != null ? Layer.Platform : "None";

        /// <summary>Preferred layer name before Initialize. Filled from NetworkPrefs / launch.</summary>
        public static string PreferredLayerName;

        public static readonly string[] SelectableTitles = {
            "Steam",
            "SteamVR",
            "Proxy SteamVR",
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

        /// <summary>Fusion GetDefaultLayer: Android → Proxy SteamVR, PC → Steam (Boneworks-native).</summary>
        public static string GetDefaultTitle() {
            if (PlatformHelper.IsAndroid)
                return "Proxy SteamVR";
            return "Steam";
        }

        public static bool IsProxyTitle(string title) {
            if (string.IsNullOrEmpty(title))
                return false;
            return title.StartsWith("Proxy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(title, "Quest", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSteamVRTitle(string title) {
            return string.Equals(title, "SteamVR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(title, "Meta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(title, "Horizon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(title, "Oculus", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>AppID to pass into SteamIntegration before layer init. 0 = game default.</summary>
        public static uint SteamAppIdForTitle(string title) {
            if (IsProxyTitle(title))
                return 0;
            if (IsSteamVRTitle(title))
                return SteamAppIds.SteamVR;
            return 0;
        }

        static INetworkLayer CreatePreferred() {
            string name = PreferredLayerName ?? GetDefaultTitle();
            INetworkLayer layer = CreateByTitle(name);
            if (layer == null) {
                EntangleLogger.Warn("[NetLayer] Unknown layer '" + name + "', using default.");
                layer = CreateByTitle(GetDefaultTitle());
            }

            if (!Validate(layer, out string reason)) {
                EntangleLogger.Warn("[NetLayer] " + layer.Title + " not valid (" + reason + "), falling back.");
                layer = FallbackFrom(layer);
            }

            return layer;
        }

        static INetworkLayer CreateByTitle(string name) {
            if (string.IsNullOrEmpty(name))
                return null;

            if (IsProxyTitle(name)
                || string.Equals(name, "Proxy SteamVR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Quest", StringComparison.OrdinalIgnoreCase))
                return new ProxySteamVRNetworkLayer();

            if (IsSteamVRTitle(name))
                return new SteamVRNetworkLayer();

            if (string.Equals(name, "Steam", StringComparison.OrdinalIgnoreCase))
                return new SteamNetworkLayer();

            return null;
        }

        static bool Validate(INetworkLayer layer, out string reason) {
            reason = null;
            if (layer is ProxyNetworkLayer) {
                if (!PlatformHelper.IsAndroid) {
                    // Allow forcing Proxy on PC for helper testing.
                    reason = null;
                    return true;
                }
                return true;
            }
            if (layer is SteamVRNetworkLayer || layer is SteamNetworkLayer) {
                if (PlatformHelper.IsAndroid) {
                    reason = "local Steamworks not on Quest";
                    return false;
                }
                return true;
            }
            reason = "unknown";
            return false;
        }

        static INetworkLayer FallbackFrom(INetworkLayer layer) {
            // Quest never has local Steamworks — always the proxy companion path.
            if (PlatformHelper.IsAndroid)
                return new ProxySteamVRNetworkLayer();

            if (layer is ProxyNetworkLayer)
                return new SteamVRNetworkLayer();
            if (layer is SteamVRNetworkLayer)
                return new SteamNetworkLayer();
            return new SteamVRNetworkLayer();
        }

        /// <summary>Cycle Steam → SteamVR → Proxy SteamVR and persist. Restart advised for AppID change.</summary>
        public static string CyclePreferredTitle() {
            string current = NetworkPrefs.networkLayerTitle.Value;
            if (string.IsNullOrEmpty(current))
                current = ActiveTitle;

            int index = 0;
            for (int i = 0; i < SelectableTitles.Length; i++) {
                if (string.Equals(SelectableTitles[i], current, StringComparison.OrdinalIgnoreCase)) {
                    index = i;
                    break;
                }
            }
            index = (index + 1) % SelectableTitles.Length;
            string next = SelectableTitles[index];
            NetworkPrefs.networkLayerTitle.Value = next;
            PreferredLayerName = next;
            MelonLoader.MelonPreferences.Save();
            EntangleLogger.Log("[NetLayer] Preferred transport set to " + next
                + " (applies fully on next launch for AppID switches)");
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
