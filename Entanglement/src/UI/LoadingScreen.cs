using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Network;
using Entanglement.Data;

using UnityEngine;
using UnityEngine.UI;

namespace Entanglement.UI {
    public static class LoadingScreen {
        public static AssetBundle assetBundle;

        public static void LoadBundle() {
            assetBundle = EmebeddedAssetBundle.LoadFromAssembly(EntanglementMod.entanglementAssembly, "Entanglement.resources.logo.eres");
        }

        public static void OverrideScreen() {
            if (assetBundle == null)
                return;

            try {
                Texture2D texture = assetBundle.LoadAsset<Texture2D>("entanglement.png");
                if (texture == null)
                    return;

                GameObject rawImage = GameObject.Find("Canvas/RawImage (1)");
                if (rawImage == null)
                    return;

                RawImage image = rawImage.GetComponent<RawImage>();
                if (image == null)
                    return;

                image.texture = texture;
            }
            catch { }
        }
    }
}
