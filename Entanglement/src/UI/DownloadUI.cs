using System;
using System.IO;

using StressLevelZero.Interaction;

using TMPro;

using UnityEngine;
using UnityEngine.Rendering;

using Entanglement.Data;
using Entanglement.Sync;

namespace Entanglement.UI
{
    public static class DownloadUI
    {
        const float Smooth = 0.35f;
        const float Tau = 6.2831855f;
        const float HalfPi = 1.5707964f;
        const int Wedges = 16;
        const float Inner = 0.016f;
        const float Outer = 0.078f;
        const float Gap = 0.045f;
        static readonly Vector3 Offset = new Vector3(0.02f, 0.07f, 0.04f);

        static GameObject root;
        static MeshRenderer ringRenderer;
        static Mesh ringMesh;
        static Color32[] colors;
        static TextMeshProUGUI label;
        static bool buildFailed;
        static bool snapped;

        public static void OnSceneWasInitialized() {
            label = null;
            ringRenderer = null;
            ringMesh = null;
            colors = null;
            root = null;
            snapped = false;
            buildFailed = false;
        }

        public static void Update() {
            bool pending = FileTransferManager.HasPendingConsent || Thunderstore.HasPending;
            bool downloading = FileTransferManager.HasActiveDownloads || Thunderstore.IsBusy;
            if (!pending && !downloading) {
                if (root != null && root.activeSelf)
                    root.SetActive(false);
                snapped = false;
                return;
            }

            if (!EnsureBuilt())
                return;

            if (!root.activeSelf)
                root.SetActive(true);

            FollowWrist();

            float progress;
            Color color;
            string text;
            if (pending) {
                FileTransfer waiting = FileTransferManager.LargestPendingConsent();
                progress = waiting != null && waiting.totalBytes > 0
                    ? Mathf.Clamp01((float)waiting.receivedBytes / waiting.totalBytes)
                    : 0.15f;
                color = new Color(1f, 0.78f, 0.15f, 1f);
                if (waiting != null)
                    text = SizeLine(Path.GetFileNameWithoutExtension(waiting.fileName), waiting.receivedBytes / 1024f / 1024f, waiting.totalBytes / 1024f / 1024f);
                else {
                    var offers = Thunderstore.GetPending();
                    text = offers.Count > 0
                        ? "Download " + offers[0].label + "\nCircle, Downloads"
                        : "Permission needed\nCircle, Downloads";
                }
            }
            else if (Thunderstore.TryGetProgress(out string storeName, out float doneMb, out float totalMb, out float storeProgress)) {
                progress = storeProgress;
                color = new Color(0.35f, 0.9f, 1f, 1f);
                text = totalMb > 0f
                    ? SizeLine(storeName, doneMb, totalMb)
                    : SizeLine(storeName, 0f, 0f);
            }
            else {
                progress = FileTransferManager.TotalDownloadProgress();
                color = new Color(0.35f, 0.9f, 1f, 1f);
                FileTransfer current = FileTransferManager.LargestActiveDownload();
                if (current == null)
                    text = SizeLine("Downloading", progress, 1f);
                else
                    text = SizeLine(Path.GetFileNameWithoutExtension(current.fileName), current.receivedBytes / 1024f / 1024f, current.totalBytes / 1024f / 1024f);
            }

            Paint(progress, color);
            if (label != null)
                label.text = text;
        }

        static bool EnsureBuilt() {
            if (root != null)
                return true;
            if (buildFailed)
                return false;

            Hand hand = Wrist();
            if (hand == null || !PlayerScripts.playerRig)
                return false;

            try {
                root = new GameObject("EntanglementDownloadPie");
                BuildRing();
                BuildLabel();
                snapped = false;
                return true;
            }
            catch (Exception e) {
                EntangleLogger.Warn($"[DownloadUI] Failed to build the pie meter: {e.Message}");
                buildFailed = true;
                if (root != null)
                    UnityEngine.Object.Destroy(root);
                root = null;
                return false;
            }
        }

