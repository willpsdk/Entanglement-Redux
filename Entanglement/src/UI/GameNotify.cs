using System;
using System.Collections.Generic;

using ModThatIsNotMod;

using StressLevelZero.UI;

using UnityEngine;

namespace Entanglement.UI
{
    /// <summary>
    /// Fusion-inspired notify queue for BONEWORKS: typed toasts, cancel-by-tag, Prefer native
    /// PopupText / subtitles over BoneMenu Notifications. Radial remains the settings shell.
    /// </summary>
    public static class GameNotify
    {
        public enum Kind : byte {
            Info = 0,
            Warning = 1,
            Error = 2,
            Success = 3,
        }

        static UI_Subtitle_Control subtitles;
        static PopupText popup;
        static int framesUntilReady;
        static bool ready;
        static readonly List<Pending> queued = new List<Pending>();
        static readonly List<Pending> live = new List<Pending>();

        struct Pending {
            public string text;
            public float seconds;
            public Kind kind;
            public string tag;
            public float showUntil;
        }

        public static void OnSceneWasInitialized() {
            subtitles = null;
            popup = null;
            ready = false;
            framesUntilReady = 90;
            live.Clear();
        }

        public static void Tick() {
            float now = Time.unscaledTime;
            for (int i = live.Count - 1; i >= 0; i--) {
                if (now >= live[i].showUntil)
                    live.RemoveAt(i);
            }

            if (ready || framesUntilReady <= 0)
                return;

            framesUntilReady--;
            if (framesUntilReady > 0)
                return;

            ready = true;
            for (int i = 0; i < queued.Count; i++)
                Deliver(queued[i]);
            queued.Clear();
        }

        public static void Show(string text, float seconds = 4f) {
            Show(text, seconds, Kind.Info, null);
        }

        public static void Show(string text, float seconds, Kind kind, string tag = null) {
            if (string.IsNullOrEmpty(text))
                return;

            var pending = new Pending {
                text = text,
                seconds = seconds,
                kind = kind,
                tag = tag,
            };

            if (!string.IsNullOrEmpty(tag))
                CancelTag(tag);

            if (!ready) {
                queued.Add(pending);
                return;
            }

            Deliver(pending);
        }

        public static void CancelTag(string tag) {
            if (string.IsNullOrEmpty(tag))
                return;
            for (int i = queued.Count - 1; i >= 0; i--) {
                if (queued[i].tag == tag)
                    queued.RemoveAt(i);
            }
            for (int i = live.Count - 1; i >= 0; i--) {
                if (live[i].tag == tag)
                    live.RemoveAt(i);
            }
        }

        static void Deliver(Pending pending) {
            string line = pending.text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (line.Length > 90)
                line = line.Substring(0, 87) + "...";

            string prefix = Prefix(pending.kind);
            string shown = string.IsNullOrEmpty(prefix) ? line : prefix + line;

            pending.text = shown;
            pending.showUntil = Time.unscaledTime + Mathf.Max(2f, pending.seconds);
            live.Add(pending);

            // 1) Native PopupText (closest to Fusion headTitles on BONELAB)
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
                    subtitles.AddSubtitles(shown, Mathf.Max(2f, pending.seconds), AudioSync.SubtitlePlacement.Top);
                    return;
                }
            }
            catch (Exception e) {
                subtitles = null;
                EntangleLogger.Warn($"[GameNotify] Subtitle banner unavailable: {e.Message}");
            }

            // 3) Last resort: ModThatIsNotMod (BoneMenu-adjacent) notifications
            try {
                Notifications.SendNotification(shown, pending.seconds);
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
