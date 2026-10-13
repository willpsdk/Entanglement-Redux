using System;
using System.Collections;
using System.Collections.Generic;

using UnhollowerBaseLib;

using UnityEngine;

using MelonLoader;

namespace Entanglement.Representation {
    public sealed class RepFade {
        static readonly string[] ColorProperties = { "_Color", "_BaseColor", "_TintColor", "_MainColor" };

        // Drawn before every fade (the transparent queue starts at 3000).
        const int DepthQueue = 2999;
        const int FadeQueue = 3000;

        class Part {
            public Renderer renderer;
            public Material[] original;
            public Material[] faded;
            public string[] colorProperty;
            public Color[] color;
            public Material depth;
        }

        readonly List<Part> parts = new List<Part>();
        readonly bool fadingIn;
        readonly float seconds;
        readonly float from;
        readonly float started;
        bool done;

        public float Alpha { get; private set; }

        public bool FadingIn => fadingIn;

        RepFade(bool fadingIn, float seconds, float from) {
            this.fadingIn = fadingIn;
            this.seconds = Mathf.Max(0.05f, seconds);
            this.from = Mathf.Clamp01(from);
            started = Time.realtimeSinceStartup;
            Alpha = this.from;
        }

        public static RepFade Start(List<Renderer> renderers, bool fadeIn, float seconds, float from) {
            RepFade fade = new RepFade(fadeIn, seconds, from);
            if (renderers != null) {
                for (int i = 0; i < renderers.Count; i++)
                    fade.Add(renderers[i]);
            }
            fade.Apply(fade.from);
            return fade;
        }

        void Add(Renderer renderer) {
            try {
                if (!renderer)
                    return;
                Il2CppReferenceArray<Material> shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0)
                    return;

                bool single = shared.Length == 1;

                Part part = new Part {
                    renderer = renderer,
                    original = new Material[shared.Length],
                    faded = new Material[shared.Length],
                    colorProperty = new string[shared.Length],
                    color = new Color[shared.Length],
                };

                for (int m = 0; m < shared.Length; m++) {
                    Material source = shared[m];
                    part.original[m] = source;
                    part.faded[m] = source;
                    if (!source)
                        continue;

                    string property;
                    Material copy = FadeableCopy(source, out property);
                    if (!copy)
                        continue;

                    SetDepthWrite(copy, !single);
                    copy.renderQueue = FadeQueue;
                    part.faded[m] = copy;
                    part.colorProperty[m] = property;
                    part.color[m] = copy.GetColor(property);

                    if (single) {
                        Material depth = new Material(copy);
                        SetDepthWrite(depth, true);
                        depth.renderQueue = DepthQueue;
                        Color none = part.color[m];
                        none.a = 0f;
                        depth.SetColor(property, none);
                        part.depth = depth;
                    }
                }

                Material[] drawn = part.faded;
                if (part.depth) {
                    drawn = new Material[] { part.faded[0], part.depth };
                }
                renderer.sharedMaterials = new Il2CppReferenceArray<Material>(drawn);
                parts.Add(part);
            }
            catch (Exception e) {
                EntangleLogger.Debug("[Rep] A part could not be faded: " + e.Message);
            }
        }

        static Material FadeableCopy(Material source, out string property) {
            property = ColorProperty(source);
            Material copy = null;
            try {
                if (property != null) {
                    copy = new Material(source);
                    if (SeeThrough(copy))
                        return copy;
                    UnityEngine.Object.Destroy(copy);
                    copy = null;
                }

                Shader fallback = FallbackShader();
                if (!fallback || !source.HasProperty("_MainTex"))
                    return null;
                copy = new Material(source);
                copy.shader = fallback;
                property = ColorProperty(copy);
                if (property != null && SeeThrough(copy))
                    return copy;
            }
            catch { }
            if (copy)
                UnityEngine.Object.Destroy(copy);
            property = null;
            return null;
        }

        static Shader fallbackShader;
        static bool fallbackLooked;

        static Shader FallbackShader() {
            if (fallbackLooked)
                return fallbackShader;
            fallbackLooked = true;
            string[] names = { "Valve/vr_standard", "Standard" };
            for (int i = 0; i < names.Length; i++) {
                try {
                    Shader shader = Shader.Find(names[i]);
                    if (shader) {
                        fallbackShader = shader;
                        EntangleLogger.Debug("[Rep] Parts that can't fade themselves fade with " + names[i]);
                        break;
                    }
                }
                catch { }
            }
            return fallbackShader;
        }

