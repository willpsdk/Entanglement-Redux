#if DEBUG
using UnityEngine;

using ModThatIsNotMod;
using ModThatIsNotMod.BoneMenu;

using Entanglement.Representation;
using Entanglement.Objects;
using Entanglement.Voice;

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
                VoiceManager.debugVoiceOnRep = false;
            });

            // Loops your own voice back out of the dummy after 10s, proximity and all, so you can
            // check voice works solo. Needs a dummy spawned (and Steam voice recording, i.e. a lobby).
            debugCategory.CreateBoolElement("Voice Chat Debug On Rep", Color.white, false, (value) => {
                if (value && PlayerRepresentation.debugRepresentation == null) {
                    Notifications.SendNotification("Spawn a debug representation first.", 3f);
                    VoiceManager.debugVoiceOnRep = false;
                    return;
                }

                VoiceManager.debugVoiceOnRep = value;
                if (value)
                    Notifications.SendNotification("Speak - you'll hear it back from the dummy in 10s.", 4f);
            });
        }
    }
}
#endif