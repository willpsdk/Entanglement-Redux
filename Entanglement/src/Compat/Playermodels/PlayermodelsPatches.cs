using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;

using HarmonyLib;

using Entanglement.Representation;
using Entanglement.Patching;
using Entanglement.Network;
using Entanglement.Extensions;
using Entanglement.UI;

using UnityEngine;

using MelonLoader;

using Entanglement.Compat;

namespace Entanglement.Compat.Playermodels
{
    [OptionalAssemblyTarget("PlayerModels")]
    public class PlayermodelsPatch : OptionalAssemblyPatch {

        //Rtas: Path.Combine() doesn't seem to work correctly in this scenario - this commented out version
        //of the function seems to only return "\PlayerModels\", at-least on my build environment
        //Path.Combine(MelonUtils.UserDataDirectory, @"\PlayerModels\");
        
        public static string playerModelsPath {
            get {
                string path = Path.Combine(MelonUtils.UserDataDirectory, "PlayerModels");
                try {
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                }
                catch { }
                return path;
            }
        }

        public static Type skinLoadingType;
        public static FieldInfo currentBundleInfo;
        public static string lastLoadedPath = null;

        /// <summary>True when the PlayerModels Melon is loaded and patched.</summary>
        public static bool PlayerModelsLoaded => skinLoadingType != null;

        public override void DoPatches(Assembly target) {
            skinLoadingType = target.GetType("PlayerModels.SkinLoading");
            if (skinLoadingType == null) {
                EntangleLogger.Warn("[PlayerModel] PlayerModels.SkinLoading type missing — local wear patches skipped.");
                return;
            }

            currentBundleInfo = skinLoadingType.GetField("currentLoadedBundle", BindingFlags.Public | BindingFlags.Static);

            MethodBase applyModelMethod = skinLoadingType.GetMethod("ApplyPlayerModel", BindingFlags.Public | BindingFlags.Static);
            MethodInfo applyPostfixMethod = typeof(PlayermodelsPatch).GetMethod("ApplyPlayerModel_Postfix", BindingFlags.Public | BindingFlags.Static);
            MethodInfo applyPrefixMethod = typeof(PlayermodelsPatch).GetMethod("ApplyPlayerModel_Prefix", BindingFlags.Public | BindingFlags.Static);

            MethodBase clearModelMethod = skinLoadingType.GetMethod("ClearPlayerModel", BindingFlags.Public | BindingFlags.Static);
            MethodInfo clearPrefixMethod = typeof(PlayermodelsPatch).GetMethod("ClearPlayerModel_Prefix", BindingFlags.Public | BindingFlags.Static);

            if (applyModelMethod != null)
                Patcher.Patch(applyModelMethod, new HarmonyMethod(applyPrefixMethod), new HarmonyMethod(applyPostfixMethod));
            else
                EntangleLogger.Warn("[PlayerModel] ApplyPlayerModel not found on PlayerModels.");

            if (clearModelMethod != null)
                Patcher.Patch(clearModelMethod, new HarmonyMethod(clearPrefixMethod));

            // LoadCustomPlayerMessageHandler is registered from PlayermodelSync.Initialize so
            // remotes can see skins even when the PlayerModels Melon isn't installed locally.
        }

        public static void ClearPlayerModel_Prefix() {
            //Unload Without Destroying Assets
            try {
                object obj = currentBundleInfo != null ? currentBundleInfo.GetValue(null) : null;
                if (obj != null)
                    ((AssetBundle)obj)?.Unload(false);
            }
            catch { }
            //Broadcast
            BroadcastPlayermodel(" ");
            //Remove
            lastLoadedPath = null;
#if DEBUG
            if (PlayerRepresentation.debugRepresentation != null) PlayerSkinLoader.ClearPlayermodel(PlayerRepresentation.debugRepresentation);
#endif
        }

        public static void ApplyPlayerModel_Prefix(string path) => AssetBundleUtilities.TryUnloadBundle(path, false);

        public static void ApplyPlayerModel_Postfix(string path) {
            //Rename to Path Name
            lastLoadedPath = path;
            string fileName = Path.GetFileName(path).ToLower();
            try {
                object obj = currentBundleInfo != null ? currentBundleInfo.GetValue(null) : null;
                if (obj != null) {
                    AssetBundle loadedBundle = (AssetBundle)obj;
                    if (loadedBundle)
                        loadedBundle.name = fileName;
                }
            }
            catch { }
            //Broadcast
            BroadcastPlayermodel(path);
#if DEBUG
            if (PlayerRepresentation.debugRepresentation != null) {
                string full = Path.IsPathRooted(path) ? path : Path.Combine(playerModelsPath, Path.GetFileName(path));
                PlayerSkinLoader.ApplyPlayermodel(PlayerRepresentation.debugRepresentation, full);
            }
#endif
        }

        public static void BroadcastPlayermodel(string path) {
            if (Node.activeNode == null)
                return;
            LoadCustomPlayerMessageData msgData = new LoadCustomPlayerMessageData();
            msgData.userId = SteamIntegration.currentUserId;
            msgData.modelPath = Path.GetFileName(path);

            Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, NetworkMessage.CreateMessage(CompatMessageType.PlayerModel, msgData).GetBytes());
        }

        public static void AddMissingToMenu() {
            try {
                string folder = playerModelsPath;
                if (!Directory.Exists(folder))
                    return;

                // Optional BoneMenu listing — skins still apply to remotes without MTINM/BoneMenu.
                object category = BoneMenuReflect.FindCategory("Player Models");
                if (category == null) {
                    EntangleLogger.Debug("[PlayerModel] No BoneMenu 'Player Models' category; files are on disk for sync/apply.");
                    return;
                }

                string[] files = Directory.GetFiles(folder);
                int added = 0;
                for (int i = 0; i < files.Length; i++) {
                    string label = Path.GetFileName(files[i]);
                    if (string.IsNullOrEmpty(label) || !label.EndsWith(".body", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (BoneMenuReflect.CategoryHas(category, label))
                        continue;

                    string captured = files[i];
                    if (!BoneMenuReflect.CreateFunction(category, label, Color.grey, () => {
                        try { WearPath(captured); }
                        catch (Exception wear) {
                            EntangleLogger.Warn("Could not wear " + label + ": " + wear.Message);
                            GameNotify.Show("Could not put on " + label + ".", 6f);
                        }
                    }))
                        continue;
                    added++;
                }

                if (added == 1)
                    GameNotify.Show("Added a model to Player Models.", 6f);
                else if (added > 1)
                    GameNotify.Show("Added " + added + " models to Player Models.", 6f);
            }
            catch (Exception e) {
                EntangleLogger.Warn("Could not add model to Player Models menu: " + e.Message);
            }
        }

        public static void Wear(string fileName) {
            if (string.IsNullOrEmpty(fileName))
                return;
            WearPath(Path.Combine(playerModelsPath, Path.GetFileName(fileName)));
        }

        public static void WearPath(string fullPath) {
            if (skinLoadingType == null)
                throw new InvalidOperationException("Player Models is not loaded — install the PlayerModels Melon to wear models locally.");
            MethodInfo apply = skinLoadingType.GetMethod("ApplyPlayerModel", BindingFlags.Public | BindingFlags.Static);
            if (apply == null)
                throw new MissingMethodException("Player Models has no ApplyPlayerModel");
            apply.Invoke(null, new object[] { fullPath });
            GameNotify.Show("Now wearing " + Path.GetFileName(fullPath) + ".", 4f, GameNotify.Kind.Success);
        }
    }
}