        static void SetDepthWrite(Material material, bool on) {
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", on ? 1 : 0);
        }

        static string ColorProperty(Material material) {
            for (int i = 0; i < ColorProperties.Length; i++) {
                if (material.HasProperty(ColorProperties[i]))
                    return ColorProperties[i];
            }
            return null;
        }

        static bool SeeThrough(Material material) {
            if (!material.HasProperty("_SrcBlend") || !material.HasProperty("_DstBlend"))
                return false;

            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 2f);
            material.SetInt("_SrcBlend", 5);    // BlendMode.SrcAlpha
            material.SetInt("_DstBlend", 10);   // BlendMode.OneMinusSrcAlpha
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = FadeQueue;
            return true;
        }

        void Apply(float alpha) {
            Alpha = alpha;
            for (int p = 0; p < parts.Count; p++) {
                Part part = parts[p];
                for (int m = 0; m < part.faded.Length; m++) {
                    string property = part.colorProperty[m];
                    if (property == null)
                        continue;
                    try {
                        Material copy = part.faded[m];
                        if (!copy)
                            continue;
                        Color c = part.color[m];
                        c.a *= alpha;
                        copy.SetColor(property, c);
                    }
                    catch { }
                }
            }
        }

        /// <summary>One frame of it. False once it is over and the real materials are back.</summary>
        public bool Step() {
            if (done)
                return false;

            float t = Mathf.Clamp01((Time.realtimeSinceStartup - started) / seconds);
            // Eased, so it neither starts nor ends with a jolt.
            float eased = t * t * (3f - 2f * t);
            float alpha = fadingIn
                ? Mathf.Lerp(from, 1f, eased)
                : Mathf.Lerp(from, 0f, eased);
            Apply(alpha);

            if (t < 1f)
                return true;

            // Out: off before the real, solid materials go back, so it never flashes back in.
            if (!fadingIn) {
                for (int p = 0; p < parts.Count; p++) {
                    try {
                        if (parts[p].renderer)
                            parts[p].renderer.enabled = false;
                    }
                    catch { }
                }
            }
            Stop();
            return false;
        }

        /// <summary>The real materials back now, whatever the fade had got to.</summary>
        public void Stop() {
            if (done)
                return;
            done = true;

            for (int p = 0; p < parts.Count; p++) {
                Part part = parts[p];
                try {
                    if (part.renderer)
                        part.renderer.sharedMaterials = new Il2CppReferenceArray<Material>(part.original);
                }
                catch { }
                for (int m = 0; m < part.faded.Length; m++) {
                    try {
                        if (part.faded[m] && !ReferenceEquals(part.faded[m], part.original[m]) && part.colorProperty[m] != null)
                            UnityEngine.Object.Destroy(part.faded[m]);
                    }
                    catch { }
                }
                try {
                    if (part.depth)
                        UnityEngine.Object.Destroy(part.depth);
                }
                catch { }
            }
            parts.Clear();
        }

        public static void FadeAway(float seconds, params GameObject[] roots) {
            List<Renderer> renderers = new List<Renderer>();
            for (int r = 0; r < roots.Length; r++) {
                GameObject root = roots[r];
                if (!root)
                    continue;
                try {
                    foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) {
                        if (collider)
                            collider.enabled = false;
                    }
                    foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true)) {
                        if (body)
                            body.detectCollisions = false;
                    }
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false)) {
                        if (renderer && renderer.enabled)
                            renderers.Add(renderer);
                    }
                }
                catch { }
            }

            MelonCoroutines.Start(CoFadeAway(Start(renderers, false, seconds, 1f), roots));
        }

        static IEnumerator CoFadeAway(RepFade fade, GameObject[] roots) {
            while (true) {
                bool running;
                try { running = fade.Step(); }
                catch { running = false; }
                if (!running)
                    break;
                yield return null;
            }

            for (int r = 0; r < roots.Length; r++) {
                try {
                    if (roots[r])
                        UnityEngine.Object.Destroy(roots[r]);
                }
                catch { }
            }
        }
    }
}
