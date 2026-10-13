using UnityEngine;

using MelonLoader;

namespace Entanglement.Utilities
{
    /// <summary>Fusion-style platform checks for layer defaults (Quest vs PC).</summary>
    public static class PlatformHelper
    {
        static readonly bool isAndroidCached = DetectAndroid();

        public static bool IsAndroid => isAndroidCached;

        public const string AndroidName = "QUEST";
        public const string WindowsName = "PC";

        public static string GetPlatformName() => IsAndroid ? AndroidName : WindowsName;

        static bool DetectAndroid() {
            try {
                if (Application.platform == RuntimePlatform.Android)
                    return true;
            }
            catch { }

            try {
                // MelonLoader: CompatiblePlatforms.ANDROID == 3
                return MelonUtils.CurrentPlatform == (MelonPlatformAttribute.CompatiblePlatforms)3;
            }
            catch {
                return false;
            }
        }
    }
}
