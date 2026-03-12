using System;
using UnityEngine;

using ModThatIsNotMod.BoneMenu;

using Entanglement.Network;
using Entanglement.Representation;

namespace Entanglement.UI
{
    public static class EntanglementUI
    {
        public static void CreateUI()
        {
            try
            {
                MenuCategory category = MenuManager.CreateCategory("Entanglement: Redux", Color.white);

                try { ServerUI.CreateUI(category); }
                catch (Exception ex) { EntangleLogger.Error($"Failed to create ServerUI: {ex.Message}"); }

                try { ClientUI.CreateUI(category); }
                catch (Exception ex) { EntangleLogger.Error($"Failed to create ClientUI: {ex.Message}"); }

                try { BanlistUI.CreateUI(category); }
                catch (Exception ex) { EntangleLogger.Error($"Failed to create BanlistUI: {ex.Message}"); }

                try { LobbiesUI.CreateUI(category); }
                catch (Exception ex) { EntangleLogger.Error($"Failed to create LobbiesUI: {ex.Message}"); }

                // Voice chat UI removed

                MenuCategory debugCategory = category.CreateSubCategory("Debuging", Color.white);

                try { StatsUI.CreateUI(debugCategory); }
                catch (Exception ex) { EntangleLogger.Error($"Failed to create StatsUI: {ex.Message}"); }

                try { DebugUI.CreateUI(debugCategory); }
                catch (Exception ex) { EntangleLogger.Error($"Failed to create DebugUI: {ex.Message}"); }

                debugCategory.CreateBoolElement("Verbose Network Logging", Color.yellow, EntangleLogger.isVerbose, (val) => {
                    EntangleLogger.isVerbose = val;
                    if (val) EntangleLogger.Log("Verbose logging ENABLED. The console will now print detailed connection steps.", ConsoleColor.Green);
                    else EntangleLogger.Log("Verbose logging DISABLED.", ConsoleColor.Red);
                });

                debugCategory.CreateBoolElement("Disable Melon Loggers", Color.yellow, EntangleLogger.disableMelonLoggers, (val) => {
                    EntangleLogger.disableMelonLoggers = val;
                });

                EntangleLogger.Log("Entanglement UI initialized successfully!", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Critical error creating UI: {ex.Message}");
            }
        }
    }
}