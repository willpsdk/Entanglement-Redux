using System;
using System.Collections.Generic;

namespace Entanglement.UI
{
    /// <summary>
    /// Fusion <c>MenuNotifications</c> for Boneworks radial — count, clear, accept/decline, replay.
    /// Backed by <see cref="GameNotify"/> inbox (Fusion SaveToMenu).
    /// </summary>
    public static class MenuNotifications
    {
        static Action _backToHub;
        static int page;

        public static void Show(Action backToHub) {
            _backToHub = backToHub ?? (() => { });
            page = 0;
            ShowPage();
        }

        public static void ShowPage() {
            var entries = new List<GameRadial.MenuEntry>();
            int count = GameNotify.Inbox.Count;

            // Fusion NotificationCountText
            string countLabel = count <= 0
                ? "You have no notifications."
                : (count == 1 ? "You have 1 notification." : "You have " + count + " notifications.");
            entries.Add(new GameRadial.MenuEntry(countLabel, null, true));

            entries.Add(new GameRadial.MenuEntry("Clear All", () => {
                GameNotify.ClearInbox();
                GameNotify.CancelAll();
                page = 0;
                ShowPage();
            }));

            const int pageSize = 4;
            int pages = count == 0 ? 1 : (count + pageSize - 1) / pageSize;
            if (page >= pages)
                page = 0;
            if (page < 0)
                page = 0;

            int start = page * pageSize;
            int shown = 0;
            for (int i = start; i < count && shown < pageSize; i++) {
                Notification item = GameNotify.Inbox[i];
                if (item == null)
                    continue;
                string label = !string.IsNullOrEmpty(item.Title) ? item.Title : item.Message;
                if (string.IsNullOrEmpty(label))
                    continue;
                if (label.Length > 26)
                    label = label.Substring(0, 23) + "...";

                string kind = KindMark(item.Type);
                if (item.HasAction) {
                    Notification captured = item;
                    entries.Add(new GameRadial.MenuEntry(kind + "OK: " + label, () => {
                        try { captured.OnAccepted?.Invoke(); }
                        catch { }
                        GameNotify.RemoveFromInbox(captured);
                        ShowPage();
                    }, true));
                    if (captured.OnDeclined != null) {
                        entries.Add(new GameRadial.MenuEntry(kind + "No: " + label, () => {
                            try { captured.OnDeclined?.Invoke(); }
                            catch { }
                            GameNotify.RemoveFromInbox(captured);
                            ShowPage();
                        }, true));
                    }
                }
                else {
                    Notification replay = item;
                    entries.Add(new GameRadial.MenuEntry(kind + label, () => {
                        GameNotify.Send(new Notification {
                            Title = replay.Title,
                            Message = replay.Message,
                            Type = replay.Type,
                            PopupLength = 4f,
                            ShowPopup = true,
                            SaveToMenu = false,
                        });
                    }, true));
                }
                shown++;
            }

            if (pages > 1) {
                entries.Add(new GameRadial.MenuEntry(
                    "Page " + (page + 1) + "/" + pages,
                    () => {
                        page = (page + 1) % pages;
                        ShowPage();
                    }));
            }

            GameRadial.OpenBackPage(_backToHub, entries, false);
        }

        static string KindMark(GameNotify.Kind kind) {
            switch (kind) {
                case GameNotify.Kind.Warning: return "[!] ";
                case GameNotify.Kind.Error: return "[X] ";
                case GameNotify.Kind.Success: return "[OK] ";
                default: return "";
            }
        }
    }
}
