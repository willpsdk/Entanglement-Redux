using System;
using System.Collections.Generic;

namespace Entanglement.Patching
{
    public static class PatchGuard
    {
        static readonly Dictionary<string, int> failures = new Dictionary<string, int>();

        public static void Report(string patch, Exception e) {
            int count;
            failures.TryGetValue(patch, out count);
            count++;
            failures[patch] = count;

            if (count <= 3 || count % 500 == 0)
                EntangleLogger.Warn("[Patch] " + patch + " failed (" + count + "x): " + e.Message);
        }

        /// <summary>Counts so far, for diagnostics.</summary>
        public static int FailureCount(string patch) {
            int count;
            failures.TryGetValue(patch, out count);
            return count;
        }
    }
}
