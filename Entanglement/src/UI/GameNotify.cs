using System;
using System.Collections.Generic;

using ModThatIsNotMod;

using StressLevelZero.UI;

using UnityEngine;

namespace Entanglement.UI
{
    /// <summary>
    /// Fusion Notifier for BONEWORKS: sequential popup queue, cancel-by-tag, radial inbox.
    /// Prefers native PopupText / subtitles (BONELAB TutorialRig.headTitles equivalent).
    /// </summary>
    public static class GameNotify
    {
        public enum Kind : byte {
            Info = 0,
            Warning = 1,
            Error = 2,
            Success = 3,
        }

        public const float DefaultDuration = 2f;

        static UI_Subtitle_Control subtitles;
        static PopupText popup;
        static int framesUntilReady;
        static bool ready;

        static readonly List<Notification> queued = new List<Notification>();
        static Notification current;
        static float currentUntil;
        static readonly List<Notification> inbox = new List<Notification>();
        const int InboxCap = 32;

        /// <summary>Fusion-style persistent notification inbox (radial → Notifications).</summary>
        public static IReadOnlyList<Notification> Inbox => inbox;

        public static Notification Current => current;

        public static void OnSceneWasInitialized() {
            subtitles = null;
            popup = null;
            ready = false;
            framesUntilReady = 90;
            // Don't wipe inbox across scenes — Fusion keeps menu notifications.
            current = null;
            currentUntil = 0f;
        }

        public static void ClearInbox() => inbox.Clear();

        public static void Tick() {
            float now = Time.unscaledTime;

            if (!ready) {
                if (framesUntilReady > 0)
                    framesUntilReady--;
                if (framesUntilReady <= 0)
                    ready = true;
            }

            if (current != null && now >= currentUntil) {
                current = null;
                try {
                    if (popup != null)
                        popup.Hide();
                }
                catch { popup = null; }
            }

            if (!ready || current != null || queued.Count == 0)
                return;

            DequeueNext();
        }

        public static void Send(Notification notification) {
            if (notification == null)
                return;
            if (string.IsNullOrEmpty(notification.Message) && string.IsNullOrEmpty(notification.Title))
                return;

            if (!string.IsNullOrEmpty(notification.Tag))
                Cancel(notification.Tag);

            if (notification.SaveToMenu)
                AddToInbox(notification);

            queued.Add(notification);
        }

        public static void Show(string text, float seconds = 4f) {
            Show(text, seconds, Kind.Info, null, true);
        }

        public static void Show(string text, float seconds, Kind kind, string tag = null, bool saveToInbox = true) {
            if (string.IsNullOrEmpty(text))
                return;
            Send(new Notification {
                Title = "Entanglement",
                Message = text,
                Type = kind,
                Tag = tag,
                PopupLength = seconds,
                ShowPopup = true,
                SaveToMenu = saveToInbox,
            });
        }

        public static void ShowTitle(string title, string message, float seconds = 4f, Kind kind = Kind.Info, string tag = null, bool saveToInbox = true) {
            Send(new Notification {
                Title = title ?? "Entanglement",
                Message = message,
                Type = kind,
                Tag = tag,
                PopupLength = seconds,
                ShowPopup = true,
                SaveToMenu = saveToInbox,
            });
        }

        public static void Cancel(string tag) {
            if (string.IsNullOrEmpty(tag))
                return;
            for (int i = queued.Count - 1; i >= 0; i--) {
                if (queued[i].Tag == tag)
                    queued.RemoveAt(i);
            }
            if (current != null && current.Tag == tag) {
                current = null;
                currentUntil = 0f;
                try { popup?.Hide(); }
                catch { popup = null; }
            }
        }

        public static void CancelAll() {
            queued.Clear();
            current = null;
            currentUntil = 0f;
            try { popup?.Hide(); }
            catch { popup = null; }
        }

        public static void CancelTag(string tag) => Cancel(tag);

        static void AddToInbox(Notification notification) {
            inbox.Insert(0, notification);
            while (inbox.Count > InboxCap)
                inbox.RemoveAt(inbox.Count - 1);
        }

        static void DequeueNext() {
            if (queued.Count == 0)
                return;

            current = queued[0];
            queued.RemoveAt(0);
            currentUntil = Time.unscaledTime + Mathf.Max(DefaultDuration, current.PopupLength);

            if (!current.ShowPopup)
                return;

            string shown = FormatPopup(current);
            DeliverPopup(shown, current.PopupLength);
        }

        static string FormatPopup(Notification n) {
            string line = n.DisplayLine.Replace("\r", " ").Replace("\n", " ").Trim();
            string prefix = Prefix(n.Type);
            // Avoid doubling if Title already carries the kind marker.
            if (!string.IsNullOrEmpty(prefix) && (string.IsNullOrEmpty(n.Title) || n.Title.Trim().Length == 0))
                line = prefix + line;
            else if (!string.IsNullOrEmpty(prefix) && n.Title != null && n.Title.StartsWith("["))
                { /* title already typed */ }
            else if (n.Type != Kind.Info && (string.IsNullOrEmpty(n.Title) || n.Title == "Entanglement"))
                line = prefix + (n.Message ?? line);

            if (line.Length > 90)
                line = line.Substring(0, 87) + "...";
            return line;
        }

        static void DeliverPopup(string shown, float seconds) {
            // 1) Native PopupText (closest to Fusion / BONELAB headTitles)
            try {
                if (popup == null)
                    popup = UnityEngine.Object.FindObjectOfType<PopupText>();
                if (popup != null) {
                    popup.Show(shown);
                    return;
                }
            }
            catch {
                popup = null;
            }

            // 2) Subtitle banner
            try {
                if (subtitles == null)
                    subtitles = UnityEngine.Object.FindObjectOfType<UI_Subtitle_Control>();
                if (subtitles != null) {
                    subtitles.AddSubtitles(shown, Mathf.Max(DefaultDuration, seconds), AudioSync.SubtitlePlacement.Top);
                    return;
                }
            }
            catch (Exception e) {
                subtitles = null;
                EntangleLogger.Warn("[GameNotify] Subtitle banner unavailable: " + e.Message);
            }

            // 3) Last resort: ModThatIsNotMod notifications
            try {
                Notifications.SendNotification(shown, seconds);
            }
            catch { }
        }

        static string Prefix(Kind kind) {
            switch (kind) {
                case Kind.Warning: return "[!] ";
                case Kind.Error: return "[X] ";
                case Kind.Success: return "[OK] ";
                default: return "";
            }
        }
    }
}
