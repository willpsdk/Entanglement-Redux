using Entanglement.Representation;
using Entanglement.Extensions;
using Entanglement.Network;
using Entanglement.Compat;

using UnityEngine;
using UnityEngine.Rendering;


using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using MelonLoader;

namespace Entanglement.Compat.Playermodels
{
    public static class PlayerSkinLoader
    {
        // Removes an existing PlayerModel and reverts back to ford.
        public static void ClearPlayermodel(PlayerRepresentation rep)
        {
            // A model still loading for this rep is no longer wanted.
            CancelLoad(rep);
            ClearNow(rep);
        }

        static void ClearNow(PlayerRepresentation rep)
        {
            // Make sure the rep has been created
            if (!rep.repFord)
                return;

            UnloadPlayermodel(rep);

            rep.repAnimationManager.animator = rep.repAnimator;
            rep.activeAnimator = rep.repAnimator;
            rep.repBody.ArtToBlender.bones = rep.repBody.ArtToBlender.bones.FillBones(rep.repAnimator.transform, rep.repAnimator);
            FetchFingerTransforms(rep);

            ReleaseBundle(rep);

            rep.currentSkinObject = null;
            rep.currentSkinPath = null;
            rep.currentSkinBundle = null;

            rep.repGeo.gameObject.SetActive(true);
            rep.repSHJnt.gameObject.SetActive(true);

            rep.repBody.OnStart();
            rep.isCustomSkinned = false;
            rep.OnModelChanged();
        }

        const string PrefabPath = "Assets/PlayerModels/PlayerModel.prefab";

        // Opening a model's file and its prefab on the main thread froze everyone's game for seconds
        // (5s hitches in the Oct 8 logs). Both now load in the background over several frames; the rep
        // keeps its current look until the new one is ready, and only the newest request is applied.
        public static void ApplyPlayermodel(PlayerRepresentation rep, string path)
        {
            if (rep == null || string.IsNullOrEmpty(path))
                return;
            // Already wearing it, or already on its way.
            if (rep.loadingSkinPath == null && rep.isCustomSkinned && rep.currentSkinPath == path && rep.currentSkinObject && rep.currentSkinBundle)
                return;
            if (rep.loadingSkinPath == path)
                return;

            rep.loadingSkinPath = path;
            int generation = ++rep.skinLoadGeneration;
            MelonCoroutines.Start(LoadThenApply(rep, path, generation));
        }

        static void CancelLoad(PlayerRepresentation rep)
        {
            if (rep.loadingSkinPath == null)
                return;
            rep.loadingSkinPath = null;
            rep.skinLoadGeneration++;
        }

        static bool StillWanted(PlayerRepresentation rep, int generation) => rep.skinLoadGeneration == generation && rep.repRoot;

        // Bundles this loader opened, by file name, so reps wearing the same model share one and it is
        // closed only once nobody wears it. One another mod opened (the local player's own model, the
        // same file) is borrowed and never closed here.
        static readonly Dictionary<string, AssetBundle> opened = new Dictionary<string, AssetBundle>();
        static readonly HashSet<string> opening = new HashSet<string>();

