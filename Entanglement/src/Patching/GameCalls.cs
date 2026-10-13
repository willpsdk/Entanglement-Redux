using System;
using System.Reflection;

using UnhollowerBaseLib;

using UnityEngine;

using StressLevelZero.Pool;

namespace Entanglement.Patching
{
    // Calls into game methods that take a Nullable. Unhollower's Il2CppSystem.Nullable<T>
    // constructor never allocates the object it fills in, so building one always throws "Object was
    // garbage collected in IL2CPP domain": the NPC restart and the despawn below never ran. These
    // pass the struct itself through il2cpp_runtime_invoke, laid out from the game's own class data.
    public static class GameCalls
    {
        // Where a Nullable<T>'s value and has_value sit in the struct, and its size.
        sealed class NullableLayout {
            public int size, value, hasValue;
            public bool ok;

            public static NullableLayout Of(IntPtr klass) {
                NullableLayout layout = new NullableLayout();
                try {
                    uint align = 0;
                    layout.size = IL2CPP.il2cpp_class_value_size(klass, ref align);
                    // Field offsets count the object header of the boxed form.
                    int header = IntPtr.Size * 2;
                    layout.value = (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(klass, "value")) - header;
                    layout.hasValue = (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(klass, "has_value")) - header;
                    layout.ok = layout.size > 0 && layout.size <= 64 && layout.value >= 0
                        && layout.hasValue > layout.value && layout.hasValue < layout.size;
                }
                catch { layout.ok = false; }
                if (!layout.ok)
                    EntangleLogger.Warn("[GameCalls] Could not read a Nullable layout");
                return layout;
            }
        }

        /// <summary>Where a field sits in a struct's raw data, or -1.</summary>
        public static int FieldOffset(IntPtr klass, string field) {
            try {
                IntPtr info = IL2CPP.GetIl2CppField(klass, field);
                if (info == IntPtr.Zero)
                    return -1;
                // Field offsets count the object header of the boxed form.
                return (int)IL2CPP.il2cpp_field_get_offset(info) - IntPtr.Size * 2;
            }
            catch { return -1; }
        }

        static NullableLayout vector3Layout, boolLayout, colorLayout;
        static IntPtr onSpawnInfo, despawnInfo;

        static IntPtr MethodInfoOf(Type type, string name) {
            try {
                MethodInfo method = type.GetMethod(name);
                return (IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method).GetValue(null);
            }
            catch (Exception e) {
                EntangleLogger.Warn("[GameCalls] No method info for " + type.Name + "." + name + ": " + e.Message);
                return IntPtr.Zero;
            }
        }

        /// <summary>Poolee.OnSpawn(scale): sets the scale, stamps the spawn and runs its spawn delegate,
        /// which is what brings a pooled NPC back to life.</summary>
        public static unsafe bool PooleeOnSpawn(Poolee poolee, Vector3 scale) {
            if (poolee == null)
                return false;
            if (onSpawnInfo == IntPtr.Zero)
                onSpawnInfo = MethodInfoOf(typeof(Poolee), "OnSpawn");
            if (vector3Layout == null)
                vector3Layout = NullableLayout.Of(Il2CppClassPointerStore<Il2CppSystem.Nullable<Vector3>>.NativeClassPtr);
            if (onSpawnInfo == IntPtr.Zero || !vector3Layout.ok)
                return false;

            byte* arg = stackalloc byte[vector3Layout.size];
            Clear(arg, vector3Layout.size);
            *(Vector3*)(arg + vector3Layout.value) = scale;
            arg[vector3Layout.hasValue] = 1;

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)arg;
            return Invoke(onSpawnInfo, poolee.Pointer, args, "Poolee.OnSpawn");
        }

        /// <summary>Poolee.Despawn(playVFX, despawnColor): runs its despawn delegate, which hands it
        /// back to its pool and spawner, then switches it off.</summary>
        public static unsafe bool PooleeDespawn(Poolee poolee, bool playVFX, Color color) {
            if (poolee == null)
                return false;
            if (despawnInfo == IntPtr.Zero)
                despawnInfo = MethodInfoOf(typeof(Poolee), "Despawn");
            if (boolLayout == null)
                boolLayout = NullableLayout.Of(Il2CppClassPointerStore<Il2CppSystem.Nullable<bool>>.NativeClassPtr);
            if (colorLayout == null)
                colorLayout = NullableLayout.Of(Il2CppClassPointerStore<Il2CppSystem.Nullable<Color>>.NativeClassPtr);
            if (despawnInfo == IntPtr.Zero || !boolLayout.ok || !colorLayout.ok)
                return false;

            byte* vfx = stackalloc byte[boolLayout.size];
            Clear(vfx, boolLayout.size);
            vfx[boolLayout.value] = (byte)(playVFX ? 1 : 0);
            vfx[boolLayout.hasValue] = 1;

            byte* tint = stackalloc byte[colorLayout.size];
            Clear(tint, colorLayout.size);
            *(Color*)(tint + colorLayout.value) = color;
            tint[colorLayout.hasValue] = 1;

            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)vfx;
            args[1] = (IntPtr)tint;
            return Invoke(despawnInfo, poolee.Pointer, args, "Poolee.Despawn");
        }

        static unsafe void Clear(byte* buffer, int size) {
            for (int i = 0; i < size; i++)
                buffer[i] = 0;
        }

        // The same call unhollower makes, virtual dispatch included. A game exception comes back
        // here instead of unwinding through our code.
        static unsafe bool Invoke(IntPtr method, IntPtr instance, IntPtr* args, string name) {
            if (instance == IntPtr.Zero)
                return false;
            IntPtr target = IL2CPP.il2cpp_object_get_virtual_method(instance, method);
            if (target == IntPtr.Zero)
                target = method;
            IntPtr exception = IntPtr.Zero;
            IL2CPP.il2cpp_runtime_invoke(target, instance, (void**)args, ref exception);
            if (exception != IntPtr.Zero) {
                try { PatchGuard.Report(name + " (the game's own)", new UnhollowerBaseLib.Il2CppException(exception)); }
                catch { }
            }
            return true;
        }
    }
}
