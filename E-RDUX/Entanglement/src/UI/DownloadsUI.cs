using System.IO;

using UnityEngine;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Network;
using Entanglement.Sync;

namespace Entanglement.UI
{
    // Fusion-style consent inbox: each pending transfer is a submenu that names the file,
    // size, type, and sender, with Accept / Decline. Opened from the circle menu → Downloads.
    public static class DownloadsUI
    {
        public static MenuCategory downloadsCategory;

        const string refreshText = "Refresh List";

        public static void CreateUI(MenuCategory category) {
            downloadsCategory = category.CreateSubCategory("Downloads", new Color(1f, 0.75f, 0.25f));
            downloadsCategory.CreateFunctionElement(refreshText, Color.white, Refresh);
            downloadsCategory.CreateFunctionElement("Accept All", Color.green, () => {
                FileTransferManager.AcceptAllPending();
                Notifications.SendNotification("Accepted all pending downloads.", 3f);
                Refresh();
            });
            downloadsCategory.CreateFunctionElement("Decline All", Color.red, () => {
                FileTransferManager.DenyAllPending();
                Notifications.SendNotification("Declined all pending downloads.", 3f);
                Refresh();
            });
            Refresh();
        }

        public static void Open() {
            Refresh();
            if (downloadsCategory != null)
                MenuManager.OpenCategory(downloadsCategory);
        }

        public static void Refresh() {
            if (downloadsCategory == null)
                return;

            // Wipe dynamic entries, keep the first three controls (Refresh / Accept All / Decline All)
            while (downloadsCategory.elements.Count > 3)
                downloadsCategory.elements.RemoveAt(downloadsCategory.elements.Count - 1);

            var pending = FileTransferManager.GetPendingConsentTransfers();
            var active = FileTransferManager.LargestActiveDownload();

            if (pending.Count == 0 && !FileTransferManager.HasActiveDownloads) {
                downloadsCategory.CreateFunctionElement("No pending downloads", Color.grey, () => { });
            }
            else {
                foreach (FileTransfer transfer in pending) {
                    string name = Path.GetFileName(transfer.fileName);
                    float mb = transfer.totalBytes / 1024f / 1024f;
                    string sender = SteamIntegration.GetUserName(transfer.peer);
                    string type = CategoryLabel(transfer.category);

                    MenuCategory item = downloadsCategory.CreateSubCategory(
                        $"{name}  ({mb:F1} MB)",
                        new Color(1f, 0.85f, 0.4f));

                    item.CreateFunctionElement($"File: {name}", Color.white, () => { });
                    item.CreateFunctionElement($"Size: {mb:F1} MB", Color.white, () => { });
                    item.CreateFunctionElement($"Type: {type}", Color.white, () => { });
                    item.CreateFunctionElement($"From: {sender}", Color.white, () => { });

                    ushort id = transfer.id;
                    item.CreateFunctionElement("Accept Download", Color.green, () => {
                        FileTransferManager.AcceptPending(id);
                        Notifications.SendNotification($"Accepted {name}", 3f);
                        Refresh();
                    });
                    item.CreateFunctionElement("Decline Download", Color.red, () => {
                        FileTransferManager.DenyPending(id);
                        Notifications.SendNotification($"Declined {name}", 3f);
                        Refresh();
                    });
                }

                if (FileTransferManager.HasActiveDownloads && active != null) {
                    string name = Path.GetFileName(active.fileName);
                    float progress = active.totalBytes > 0
                        ? (100f * active.receivedBytes / active.totalBytes)
                        : 0f;
                    downloadsCategory.CreateFunctionElement(
                        $"Downloading: {name} ({(int)progress}%)",
                        new Color(0.4f, 0.85f, 1f),
                        () => { });
                }
            }

        }

        static string CategoryLabel(FileTransferCategory category) {
            switch (category) {
                case FileTransferCategory.CustomItem: return "Custom Item";
                case FileTransferCategory.Playermodel: return "Playermodel";
                case FileTransferCategory.CustomMap: return "Custom Map";
                default: return category.ToString();
            }
        }
    }
}