        static IEnumerator LoadThenApply(PlayerRepresentation rep, string path, int generation)
        {
            string fileName = Path.GetFileName(path);
            string key = fileName.ToLower();

            // Another rep is opening the same file: wait for it and share.
            while (opening.Contains(key))
                yield return null;

            AssetBundle bundle;
            if (!opened.TryGetValue(key, out bundle) || !bundle) {
                opened.Remove(key);
                bundle = null;
                try { bundle = AssetBundleUtilities.TryGetBundle(fileName); }
                catch { }
            }

            if (!bundle) {
                AssetBundleCreateRequest request = null;
                try { request = AssetBundle.LoadFromFileAsync(Path.Combine(PlayermodelsPatch.playerModelsPath, fileName)); }
                catch (Exception e) { EntangleLogger.Warn("[PlayerModel] Could not start loading " + fileName + ": " + e.Message); }

                if (request != null) {
                    opening.Add(key);
                    try {
                        while (!request.isDone)
                            yield return null;
                    }
                    finally { opening.Remove(key); }
                    try {
                        bundle = request.assetBundle;
                        if (bundle) {
                            bundle.name = key;
                            opened[key] = bundle;
                        }
                    }
                    catch { bundle = null; }
                }
            }

            if (!StillWanted(rep, generation)) {
                CloseIfUnused(bundle);
                yield break;
            }

            if (!bundle) {
                rep.loadingSkinPath = null;
                ClearNow(rep);
                rep.currentSkinPath = path;
                EntangleLogger.Warn($"PlayerModel failed to load for player {rep.playerName}.\nIf the PlayerModel was sent over discord, spaces in the name may have been replaced by underscores.");
                yield break;
            }

            // The prefab and its textures, in the background too where the game has the call for it.
            GameObject loadedModel = null;
            AsyncOperation assetRequest = StartAssetLoad(bundle, PrefabPath);
            if (assetRequest != null) {
                while (!assetRequest.isDone)
                    yield return null;
                loadedModel = AssetOf(assetRequest);
            }

            if (!StillWanted(rep, generation)) {
                CloseIfUnused(bundle);
                yield break;
            }
            rep.loadingSkinPath = null;

            try { Apply(rep, path, bundle, loadedModel); }
            catch (Exception e) { EntangleLogger.Warn("[PlayerModel] Could not put " + fileName + " on " + rep.playerName + ": " + e.Message); }
        }

        static MethodInfo loadAssetAsync;
        static bool lookedUpAsync;

        // AssetBundle.LoadAssetAsync(string, Type), if this game's build kept it; null means load it here.
        static AsyncOperation StartAssetLoad(AssetBundle bundle, string name)
        {
            try {
                if (!lookedUpAsync) {
                    lookedUpAsync = true;
                    loadAssetAsync = typeof(AssetBundle).GetMethod("LoadAssetAsync", new Type[] { typeof(string), typeof(Il2CppSystem.Type) });
                }
                if (loadAssetAsync == null)
                    return null;
                return loadAssetAsync.Invoke(bundle, new object[] { name, UnhollowerRuntimeLib.Il2CppType.Of<GameObject>() }) as AsyncOperation;
            }
            catch { return null; }
        }

        static GameObject AssetOf(AsyncOperation request)
        {
            try {
                PropertyInfo property = request.GetType().GetProperty("asset");
                UnityEngine.Object asset = property != null ? property.GetValue(request) as UnityEngine.Object : null;
                return asset ? asset.TryCast<GameObject>() : null;
            }
            catch { return null; }
        }

