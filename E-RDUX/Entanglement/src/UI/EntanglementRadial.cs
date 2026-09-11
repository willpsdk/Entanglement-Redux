using System;
using System.Reflection;

using UnityEngine;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Sync;

namespace Entanglement.UI
{
    // Wrist circle → full Entanglement hub pages (Fusion opens one button; we expose the main tabs)
    public static class EntanglementRadial
    {
        static bool registered;
        static MethodInfo addRadialButton;

        public static void Initialize() {
            if (registered) return;
            registered = true;

            try {
                addRadialButton = typeof(MenuManager).Assembly
                    .GetType("ModThatIsNotMod.BoneMenu.MenuManager")
                    ?.GetMethod("AddRadialMenuButton", BindingFlags.Public | BindingFlags.Static);

                if (addRadialButton == null) {
                    foreach (Type type in typeof(MenuManager).Assembly.GetTypes()) {
                        MethodInfo method = type.GetMethod("AddRadialMenuButton", BindingFlags.Public | BindingFlags.Static);
                        if (method != null) {
                            addRadialButton = method;
                            break;
                        }
                    }
                }

                if (addRadialButton == null) {
                    EntangleLogger.Warn("[EntanglementRadial] AddRadialMenuButton not found - circle menu entry disabled.");
                    return;
                }

                addRadialButton.Invoke(null, new object[] { "Entanglement", (Action)(() => EntanglementMenu.OpenRoot()) });
                addRadialButton.Invoke(null, new object[] { "Lobby", (Action)(() => EntanglementMenu.OpenLobby()) });
                addRadialButton.Invoke(null, new object[] { "Matchmaking", (Action)(() => EntanglementMenu.OpenMatchmaking()) });
                addRadialButton.Invoke(null, new object[] { "Players", (Action)(() => EntanglementMenu.OpenPlayers()) });
                addRadialButton.Invoke(null, new object[] { "Downloads", (Action)OpenDownloads });
                addRadialButton.Invoke(null, new object[] { "Settings", (Action)(() => EntanglementMenu.OpenSettings()) });

                EntangleLogger.Log("[EntanglementRadial] Registered full circle-menu hub buttons");
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[EntanglementRadial] Failed to register circle-menu buttons: {e.Message}");
            }
        }

        static void OpenDownloads() {
            try {
                if (!FileTransferManager.HasPendingConsent && !FileTransferManager.HasActiveDownloads)
                    Notifications.SendNotification("No pending downloads.\nWhen a file needs permission, open Downloads here.", 4f);
                EntanglementMenu.OpenDownloads();
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[EntanglementRadial] OpenDownloads failed: {e.Message}");
                Notifications.SendNotification("Open Entanglement → Downloads", 3f);
            }
        }
    }
}
