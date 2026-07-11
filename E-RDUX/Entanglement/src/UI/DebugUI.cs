#if DEBUG
using UnityEngine;

using ModThatIsNotMod.BoneMenu;

using Entanglement.Representation;
using Entanglement.Objects;

namespace Entanglement.UI {
    public static class DebugUI {
        public static void CreateUI(MenuCategory category) {
            MenuCategory debugCategory = category.CreateSubCategory("Debug", Color.red);

            debugCategory.CreateFunctionElement("Create Debug Representation", Color.white, () => {
                // Clean up the previous dummy first, otherwise it lingers frozen in the world
                PlayerRepresentation.debugRepresentation?.DeleteRepresentations();
                PlayerRepresentation.debugRepresentation = new PlayerRepresentation("Dummy", 0);
            });

            debugCategory.CreateFunctionElement("Remove Debug Representation", Color.white, () => {
                PlayerRepresentation.debugRepresentation?.DeleteRepresentations();
                PlayerRepresentation.debugRepresentation = null;
            });
        }
    }
}
#endif