        static void Apply(PlayerRepresentation rep, string path, AssetBundle bundle, GameObject loadedModel)
        {
            // Putting the same file back on: its bundle stays open.
            if (rep.currentSkinBundle == bundle)
                rep.currentSkinBundle = null;
            // Clear Model
            ClearNow(rep);
            // Save skin
            rep.currentSkinPath = path;

            rep.currentSkinBundle = bundle;
            rep.isCustomSkinned = true;

            // Make sure the rep has been created
            if (!rep.repFord)
                return;

            if (loadedModel == null)
                loadedModel = rep.currentSkinBundle.LoadAsset<GameObject>(PrefabPath);
            if (loadedModel == null)
                return;

            rep.currentSkinObject = GameObject.Instantiate(loadedModel);
            ShaderFix.FixObjectShaders(rep.currentSkinObject);
            rep.currentSkinObject.name = rep.repFord.name;
            rep.skinAnimator = rep.currentSkinObject.GetComponent<Animator>();
            rep.skinAnimator.runtimeAnimatorController = rep.repAnimator.runtimeAnimatorController;
            rep.skinAnimator.enabled = false;

            rep.repAnimationManager.animator = rep.skinAnimator;
            rep.activeAnimator = rep.skinAnimator;
            rep.repBody.ArtToBlender.bones = rep.repBody.ArtToBlender.bones.FillBones(rep.skinAnimator.transform, rep.skinAnimator);
            FetchFingerTransforms(rep);

            rep.repBody.OnStart();

            rep.repGeo.gameObject.SetActive(false);
            rep.repSHJnt.gameObject.SetActive(false);

            // Get Renderers
            Renderer[] renderers = rep.currentSkinObject.GetComponentsInChildren<Renderer>();
            bool hasHead = false;
            // Find Head
            foreach (Renderer rend in renderers)
                if (rend.name.ToLower().Contains("head")) {
                    if (rend.material && !rend.material.shader.name.Contains("ShadowOnly") && rend.shadowCastingMode != ShadowCastingMode.ShadowsOnly) {
                        hasHead = true;
                        break;
                    }
                }
            // Replace ShadowCasts
            if (!hasHead) {
                foreach (Renderer rend in renderers) {
                    if (rend.material && rend.material.shader.name.Contains("ShadowOnly"))
                        rend.material = rep.repHologram;
                    else if (rend.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                        rend.shadowCastingMode = ShadowCastingMode.On;
                }
            }
            rep.OnModelChanged();
        }

        // Unloads currently loaded bundles
        public static void UnloadPlayermodel(PlayerRepresentation rep)
        {
            if (rep.currentSkinObject) {
                GameObject.Destroy(rep.currentSkinObject);
            }
            ReleaseBundle(rep);
        }

        static void ReleaseBundle(PlayerRepresentation rep)
        {
            AssetBundle bundle = rep.currentSkinBundle;
            rep.currentSkinBundle = null;
            CloseIfUnused(bundle);
        }

        // Closes a bundle this loader opened once no rep wears it.
        static void CloseIfUnused(AssetBundle bundle)
        {
            if (!bundle)
                return;
            try {
                string key = null;
                foreach (KeyValuePair<string, AssetBundle> pair in opened) {
                    if (pair.Value == bundle) {
                        key = pair.Key;
                        break;
                    }
                }
                if (key == null)
                    return;
                foreach (KeyValuePair<long, PlayerRepresentation> pair in PlayerRepresentation.Remotes()) {
                    PlayerRepresentation other = pair.Value;
                    if (other != null && other.currentSkinBundle == bundle)
                        return;
                }
                opened.Remove(key);
                bundle.Unload(false);
            }
            catch { }
        }

        public static void FetchFingerTransforms(PlayerRepresentation rep)
        {
            //Left Hand
            Transform leftBone = rep.activeAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform leftHand = TransformDeepChildExtension.FindDeepChild(leftBone, "l_Hand_2SHJnt");
            Transform leftHandle = TransformDeepChildExtension.FindDeepChild(leftHand, "l_GripPoint_AuxSHJnt");
            Transform leftOpen = TransformDeepChildExtension.FindDeepChild(leftHand.parent, "l_Hand_2SHJnt_open");
            rep.repAnimationManager.leftHandTransform = leftHand;
            rep.repAnimationManager.leftHandleTransform = leftHandle;
            rep.repAnimationManager.leftOpenHandTransform = leftOpen;
            rep.repAnimationManager.leftClosedHandTransform = leftHand;
            //Right Hand
            Transform rightBone = rep.activeAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform rightHand = TransformDeepChildExtension.FindDeepChild(rightBone, "r_Hand_2SHJnt");
            Transform rightHandle = TransformDeepChildExtension.FindDeepChild(rightHand, "r_GripPoint_AuxSHJnt");
            Transform rightOpen = TransformDeepChildExtension.FindDeepChild(rightHand.parent, "r_Hand_2SHJnt_open");
            rep.repAnimationManager.rightHandTransform = rightHand;
            rep.repAnimationManager.rightHandleTransform = rightHandle;
            rep.repAnimationManager.rightOpenHandTransform = rightOpen;
            rep.repAnimationManager.rightClosedHandTransform = rightHand;
            //Calculate
            rep.repAnimationManager.CalculateHandPoseRefs();
        }
    }
}
