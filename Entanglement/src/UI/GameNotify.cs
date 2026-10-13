using System;
using System.Collections.Generic;

using ModThatIsNotMod;

using UnityEngine;

namespace Entanglement.UI
{
    public static class GameNotify
    {
        static UI_Subtitle_Control subtitles;
        static int framesUntilReady;
        static bool ready;
        static readonly List<Pending> queued = new List<Pending>();

        struct Pending {
            public string text;
            public float seconds;
        }

        public static void OnSceneWasInitialized() {
            subtitles = null;
            ready = false;
            framesUntilReady = 90;
        }

        public static void Tick() {
            if (ready || framesUntilReady <= 0)
                return;

            framesUntilReady--;
            if (framesUntilReady > 0)
                return;

            ready = true;
            for (int i = 0; i < queued.Count; i++)
                Deliver(queued[i].text, queued[i].seconds);
            queued.Clear();
        }

        public static void Show(string text, float seconds = 4f) {
            if (string.IsNullOrEmpty(text))
                return;

            if (!ready) {
                queued.Add(new Pending { text = text, seconds = seconds });
                return;
            }

            Deliver(text, seconds);
        }

        static void Deliver(string text, float seconds) {
            string line = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (line.Length > 90)
                line = line.Substring(0, 87) + "...";

            try {
                if (subtitles == null)
                    subtitles = UnityEngine.Object.FindObjectOfType<UI_Subtitle_Control>();
                if (subtitles != null) {
                    subtitles.AddSubtitles(line, Mathf.Max(2f, seconds), AudioSync.SubtitlePlacement.Top);
                    return;
                }
            }
            catch (Exception e) {
                subtitles = null;
                EntangleLogger.Warn($"[GameNotify] Subtitle banner unavailable: {e.Message}");
            }

            try {
                Notifications.SendNotification(text, seconds);
            }
            catch { }
        }
    }
}
