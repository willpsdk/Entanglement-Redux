using System;

using HarmonyLib;

using ModThatIsNotMod;

using Entanglement.Data;

using StressLevelZero.UI;
using StressLevelZero.UI.Radial;

using UnityEngine;

namespace Entanglement.UI
{
    public static class EntanglementRadial
    {
        const string ButtonName = "Entanglement Redux";

        static bool added;
        static int framesUntilTry = int.MaxValue;

        // Don't take northwest. That's BoneMenu's slot.
        static readonly string[] spareRegions = {
            "button_Region_S",
            "button_Region_SW",
            "button_Region_W",
            "button_Region_NE",
            "button_Region_E",
            "button_Region_SE",
        };

        public static void OnSceneWasInitialized() {
            added = false;
            framesUntilTry = 0;
        }

        public static void EnsureVisible() {
            framesUntilTry = 0;
            TryAdd(true);
        }

        public static void Tick() {
            if (added)
                return;
            if (framesUntilTry > 0) {
                framesUntilTry--;
                return;
            }
            TryAdd();
        }

        static void TryAdd(bool refreshPage = false) {
            try {
                GameObject rig = null;
                if (PlayerScripts.playerRig)
                    rig = PlayerScripts.playerRig.gameObject;
                if (!rig)
                    rig = Player.GetRigManager();
                if (!rig)
                    return;

                Transform panel = rig.transform.Find("[UIRig]/PLAYERUI/panel_Default");
                if (!panel)
                    return;

                PageView pageView = panel.GetComponent<PageView>();
                if (pageView == null || pageView.m_HomePage == null || pageView.m_HomePage.items == null)
                    return;

                PopUpMenuView radialMenu = GameObject.FindObjectOfType<PopUpMenuView>();

                RemoveExtraEntanglement(pageView);
                if (HasEntanglement(pageView)) {
                    added = true;
                    if (refreshPage)
                        pageView.Render(pageView.m_HomePage);
                    return;
                }

                foreach (string region in spareRegions) {
                    PageItem.Directions direction = DirectionFromRegion(region);
                    if (DirectionTaken(pageView, direction))
                        continue;

                    Transform button = panel.Find(region);
                    if (!button)
                        continue;

                    PageItemView view = button.GetComponent<PageItemView>();
                    if (!view)
                        continue;

                    Action action = () => {
                        if (GameRadial.Open())
                            return;

                        EntangleLogger.Warn("[EntanglementRadial] Could not open the Entanglement Redux menu.");
                    };

                    view.m_Data = new PageItem(ButtonName, direction, action);
                    pageView.m_HomePage.items.Add(view.m_Data);
                    added = true;
                    if (refreshPage || !pageView.m_IsActivated)
                        pageView.Render(pageView.m_HomePage);
                    EntangleLogger.Log($"[EntanglementRadial] Added Entanglement on {region}");
                    return;
                }

                EntangleLogger.Warn("[EntanglementRadial] No free circle-menu slot.");
                added = true;
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[EntanglementRadial] Failed to add circle button: {e.Message}");
                added = true;
            }
        }

        static bool HasEntanglement(PageView pageView) {
            for (int i = 0; i < pageView.m_HomePage.items.Count; i++) {
                PageItem item = pageView.m_HomePage.items[i];
                if (item != null && item.name == ButtonName)
                    return true;
            }
            return false;
        }

        static void RemoveExtraEntanglement(PageView pageView) {
            bool kept = false;
            var items = pageView.m_HomePage.items;
            for (int i = items.Count - 1; i >= 0; i--) {
                PageItem item = items[i];
                if (item == null || item.name != ButtonName)
                    continue;
                if (!kept) {
                    kept = true;
                    continue;
                }
                items.RemoveAt(i);
            }
        }

        static bool DirectionTaken(PageView pageView, PageItem.Directions direction) {
            for (int i = 0; i < pageView.m_HomePage.items.Count; i++) {
                PageItem item = pageView.m_HomePage.items[i];
                if (item != null && item.direction == direction)
                    return true;
            }
            return false;
        }

        static PageItem.Directions DirectionFromRegion(string region) {
            if (region.EndsWith("_NE")) return PageItem.Directions.NORTHEAST;
            if (region.EndsWith("_NW")) return PageItem.Directions.NORTHWEST;
            if (region.EndsWith("_SE")) return PageItem.Directions.SOUTHEAST;
            if (region.EndsWith("_SW")) return PageItem.Directions.SOUTHWEST;
            if (region.EndsWith("_N")) return PageItem.Directions.NORTH;
            if (region.EndsWith("_S")) return PageItem.Directions.SOUTH;
            if (region.EndsWith("_E")) return PageItem.Directions.EAST;
            return PageItem.Directions.WEST;
        }
    }

    [HarmonyPatch(typeof(PopUpMenuView), nameof(PopUpMenuView.Activate))]
    public static class EntanglementRadialOpenPatch {
        public static void Prefix() => EntanglementRadial.EnsureVisible();

        public static void Postfix() {
            try { GameRadial.OnMenuOpened(); }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("EntanglementRadial.OpenPostfix", e); }
        }
    }
}
