using System;
using System.Net;
using System.Net.Http;
using System.Threading;

using Entanglement.UI;

namespace Entanglement
{
    public static class UpdateCheck
    {
        const string packageUrl = "https://thunderstore.io/api/experimental/package/willpsdk/Entanglement_Redux/";

        static string pendingMessage;
        static int started;

        public static void Start() {
            if (Interlocked.Exchange(ref started, 1) != 0)
                return;

            Thread thread = new Thread(Run) {
                IsBackground = true,
                Name = "EntanglementUpdateCheck"
            };
            thread.Start();
        }

        public static void Tick() {
            string message = Interlocked.Exchange(ref pendingMessage, null);
            if (!string.IsNullOrEmpty(message))
                GameNotify.Show(message, 8f);
        }

        static void Run() {
            try {
                HttpClientHandler handler = new HttpClientHandler() {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
                HttpClient client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(8);

                string json = client.GetStringAsync(packageUrl).GetAwaiter().GetResult();
                client.Dispose();
                handler.Dispose();

                string latest = ReadLatestVersion(json);
                if (string.IsNullOrEmpty(latest)) {
                    EntangleLogger.Warn("Thunderstore didn't return a version for Entanglement Redux.");
                    return;
                }

                if (!System.Version.TryParse(EntanglementMod.VersionString, out System.Version local) ||
                    !System.Version.TryParse(latest, out System.Version remote)) {
                    EntangleLogger.Warn($"Couldn't compare versions (yours {EntanglementMod.VersionString}, Thunderstore {latest}).");
                    return;
                }

                int compare = local.CompareTo(remote);
                if (compare < 0) {
                    EntangleLogger.Warn($"Entanglement Redux is out of date. Yours: {local}. Latest: {remote}.");
                    pendingMessage = $"Entanglement Redux is out of date. Yours: {local}. Latest: {remote}.";
                }
                else if (compare == 0)
                    EntangleLogger.Log($"You're on the latest Entanglement Redux ({local}).");
                else
                    EntangleLogger.Log($"This build ({local}) is newer than Thunderstore ({remote}).");
            }
            catch (Exception e) {
                EntangleLogger.Warn($"Couldn't reach Thunderstore to check for an update ({e.GetType().Name}).");
            }
        }

        static string ReadLatestVersion(string json) {
            if (string.IsNullOrEmpty(json))
                return null;

            int latest = json.IndexOf("\"latest\"", StringComparison.Ordinal);
            if (latest < 0)
                return null;

            const string key = "\"version_number\":\"";
            int start = json.IndexOf(key, latest, StringComparison.Ordinal);
            if (start < 0)
                return null;

            start += key.Length;
            int end = json.IndexOf('"', start);
            if (end < 0)
                return null;

            return json.Substring(start, end - start);
        }
    }
}
