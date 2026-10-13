using System;
using System.Diagnostics;
using System.Text;

using UnityEngine;

using Entanglement.Network;

namespace Entanglement.Objects
{
    public static class NpcTrace
    {
        public static bool enabled;

        const int MaxLinesPerScene = 600;
        static int lines;
        static bool warnedCap;

        public static void OnSceneCleanup() {
            lines = 0;
            warnedCap = false;
        }

        static bool Allowed() {
            if (!enabled || !SteamIntegration.hasLobby)
                return false;
            if (lines >= MaxLinesPerScene) {
                if (!warnedCap) {
                    warnedCap = true;
                    EntangleLogger.Log("[NpcTrace] Hit " + MaxLinesPerScene + " lines for this level, going quiet until the next one.");
                }
                return false;
            }
            lines++;
            return true;
        }

        public static void Log(string message) {
            if (!Allowed())
                return;
            EntangleLogger.Log("[NpcTrace] " + (Node.isServer ? "HOST " : "CLIENT ") + message);
        }

        public static string Name(GameObject go) {
            try { return go ? go.name : "null"; }
            catch { return "dead"; }
        }

        /// <summary>A traced syncable changed owner. Logs who called it, so we can see the path.</summary>
        public static void Owner(Syncable sync, long from, long to, string how) {
            if (!enabled || from == to)
                return;

            string name;
            try { name = sync.transform.root.name + "/" + sync.name; }
            catch { name = "?"; }

            Log("owner #" + sync.objectId + " " + name + ": " + from + " -> " + to + " via " + how + " from " + Callers());
        }

        // The mod's own methods that led here, nearest first, skipping this class and Syncable.
        static string Callers() {
            try {
                StackTrace trace = new StackTrace(2, false);
                StringBuilder sb = new StringBuilder();
                int taken = 0;
                for (int i = 0; i < trace.FrameCount && taken < 4; i++) {
                    var method = trace.GetFrame(i).GetMethod();
                    if (method == null || method.DeclaringType == null)
                        continue;
                    string type = method.DeclaringType.Name;
                    if (type == "Syncable" || type == "NpcTrace")
                        continue;
                    if (sb.Length > 0)
                        sb.Append(" < ");
                    sb.Append(type).Append('.').Append(method.Name);
                    taken++;
                }
                return sb.ToString();
            }
            catch (Exception) { return "?"; }
        }
    }
}
