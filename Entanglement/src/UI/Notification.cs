using System;

namespace Entanglement.UI
{
    /// <summary>
    /// Fusion Notification for Boneworks — title/body, tag, popup + radial inbox.
    /// Display uses Boneworks PopupText / UI_Subtitle_Control (BONELAB headTitles equivalent).
    /// </summary>
    public sealed class Notification
    {
        public string Title { get; set; } = "Entanglement";
        public string Message { get; set; }
        public string Tag { get; set; }

        public bool ShowPopup { get; set; } = true;
        public float PopupLength { get; set; } = 2f;
        public GameNotify.Kind Type { get; set; } = GameNotify.Kind.Info;

        /// <summary>Save into the radial Notifications inbox (Fusion SaveToMenu).</summary>
        public bool SaveToMenu { get; set; } = true;

        public Action OnAccepted { get; set; }
        public Action OnDeclined { get; set; }

        public bool HasAction => OnAccepted != null || OnDeclined != null;

        public string DisplayLine {
            get {
                string title = Title ?? "";
                string body = Message ?? "";
                if (string.IsNullOrEmpty(title))
                    return body ?? "";
                if (string.IsNullOrEmpty(body))
                    return title;
                return title + " — " + body;
            }
        }
    }
}
