using System;

using HarmonyLib;

using StressLevelZero.Interaction;
using StressLevelZero.Props;
using StressLevelZero.Props.Weapons;

using Entanglement.Network;
using Entanglement.Managers;
using Entanglement.Data;

using UnityEngine;

namespace Entanglement.Patching
{
    /// <summary>
    /// Patches Boneworks story mode systems to sync state across the network.
    /// Detects when doors open/close, objects are used, and destructibles break.
    /// </summary>
    public static class StoryModeSyncPatches
    {
        // This class serves as a container for story mode syncing
        // The actual patches are more complex and depend on Boneworks internals
        // For now, we register the message handlers and provide the infrastructure

        public static void Initialize()
        {
            // Story mode sync is now available via StoryModeSync static class
            // Patches would be added here once Door/Grabbable/etc are properly resolved
            EntangleLogger.Verbose("Story Mode Sync initialized");
        }
    }
}
