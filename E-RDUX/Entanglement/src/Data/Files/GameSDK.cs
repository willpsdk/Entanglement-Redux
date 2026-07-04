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
            // Extracts the Steamworks native library if its missing
            string sdkPath = PersistentData.GetPath("steam_api64.dll");
            if (!File.Exists(sdkPath))
            {
                EntangleLogger.Log("steam_api64.dll was missing, autoextracting it!");
                File.WriteAllBytes(sdkPath, EmbeddedResource.LoadFromAssembly(EntanglementMod.entanglementAssembly, "Entanglement.resources.steam_api64.dll"));
            }

            // SUPER SKETCHY but this is a fix for R2ModMan, instead of waiting for DllImport we just invoke it ourselves :)
            // If BONEWORKS already loaded its own steam_api64.dll this is a no-op and the game's copy is used instead
            _ = DllTools.LoadLibrary(sdkPath);
        }
    }
}