        static void BuildRing() {
            var verts = new Vector3[Wedges * 4];
            var tris = new int[Wedges * 6];
            colors = new Color32[Wedges * 4];

            for (int i = 0; i < Wedges; i++) {
                float a0 = (i / (float)Wedges) * Tau - HalfPi + Gap * 0.5f;
                float a1 = ((i + 1) / (float)Wedges) * Tau - HalfPi - Gap * 0.5f;
                int b = i * 4;
                verts[b] = Polar(a0, Inner);
                verts[b + 1] = Polar(a0, Outer);
                verts[b + 2] = Polar(a1, Outer);
                verts[b + 3] = Polar(a1, Inner);
                int t = i * 6;
                tris[t] = b;
                tris[t + 1] = b + 1;
                tris[t + 2] = b + 2;
                tris[t + 3] = b;
                tris[t + 4] = b + 2;
                tris[t + 5] = b + 3;
            }

            ringMesh = new Mesh();
            ringMesh.vertices = verts;
            ringMesh.triangles = tris;
            ringMesh.colors32 = colors;
            ringMesh.RecalculateBounds();

            var ring = new GameObject("Ring");
            ring.transform.SetParent(root.transform, false);
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = RingMaterial();
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
        }

        static string SizeLine(string name, float doneMb, float totalMb) {
            if (string.IsNullOrEmpty(name))
                name = "Download";
            if (name.Length > 18)
                name = name.Substring(0, 17) + "...";
            return $"{name}\n{doneMb:0.0}/{totalMb:0.0} MB";
        }

        static Vector3 Polar(float angle, float radius) {
            return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }

        static Material RingMaterial() {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("UI/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            var mat = new Material(shader);
            mat.mainTexture = WhiteTex();
            mat.color = Color.white;
            if (mat.HasProperty("_Cull"))
                mat.SetInt("_Cull", 0);
            mat.renderQueue = 4000;
            return mat;
        }

        static Texture2D whiteTex;

        static Texture2D WhiteTex() {
            if (whiteTex != null)
                return whiteTex;
            whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply();
            return whiteTex;
        }

        static void BuildLabel() {
            // A cloned gun-menu label has no world canvas, so the title never drew.
            var go = new GameObject("Label");
            go.transform.SetParent(root.transform, false);
            // Face the player, then un-mirror. A 180 yaw alone makes the title read backwards.
            go.transform.localPosition = new Vector3(0f, 0.012f, -0.02f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(-0.0008f, 0.0008f, 0.0008f);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 500;

            label = go.AddComponent<TextMeshProUGUI>();
            if (label.font == null && PlayerScripts.playerRig) {
                TextMeshProUGUI sample = PlayerScripts.playerRig.GetComponentInChildren<TextMeshProUGUI>(true);
                if (sample != null && sample.font != null)
                    label.font = sample.font;
            }
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.color = Color.white;
            label.fontSize = 48f;
            label.rectTransform.sizeDelta = new Vector2(520f, 180f);
            label.text = "test download\n0.0/10.0 MB";
        }

        static void FollowWrist() {
            Hand hand = Wrist();
            Transform head = Head();
            if (hand == null || head == null || hand.palmPositionTransform == null)
                return;

            Vector3 palm = hand.palmPositionTransform.position;
            Vector3 toHead = head.position - palm;
            toHead.y = 0f;
            if (toHead.sqrMagnitude < 0.0001f)
                toHead = -head.forward;
            toHead.Normalize();

            Vector3 target = palm + Vector3.up * Offset.y + hand.transform.right * Offset.x + toHead * Offset.z;
            // Point the back of the ring at the head so a standard quad faces the player.
            Quaternion face = Quaternion.LookRotation(target - head.position, Vector3.up);

            if (!snapped) {
                root.transform.SetPositionAndRotation(target, face);
                snapped = true;
                return;
            }

            root.transform.position = Vector3.Lerp(root.transform.position, target, Smooth);
            root.transform.rotation = Quaternion.Slerp(root.transform.rotation, face, Smooth);
        }

        static void Paint(float progress, Color color) {
            if (ringMesh == null || colors == null)
                return;

            float filled = Mathf.Clamp01(progress) * Wedges;
            Color32 dim = new Color32(28, 30, 36, 230);
            Color32 lit = (Color32)color;

            for (int i = 0; i < Wedges; i++) {
                float slice = Mathf.Clamp01(filled - i);
                Color32 tint = Color32.Lerp(dim, lit, slice);
                int b = i * 4;
                colors[b] = tint;
                colors[b + 1] = tint;
                colors[b + 2] = tint;
                colors[b + 3] = tint;
            }

            ringMesh.colors32 = colors;
        }

        static Hand Wrist() {
            Hand left = PlayerScripts.playerLeftHand;
            if (left != null && left.palmPositionTransform != null)
                return left;
            Hand right = PlayerScripts.playerRightHand;
            if (right != null && right.palmPositionTransform != null)
                return right;
            return null;
        }

        static Transform Head() {
            if (!PlayerScripts.playerRig)
                return null;
            Transform head = PlayerScripts.playerRig.transform.Find("[PhysicsRig]/Head");
            if (head != null)
                return head;
            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
