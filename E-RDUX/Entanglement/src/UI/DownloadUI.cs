using System;
using System.IO;

using UnityEngine;
using UnityEngine.UI;

using TMPro;

using Entanglement.Data;
using Entanglement.Sync;

namespace Entanglement.UI
{
    // Radial "pie" download progress stuck to the right hand, shown only while files are coming in.
    // Built lazily and wrapped in try/catch so a UI failure can't take the game down. The
    // localPosition/localScale below are guesses - nudge them once you can see it in-headset.
    public static class DownloadUI
    {
        static GameObject root;
        static Image pie;
        static TextMeshProUGUI label;
        static bool buildFailed;

        public static void Update()
        {
            if (!FileTransferManager.HasActiveDownloads) {
                if (root != null && root.activeSelf)
                    root.SetActive(false);
                return;
            }

            if (!EnsureBuilt())
                return;

            if (!root.activeSelf)
                root.SetActive(true);

            float progress = FileTransferManager.TotalDownloadProgress();
            pie.fillAmount = progress;

            FileTransfer current = FileTransferManager.LargestActiveDownload();
            if (current != null) {
                string name = Path.GetFileNameWithoutExtension(current.fileName);
                float mb = current.totalBytes / 1024f / 1024f;
                float doneMb = current.receivedBytes / 1024f / 1024f;
                label.text = $"Downloading\n{name}\n{doneMb:F1} / {mb:F1} MB ({(int)(progress * 100f)}%)";
            }
        }

        static bool EnsureBuilt()
        {
            if (root != null)
                return true;
            if (buildFailed)
                return false;
            if (PlayerScripts.playerRightHand == null)
                return false; // rig not ready yet

            try {
                root = new GameObject("EntanglementDownloadUI");
                root.transform.SetParent(PlayerScripts.playerRightHand.transform, false);
                root.transform.localPosition = new Vector3(0f, 0.12f, 0f); // above the wrist
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one * 0.001f;

                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                root.AddComponent<CanvasScaler>();

                RectTransform rootRect = root.GetComponent<RectTransform>();
                rootRect.sizeDelta = new Vector2(220f, 260f);

                // Radial pie
                GameObject pieObj = new GameObject("Pie");
                pieObj.transform.SetParent(root.transform, false);
                pie = pieObj.AddComponent<Image>();
                pie.sprite = MakeCircleSprite(128);
                pie.type = Image.Type.Filled;
                pie.fillMethod = Image.FillMethod.Radial360;
                pie.fillOrigin = (int)Image.Origin360.Top;
                pie.fillClockwise = true;
                pie.color = new Color(0.3f, 0.8f, 1f, 0.95f);
                RectTransform pieRect = pieObj.GetComponent<RectTransform>();
                pieRect.sizeDelta = new Vector2(130f, 130f);
                pieRect.anchoredPosition = new Vector2(0f, 45f);

                // File name / size / percent
                GameObject labelObj = new GameObject("Label");
                labelObj.transform.SetParent(root.transform, false);
                label = labelObj.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = true;
                label.color = Color.white;
                label.text = "Downloading...";
                RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                labelRect.sizeDelta = new Vector2(210f, 90f);
                labelRect.anchoredPosition = new Vector2(0f, -80f);

                return true;
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[DownloadUI] Failed to build the hand UI, disabling it: {e.Message}");
                buildFailed = true;
                if (root != null)
                    UnityEngine.Object.Destroy(root);
                root = null;
                return false;
            }
        }

        // Filled white circle for the radial fill to reveal like a pie slice
        static Sprite MakeCircleSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size / 2f;
            Color opaque = Color.white;
            Color clear = new Color(1f, 1f, 1f, 0f);

            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float dx = x - r, dy = y - r;
                    tex.SetPixel(x, y, dx * dx + dy * dy <= r * r ? opaque : clear);
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
