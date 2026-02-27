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
            MenuCategory category = MenuManager.CreateCategory("Entanglement: Redux", Color.white);

            // Added directly to the main category to bypass ModThatIsNotMod subcategory version conflicts!
            category.CreateBoolElement("Verbose Network Logging", Color.yellow, EntangleLogger.isVerbose, (val) => {
                EntangleLogger.isVerbose = val;
                if (val) EntangleLogger.Log("Verbose logging ENABLED. The console will now print detailed connection steps.", ConsoleColor.Green);
                else EntangleLogger.Log("Verbose logging DISABLED.", ConsoleColor.Red);
            });

            ServerUI.CreateUI(category);
            ClientUI.CreateUI(category);
            BanlistUI.CreateUI(category);
            LobbiesUI.CreateUI(category);
            VoiceUI.CreateUI(category);
            StatsUI.CreateUI(category);

#if DEBUG
            DebugUI.CreateUI(category);
#endif
        }
    }
}