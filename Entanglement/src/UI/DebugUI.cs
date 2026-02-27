#if DEBUG
using UnityEngine;

using ModThatIsNotMod.BoneMenu;

using Entanglement.Representation;
using Entanglement.Objects;
using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Managers;

namespace Entanglement.UI {
    public static class DebugUI {
        public static void CreateUI(MenuCategory category) {
            MenuCategory debugCategory = category.CreateSubCategory("--DEBUG--", Color.red);

            // Player Representation Debug
            debugCategory.CreateFunctionElement("Create Debug Representation", Color.white, () => { 
                EntangleLogger.Log("[DEBUG] Creating debug representation...", ConsoleColor.Magenta);
                PlayerRepresentation.debugRepresentation = new PlayerRepresentation("Dummy", 0);
                EntangleLogger.Log("[DEBUG] ✓ Debug representation created", ConsoleColor.Green);
            });

            // Network Status
            debugCategory.CreateFunctionElement("Print Network Status", Color.cyan, () => {
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Cyan);
                EntangleLogger.Log("[DEBUG] NETWORK STATUS:", ConsoleColor.Cyan);
                EntangleLogger.Log($"  Has Lobby: {SteamIntegration.hasLobby}", ConsoleColor.Yellow);
                EntangleLogger.Log($"  Is Host: {SteamIntegration.isHost}", ConsoleColor.Yellow);
                EntangleLogger.Log($"  Is Invalid: {SteamIntegration.isInvalid}", ConsoleColor.Yellow);
                if (Node.activeNode != null)
                {
                    EntangleLogger.Log($"  Active Node: {(Node.isServer ? "Server" : "Client")}", ConsoleColor.Yellow);
                    EntangleLogger.Log($"  Sent Bytes: {Node.activeNode.sentByteCount}", ConsoleColor.Yellow);
                    EntangleLogger.Log($"  Received Bytes: {Node.activeNode.recievedByteCount}", ConsoleColor.Yellow);
                }
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Cyan);
            });

