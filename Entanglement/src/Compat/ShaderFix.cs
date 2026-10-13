using System.Collections.Generic;

using UnityEngine;

namespace Entanglement.Compat
{
    /// <summary>
    /// SDK → in-game shader rewrite for custom skins/items
    /// (vendored from ModThatIsNotMod.CustomItems.FixObjectShaders — no MTINM dependency).
    /// </summary>
    public static class ShaderFix
    {
        static readonly Dictionary<string, string> Replacements = new Dictionary<string, string> {
            {"Hidden/InternalErrorShader", "Valve/vr_standard"},
            {"Valve/vr_standard2", "Valve/vr_standard"},
            {"Valve/vr_standard", "Valve/vr_standard"},
            {"Standard", "Valve/vr_standard"},
            {"SDK/AdditiveHDR", "SLZ/Additive HDR"},
            {"SDK/CubeReflection", "SLZ/Cube Reflection"},
            {"SDK/GibSkin", "SLZ/GibSkin"},
            {"SDK/GibSkinCrablet", "SLZ/GibSkinCrablet"},
            {"SDK/GibSkinMAS", "SLZ/GibSkinMAS"},
            {"SDK/GibSkinMASEmissive", "SLZ/GibSkinMAS_Emissive"},
            {"SDK/GibSkinMASTransparent", "SLZ/GibSkinMAS Transparent"},
            {"SDK/GibSkinWire", "SLZ/GibSkinWire"},
            {"SDK/Glass", "SLZ/Glass"},
            {"SDK/GlowingFalloff", "SLZ/Glowing Falloff"},
            {"SDK/GridGlitch", "SLZ/Grid Glitch"},
            {"SDK/Highlighter", "SLZ/Highlighter"},
            {"SDK/HolographicPortal", "SLZ/Holographic Portal"},
            {"SDK/HolographicProjection", "SLZ/Holographic Projection"},
            {"SDK/HolographicVisor", "SLZ/Holographic Visor"},
            {"SDK/HolographicWall", "SLZ/Holographic Wall"},
            {"SDK/HolographicWallGlitchy", "SLZ/Holographic Wall Glitchy"},
            {"SDK/IconBillboard", "SLZ/Icon Billboard"},
            {"SDK/Laser", "SLZ/Laser"},
            {"SDK/LitHologram", "SLZ/Lit Hologram"},
            {"SDK/MultiplicativeDecal", "SLZ/Multiplicative Decal"},
            {"SDK/Multiply", "SLZ/Multiply"},
            {"SDK/Nothin", "SLZ/Nothin"},
            {"SDK/ParticleMotionVector", "SLZ/Particle Motion Vector"},
            {"SDK/ReflexScope", "Shader Forge/reflexScope"},
            {"SDK/ShadowOnly", "SLZ/ShadowOnly"},
            {"SDK/SimpleFluorescence", "SLZ/Simple Fluorescence"},
            {"SDK/SmokePoof", "SLZ/SmokePoof"},
            {"SDK/StochasticHologram", "SLZ/Stochastic Hologram"},
            {"SDK/StylizedFXShield", "Stylized FX/Shield"},
            {"SDK/SwirlClouds", "SLZ/SwirlClouds"},
            {"SDK/TextureArrays", "SLZ/Texture Arrays"},
            {"SDK/Void", "SLZ/Void"},
            {"SDK/VoidBleed", "SLZ/VoidBleed"},
            {"SDK/VRTerrain", "SLZ/VR Terrain"},
            {"SDK/WhiteDimension", "SLZ/White Dimension"},
            {"SDK/ZeroLabVRDissolve", "ZeroLab/VR_Dissolve"},
            {"SDK/ZoomLens", "SLZ/ZoomLens"},
        };

        public static void FixObjectShaders(GameObject obj) {
            if (!obj)
                return;
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++) {
                Renderer renderer = renderers[r];
                if (!renderer)
                    continue;
                try {
                    Material[] mats = renderer.sharedMaterials;
                    if (mats == null)
                        continue;
                    for (int m = 0; m < mats.Length; m++) {
                        Material mat = mats[m];
                        if (mat == null || mat.shader == null)
                            continue;
                        string name = mat.shader.name;
                        string replacement;
                        if (Replacements.TryGetValue(name, out replacement)) {
                            Shader found = Shader.Find(replacement);
                            if (found != null)
                                mat.shader = found;
                        }
                        else if (name.Contains("SDK")) {
                            Shader found = Shader.Find(name.Replace("SDK", "SLZ"));
                            if (found != null)
                                mat.shader = found;
                        }
                    }
                }
                catch { }
            }
        }
    }
}
