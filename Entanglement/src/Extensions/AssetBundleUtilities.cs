using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using UnhollowerBaseLib;

using UnityEngine;

namespace Entanglement.Extensions
{
    public static class AssetBundleUtilities {
        public static void TryUnloadBundle(string path, bool unloadAllLoadedObjects) {
            string fileName = Path.GetFileName(path);
            AssetBundle existingBundle = TryGetBundle(fileName);
            existingBundle?.Unload(unloadAllLoadedObjects);
        }

        public static AssetBundle TryLoadBundle(string path, string userDataFolder) {
            string fileName = Path.GetFileName(path);
            // Try to find an existing AssetBundle
            AssetBundle existingBundle = TryGetBundle(fileName);
            // Load a new bundle if its not already loaded
            if (!existingBundle)
            {
                existingBundle = AssetBundle.LoadFromFile(Path.Combine(userDataFolder, fileName));
                if (existingBundle) existingBundle.name = fileName.ToLower();
            }
            return existingBundle;
        }
        public static AssetBundle TryGetBundle(string fileName) {
            fileName = fileName.ToLower();
            Il2CppArrayBase<AssetBundle> bundles = AssetBundle.GetAllLoadedAssetBundles().Cast<Il2CppArrayBase<AssetBundle>>();
            foreach (AssetBundle bundle in bundles)
            {
                if (bundle.name.ToLower() == fileName)
                    return bundle;
            }
            return null;
        }
    }
}
