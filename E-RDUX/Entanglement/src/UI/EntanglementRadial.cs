using System;
using System.Reflection;

using UnityEngine;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Sync;

namespace Entanglement.UI
{
    // Hooks the BONEWORKS circle / radial menu (via ModThatIsNotMod) so Entanglement isn't
    // buried only in BoneMenu. Deep prefs still live in BoneMenu for now; the radial entry is
    // the Fusion-style "open multiplayer stuff from the wrist circle" path, plus Accept/Deny
    // for pending file downloads without digging through nested BoneMenu pages.
    public static class EntanglementRadial
    {
        static bool registered;
        static MethodInfo addRadialButton;

        public static void Initialize() {
            if (registered) return;
            registered = true;

            try {
                // ModThatIsNotMod.AddRadialMenuButton(string name, Action onClick)
                addRadialButton = typeof(MenuManager).Assembly
                    .GetType("ModThatIsNotMod.BoneMenu.MenuManager")
                    ?.GetMethod("AddRadialMenuButton", BindingFlags.Public | BindingFlags.Static);

                if (addRadialButton == null) {
                    // Fallback: search the whole assembly for the helper
                    foreach (Type type in typeof(MenuManager).Assembly.GetTypes()) {
                        MethodInfo method = type.GetMethod("AddRadialMenuButton", BindingFlags.Public | BindingFlags.Static);
                        if (method != null) {
                            addRadialButton = method;
                            break;
                        }
                    }
                }

                if (addRadialButton == null) {
                    EntangleLogger.Warn("[EntanglementRadial] AddRadialMenuButton not found - circle menu entry disabled. BoneMenu still works.");
                    return;
                }

                addRadialButton.Invoke(null, new object[] { "Entanglement", (Action)OpenEntanglement });
                addRadialButton.Invoke(null, new object[] { "Accept Downloads", (Action)AcceptDownloads });
                addRadialButton.Invoke(null, new object[] { "Deny Downloads", (Action)DenyDownloads });

                EntangleLogger.Log("[EntanglementRadial] Registered circle-menu buttons");
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[EntanglementRadial] Failed to register circle-menu buttons: {e.Message}");
            }
        }

        static void OpenEntanglement() {
            // Opens the existing BoneMenu root until we ship a full custom circle panel
            try {
                EntanglementUI.OpenRootMenu();
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[EntanglementRadial] OpenEntanglement failed: {e.Message}");
                Notifications.SendNotification("Open BoneMenu → Entanglement Redux", 3f);
            }
        }

        static void AcceptDownloads() {
            if (!FileTransferManager.HasPendingConsent) {
                Notifications.SendNotification("No pending downloads.", 2f);
                return;
            }
            int count = FileTransferManager.PendingConsentCount;
            FileTransferManager.AcceptAllPending();
            Notifications.SendNotification($"Accepted {count} download(s).", 3f);
        }

        static void DenyDownloads() {
            if (!FileTransferManager.HasPendingConsent) {
                Notifications.SendNotification("No pending downloads.", 2f);
                return;
            }
            int count = FileTransferManager.PendingConsentCount;
            FileTransferManager.DenyAllPending();
            Notifications.SendNotification($"Denied {count} download(s).", 3f);
        }
    }
}
