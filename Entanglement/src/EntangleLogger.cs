using System;

using MelonLoader;

namespace Entanglement {
    public static class EntangleLogger {
        static MelonLogger.Instance Logger {
            get {
                try {
                    EntanglementMod mod = EntanglementMod.Instance;
                    if (mod != null)
                        return mod.LoggerInstance;
                }
                catch { }
                return null;
            }
        }

        public static void Log(string txt, ConsoleColor txt_color = ConsoleColor.White) {
            MelonLogger.Instance logger = Logger;
            if (logger != null) logger.Msg(txt_color, txt);
            else MelonLogger.Msg(txt_color, "[Entanglement] " + txt);
        }

        public static void Log(object obj, ConsoleColor txt_color = ConsoleColor.White) {
            MelonLogger.Instance logger = Logger;
            if (logger != null) logger.Msg(txt_color, obj);
            else MelonLogger.Msg(txt_color, "[Entanglement] " + obj);
        }

        public static bool verbose;

        public static void Debug(string txt, ConsoleColor txt_color = ConsoleColor.White) {
            if (verbose)
                Log(txt, txt_color);
        }

        public static void Warn(string txt) {
            MelonLogger.Instance logger = Logger;
            if (logger != null) logger.Warning(txt);
            else MelonLogger.Warning("[Entanglement] " + txt);
        }

        public static void Warn(object obj) {
            MelonLogger.Instance logger = Logger;
            if (logger != null) logger.Warning(obj);
            else MelonLogger.Warning("[Entanglement] " + obj);
        }

        public static void Error(string txt) {
            MelonLogger.Instance logger = Logger;
            if (logger != null) logger.Error(txt);
            else MelonLogger.Error("[Entanglement] " + txt);
        }

        public static void Error(object obj) {
            MelonLogger.Instance logger = Logger;
            if (logger != null) logger.Error(obj);
            else MelonLogger.Error("[Entanglement] " + obj);
        }
    }
}
