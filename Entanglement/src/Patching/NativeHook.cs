using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

using MelonLoader;

using UnhollowerBaseLib;
using UnhollowerBaseLib.Runtime;

namespace Entanglement.Patching
{
    // For game methods Harmony can't patch safely: ones that take a struct holding object
    // references, like Attack. MelonLoader's patcher reads that argument as a boxed object, but the
    // game passes a pointer to the bare struct, so the patch and the original both read the wrong
    // memory. A native hook takes the argument the way the game passes it, and runs the original
    // through il2cpp_runtime_invoke the way MelonLoader does.
    public sealed class NativeHook
    {
        // The game calls into these, so they must never be collected.
        static readonly List<Delegate> detours = new List<Delegate>();

        readonly string name;
        IntPtr original;

        public NativeHook(string name) {
            this.name = name;
        }

        public void Attach(Type type, string method, Delegate detour) {
            try {
                MethodInfo target = type.GetMethod(method);
                IntPtr info = (IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(target).GetValue(null);
                if (info == IntPtr.Zero)
                    throw new InvalidOperationException("no method info");

                // Hooking this copy of the method info turns its code pointer into one that runs the
                // original, which is what Invoke calls.
                IntPtr copy = UnityVersionHandler.CopyMethodInfoStruct(info);
                original = copy;
                detours.Add(detour);
                MelonUtils.NativeHookAttach(copy, Marshal.GetFunctionPointerForDelegate(detour));
            }
            catch (Exception e) {
                EntangleLogger.Error("[NativeHook] Could not hook " + name + ": " + e.Message);
            }
        }

        // An exception in the game's code comes back here instead of unwinding through ours.
        public unsafe IntPtr Invoke(IntPtr instance, IntPtr* args) {
            IntPtr exception = IntPtr.Zero;
            IntPtr result = IL2CPP.il2cpp_runtime_invoke(original, instance, (void**)args, ref exception);
            if (exception != IntPtr.Zero) {
                try { PatchGuard.Report(name + " (the game's own)", new UnhollowerBaseLib.Il2CppException(exception)); }
                catch { }
            }
            return result;
        }
    }
}
