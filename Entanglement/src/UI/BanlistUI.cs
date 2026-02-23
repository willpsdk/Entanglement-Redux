using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Network;
using Entanglement.Data;

using ModThatIsNotMod.BoneMenu;

using UnityEngine;

using MelonLoader;

namespace Entanglement.UI
{
    public static class BanlistUI {
        public static MenuCategory banCategory;
        const string refreshText = "Refresh";

        public static void CreateUI(MenuCategory category) {
            banCategory = category.CreateSubCategory("Banned Users", Color.white);
            banCategory.CreateFunctionElement(refreshText, Color.white, Refresh);
        }

        public static void ClearPlayers() {
            List<string> elementsToRemove = new List<string>();
            foreach (MenuElement element in banCategory.elements) {
                if (element.displayText != refreshText) elementsToRemove.Add(element.displayText);
            }

            foreach (string element in elementsToRemove) banCategory.RemoveElement(element);
        }

        public static void Refresh() {
            ClearPlayers();

            foreach (var user in BanList.bannedUsers) {
                // FIXED: Passing ulong and string directly instead of a Discord User object
                AddUser(user.Item1, user.Item2);
            }

            UpdateMenu();
        }

        public static void UpdateMenu() => MenuManager.OpenCategory(banCategory);

        // FIXED: Accepting Steam ID and Username instead of Discord User
        public static void AddUser(ulong steamId, string username)
        {
            string playerName = $"{username}";

            MenuCategory userItem = banCategory.CreateSubCategory(playerName, Color.white);
            userItem.CreateFunctionElement("Unban", Color.red, () => {
                BanList.UnbanUser(steamId, username);
                Refresh();
            });
        }
    }
}