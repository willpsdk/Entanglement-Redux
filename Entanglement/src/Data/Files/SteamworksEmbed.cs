using System;
using System.IO;
using System.Reflection;

using MelonLoader;

namespace Entanglement.Data
{
    public static class SteamworksEmbed
    {
        public const string SteamworksResource = "Entanglement.resources.Steamworks.NET.dll";
        public const string SteamApiResource = "Entanglement.resources.steam_api64.dll";

        static Assembly steamworksAssembly;
        static bool resolveHooked;

        public static void HookResolve() {
            if (resolveHooked)
                return;
            resolveHooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
            EnsureSteamworksOnDisk();
        }

        static Assembly OnResolve(object sender, ResolveEventArgs args) {
            try {
                if (new AssemblyName(args.Name).Name != "Steamworks.NET")
                    return null;
            }
            catch {
                return null;
            }

            if (steamworksAssembly != null)
                return steamworksAssembly;

            steamworksAssembly = LoadSteamworks();
            return steamworksAssembly;
        }

        public static void EnsureSteamworksOnDisk() {
            try {
                byte[] bytes = EmbeddedResource.LoadFromAssembly(Assembly.GetExecutingAssembly(), SteamworksResource);
                if (bytes == null || bytes.Length == 0) {
                    MelonLogger.Error("[Entanglement] Embedded Steamworks.NET.dll is missing from this build.");
                    return;
                }

                string gameDir = ResolveGameDirectory();
                if (string.IsNullOrEmpty(gameDir)) {
                    MelonLogger.Warning("[Entanglement] Could not work out the game directory yet; "
                        + "Steamworks.NET.dll will be extracted on the next start.");
                    return;
                }

                string managedPath = Path.Combine(gameDir, "MelonLoader", "Managed", "Steamworks.NET.dll");
                WriteIfMissingOrStale(managedPath, bytes);
            }
            catch (Exception e) {
                MelonLogger.Warning("[Entanglement] Could not extract Steamworks.NET.dll: " + e.Message);
            }
        }

        static string ResolveGameDirectory() {
            try {
                string dir = MelonUtils.GameDirectory;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }
            catch { }

            try {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(Path.Combine(dir, "MelonLoader")))
                    return dir;
            }
            catch { }

            return null;
        }

        public static Assembly LoadSteamworks() {
            try {
                string managedPath = Path.Combine(MelonUtils.GameDirectory, "MelonLoader", "Managed", "Steamworks.NET.dll");
                if (File.Exists(managedPath)) {
                    steamworksAssembly = Assembly.LoadFrom(managedPath);
                    if (steamworksAssembly != null)
                        return steamworksAssembly;
                }
            }
            catch { }

            try {
                byte[] bytes = EmbeddedResource.LoadFromAssembly(Assembly.GetExecutingAssembly(), SteamworksResource);
                if (bytes != null && bytes.Length > 0) {
                    steamworksAssembly = Assembly.Load(bytes);
                    return steamworksAssembly;
                }
            }
            catch (Exception e) {
                MelonLogger.Error("[Entanglement] Failed loading embedded Steamworks.NET.dll: " + e.Message);
            }

            return null;
        }

        static void WriteIfMissingOrStale(string path, byte[] bytes) {
            try {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(path)) {
                    long len = new FileInfo(path).Length;
                    if (len == bytes.Length)
                        return;
                }

                File.WriteAllBytes(path, bytes);
                MelonLogger.Msg("[Entanglement] Extracted Steamworks.NET.dll to " + path);
            }
            catch (Exception e) {
                MelonLogger.Warning("[Entanglement] Could not write " + path + ": " + e.Message);
            }
        }
    }
}