            // Object Sync Status
            debugCategory.CreateFunctionElement("Print Object Sync Status", Color.green, () => {
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Green);
                EntangleLogger.Log("[DEBUG] OBJECT SYNC STATUS:", ConsoleColor.Green);
                EntangleLogger.Log($"  Synced Objects: {ObjectSync.syncedObjects.Count}", ConsoleColor.Yellow);
                EntangleLogger.Log($"  Queued Syncs: {ObjectSync.queuedSyncs.Count}", ConsoleColor.Yellow);
                EntangleLogger.Log($"  Last Object ID: {ObjectSync.lastId}", ConsoleColor.Yellow);
                foreach (var kvp in ObjectSync.syncedObjects)
                {
                    EntangleLogger.Log($"    - ID {kvp.Key}: {kvp.Value.gameObject.name}", ConsoleColor.Cyan);
                }
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Green);
            });

            // Player Status
            debugCategory.CreateFunctionElement("Print Player Status", Color.blue, () => {
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Blue);
                EntangleLogger.Log("[DEBUG] PLAYER STATUS:", ConsoleColor.Blue);
                EntangleLogger.Log($"  Connected Players: {PlayerRepresentation.representations.Count}", ConsoleColor.Yellow);
                foreach (var kvp in PlayerRepresentation.representations)
                {
                    EntangleLogger.Log($"    - {kvp.Value.playerName} (ID: {kvp.Key})", ConsoleColor.Cyan);
                    if (kvp.Value.repRoot != null)
                    {
                        EntangleLogger.Log($"      Position: {kvp.Value.repRoot.position}", ConsoleColor.Gray);
                    }
                }
                EntangleLogger.Log("═══════════════════════════════════════════════════════", ConsoleColor.Blue);
            });

            // Full Debug Report
            debugCategory.CreateFunctionElement("Print Full Debug Report", Color.magenta, () => {
                EntangleLogger.Log("", ConsoleColor.White);
                EntangleLogger.Log("╔══════════════════════════════════════════════════════════╗", ConsoleColor.Magenta);
                EntangleLogger.Log("║          FULL ENTANGLEMENT DEBUG REPORT                 ║", ConsoleColor.Magenta);
                EntangleLogger.Log("╚══════════════════════════════════════════════════════════╝", ConsoleColor.Magenta);

                // Network Info
                EntangleLogger.Log("\n[NETWORK INFO]", ConsoleColor.Cyan);
                EntangleLogger.Log($"  Has Lobby: {SteamIntegration.hasLobby}", ConsoleColor.Yellow);
                EntangleLogger.Log($"  Is Host: {SteamIntegration.isHost}", ConsoleColor.Yellow);
                EntangleLogger.Log($"  Lobby ID: {(SteamIntegration.hasLobby ? SteamIntegration.lobbyId.m_SteamID : "None")}", ConsoleColor.Yellow);

                // Node Info
                EntangleLogger.Log("\n[NODE INFO]", ConsoleColor.Green);
                if (Node.activeNode != null)
                {
                    EntangleLogger.Log($"  Active: {(Node.isServer ? "Server" : "Client")}", ConsoleColor.Yellow);
                    EntangleLogger.Log($"  Connected Users: {Node.activeNode.connectedUsers.Count}", ConsoleColor.Yellow);
                    EntangleLogger.Log($"  Sent: {Node.activeNode.sentByteCount} bytes", ConsoleColor.Yellow);
                    EntangleLogger.Log($"  Received: {Node.activeNode.recievedByteCount} bytes", ConsoleColor.Yellow);
                }
                else
                {
                    EntangleLogger.Log("  No active node", ConsoleColor.Red);
                }

                // Players
                EntangleLogger.Log($"\n[PLAYERS] ({PlayerRepresentation.representations.Count})", ConsoleColor.Blue);
                foreach (var kvp in PlayerRepresentation.representations)
                {
                    EntangleLogger.Log($"  {kvp.Value.playerName} (ID: {kvp.Key})", ConsoleColor.Yellow);
                }

                // Objects
                EntangleLogger.Log($"\n[SYNCED OBJECTS] ({ObjectSync.syncedObjects.Count})", ConsoleColor.Green);
                if (ObjectSync.syncedObjects.Count > 0)
                {
                    foreach (var kvp in ObjectSync.syncedObjects)
                    {
                        EntangleLogger.Log($"  ID {kvp.Key}: {kvp.Value.gameObject.name}", ConsoleColor.Yellow);
                    }
                }
                else
                {
                    EntangleLogger.Log("  None", ConsoleColor.Gray);
                }

                // Story Mode
                EntangleLogger.Log($"\n[STORY MODE]", ConsoleColor.Magenta);
                EntangleLogger.Log($"  Status: Initialized", ConsoleColor.Yellow);

                EntangleLogger.Log("\n╔══════════════════════════════════════════════════════════╗", ConsoleColor.Magenta);
                EntangleLogger.Log("║            END DEBUG REPORT                              ║", ConsoleColor.Magenta);
                EntangleLogger.Log("╚══════════════════════════════════════════════════════════╝", ConsoleColor.Magenta);
                EntangleLogger.Log("", ConsoleColor.White);
            });

            // Clear Cache
            debugCategory.CreateFunctionElement("Clear All Caches", Color.red, () => {
                EntangleLogger.Log("[DEBUG] Clearing all caches...", ConsoleColor.Yellow);
                ObjectSync.OnCleanup();
                ObjectSync.poolPairs.Clear();
                StoryModeSync.ClearAll();
                PooleeSyncable._Cache.Clear();
                PooleeSyncable._PooleeLookup.Clear();
                EntangleLogger.Log("[DEBUG] ✓ All caches cleared", ConsoleColor.Green);
            });
        }
    }
}
#endif
