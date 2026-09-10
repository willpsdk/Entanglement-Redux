using UnityEngine;

using ModThatIsNotMod.BoneMenu;

using Entanglement.Sync;

namespace Entanglement.UI {
    public static class SyncUI {
        public static void CreateUI(MenuCategory category) {
            MenuCategory syncCategory = category.CreateSubCategory("File Sync", Color.green);

            syncCategory.CreateBoolElement("Sync Custom Items", Color.white, SyncPrefs.itemSyncEnabled.Value, (value) => {
                SyncPrefs.itemSyncEnabled.Value = value;
            });

            syncCategory.CreateBoolElement("Sync Playermodels", Color.white, SyncPrefs.playermodelSyncEnabled.Value, (value) => {
                SyncPrefs.playermodelSyncEnabled.Value = value;
            });

            syncCategory.CreateBoolElement("Sync Custom Maps", Color.white, SyncPrefs.mapSyncEnabled.Value, (value) => {
                SyncPrefs.mapSyncEnabled.Value = value;
            });

            syncCategory.CreateBoolElement("Ask Before Download", Color.white, SyncPrefs.requireDownloadConsent.Value, (value) => {
                SyncPrefs.requireDownloadConsent.Value = value;
            });

            syncCategory.CreateIntElement("Max File Size (MB)", Color.white, SyncPrefs.maxSyncSizeKB.Value / 1024, (value) => {
                SyncPrefs.maxSyncSizeKB.Value = value * 1024;
            },
            10, 1, 500, true);

            syncCategory.CreateFunctionElement("Accept Pending Downloads", Color.green, () => {
                FileTransferManager.AcceptAllPending();
            });

            syncCategory.CreateFunctionElement("Deny Pending Downloads", Color.red, () => {
                FileTransferManager.DenyAllPending();
            });
        }
    }
}
