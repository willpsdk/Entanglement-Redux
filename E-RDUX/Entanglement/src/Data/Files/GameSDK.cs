using System;
using System.IO;
using System.Reflection;

using Entanglement.Modularity;

using MelonLoader;

namespace Entanglement.Data
{
    public class GameSDK
    {
        public static void LoadGameSDK()
        {
            // Extract to %AppData%/EntanglementMod/ if missing or zero-byte (a failed write would
            // otherwise sit there forever, since File.Exists can't tell a good file from an empty one)
            string sdkPath = PersistentData.GetPath("steam_api64.dll");
            if (!File.Exists(sdkPath) || new FileInfo(sdkPath).Length == 0)
            {
                EntangleLogger.Log($"steam_api64.dll missing or empty at {sdkPath}, autoextracting it!");

                byte[] sdkBytes = EmbeddedResource.LoadFromAssembly(EntanglementMod.entanglementAssembly, "Entanglement.resources.steam_api64.dll");
                if (sdkBytes == null || sdkBytes.Length == 0)
                {
                    EntangleLogger.Error("Embedded steam_api64.dll resource is missing from this build! Steam will fail to initialize unless steam_api64.dll is placed in the BONEWORKS root folder manually.");
                    return;
                }

                try
                {
                    File.WriteAllBytes(sdkPath, sdkBytes);
                    EntangleLogger.Log($"Wrote {sdkBytes.Length / 1024}KB to {sdkPath}");
                }
                catch (Exception e)
                {
                    EntangleLogger.Error($"Failed to write steam_api64.dll to {sdkPath}: {e.Message}\nSteam will fail to initialize unless steam_api64.dll is placed in the BONEWORKS root folder manually.");
                    return;
                }
            }

            // SUPER SKETCHY but this is a fix for R2ModMan, instead of waiting for DllImport we just invoke it ourselves :)
            // Preloading it means later DllImport("steam_api64") calls find it already loaded by name
            // instead of searching next to BONEWORKS.exe. If the game loaded its own copy this is a no-op.
            IntPtr handle = DllTools.LoadLibrary(sdkPath);
            if (handle == IntPtr.Zero)
                EntangleLogger.Error($"LoadLibrary failed for {sdkPath} (Win32 error {DllTools.GetLastError()}). Steam will likely fail to initialize unless steam_api64.dll is placed in the BONEWORKS root folder manually.");
            else
                EntangleLogger.Log($"Preloaded steam_api64.dll from {sdkPath}");
        }
    }
}
