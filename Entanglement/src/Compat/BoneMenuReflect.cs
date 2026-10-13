using System;
using System.Collections;
using System.Reflection;

using UnityEngine;

namespace Entanglement.Compat
{
    /// <summary>
    /// Optional ModThatIsNotMod BoneMenu access via reflection.
    /// No compile-time MTINM / BoneMenu dependency — no-ops if BoneMenu isn't loaded.
    /// </summary>
    public static class BoneMenuReflect
    {
        static Type menuManagerType;
        static Type menuCategoryType;
        static FieldInfo categoriesField;
        static bool resolved;
        static bool available;

        static void Resolve() {
            if (resolved)
                return;
            resolved = true;
            try {
                menuManagerType = Type.GetType("ModThatIsNotMod.BoneMenu.MenuManager, ModThatIsNotMod");
                menuCategoryType = Type.GetType("ModThatIsNotMod.BoneMenu.MenuCategory, ModThatIsNotMod");
                if (menuManagerType == null || menuCategoryType == null)
                    return;
                categoriesField = menuManagerType.GetField("categories", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                available = categoriesField != null;
            }
            catch {
                available = false;
            }
        }

        public static bool IsAvailable {
            get {
                Resolve();
                return available;
            }
        }

        public static object FindCategory(string displayText) {
            Resolve();
            if (!available)
                return null;
            try {
                IList list = categoriesField.GetValue(null) as IList;
                if (list == null)
                    return null;
                for (int i = 0; i < list.Count; i++) {
                    object cat = list[i];
                    if (cat == null)
                        continue;
                    string text = cat.GetType().GetField("displayText")?.GetValue(cat) as string
                        ?? cat.GetType().GetProperty("displayText")?.GetValue(cat, null) as string;
                    if (string.Equals(text, displayText, StringComparison.Ordinal))
                        return cat;
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn("[BoneMenu] FindCategory: " + e.Message);
            }
            return null;
        }

        public static bool CategoryHas(object category, string label) {
            if (category == null || string.IsNullOrEmpty(label))
                return false;
            try {
                object elements = category.GetType().GetField("elements")?.GetValue(category)
                    ?? category.GetType().GetProperty("elements")?.GetValue(category, null);
                IList list = elements as IList;
                if (list == null)
                    return false;
                for (int i = 0; i < list.Count; i++) {
                    object el = list[i];
                    if (el == null)
                        continue;
                    string text = el.GetType().GetField("displayText")?.GetValue(el) as string
                        ?? el.GetType().GetProperty("displayText")?.GetValue(el, null) as string;
                    if (text == label)
                        return true;
                }
            }
            catch { }
            return false;
        }

        public static bool CreateFunction(object category, string label, Color color, Action action) {
            if (category == null || string.IsNullOrEmpty(label) || action == null)
                return false;
            try {
                MethodInfo method = category.GetType().GetMethod(
                    "CreateFunctionElement",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(string), typeof(Color), typeof(Action) },
                    null);
                if (method == null) {
                    // Some builds take (string, Color, Action, string) or similar — try name-only.
                    foreach (MethodInfo m in category.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                        if (m.Name != "CreateFunctionElement")
                            continue;
                        ParameterInfo[] ps = m.GetParameters();
                        if (ps.Length >= 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(Color)) {
                            method = m;
                            break;
                        }
                    }
                }
                if (method == null)
                    return false;

                ParameterInfo[] parms = method.GetParameters();
                object[] args = new object[parms.Length];
                args[0] = label;
                args[1] = color;
                args[2] = action;
                for (int i = 3; i < parms.Length; i++) {
                    if (parms[i].ParameterType == typeof(string))
                        args[i] = "";
                    else if (parms[i].HasDefaultValue)
                        args[i] = parms[i].DefaultValue;
                    else
                        args[i] = null;
                }
                method.Invoke(category, args);
                return true;
            }
            catch (Exception e) {
                EntangleLogger.Warn("[BoneMenu] CreateFunction: " + e.Message);
                return false;
            }
        }
    }
}
