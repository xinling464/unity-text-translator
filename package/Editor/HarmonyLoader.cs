using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace YGTextTranslator
{
    /// <summary>
    /// 反射加载 0Harmony：优先复用生态里已经加载的那份（NDMF 等），
    /// 否则加载包内 Dependencies~/0Harmony.dll。
    /// 本程序集对 HarmonyLib 零编译期依赖，所有 Harmony 类型都通过反射获取。
    /// </summary>
    internal static class HarmonyLoader
    {
        const string HarmonyId = "com.yg.unity-text-translator";

        const string HarmonyAssemblyName = "0Harmony";
        const string BundledDllRelativePath = "Dependencies~/0Harmony.dll";
        const string ImportedDllRelativePath = "Editor/Dependencies/0Harmony.dll.bytes";

        /// <summary>HarmonyLib.Harmony 实例；null 表示尚未初始化成功。</summary>
        public static object Harmony;

        /// <summary>来源："ndmf"（复用已加载）/"bundled"（包内 dll）/"none"（未找到）。</summary>
        public static string Source = "none";

        static Type _harmonyType;
        static Type _harmonyMethodType;

        /// <summary>加载 0Harmony 并创建 Harmony 实例。已初始化时直接返回 true。</summary>
        public static bool TryInit(out string error)
        {
            error = null;
            if (Harmony != null) return true;

            try
            {
                var assembly = FindLoadedHarmony();
                string source;
                if (assembly != null)
                {
                    source = "ndmf";
                }
                else
                {
                    var dllPath = ResolveBundledDllPath();
                    if (dllPath == null)
                    {
                        error = "0Harmony 未找到，请确认插件依赖已完整导入。";
                        return false;
                    }
                    if (!File.Exists(dllPath))
                    {
                        error = "0Harmony 未找到：文件不存在 " + dllPath;
                        return false;
                    }

                    assembly = Assembly.LoadFrom(dllPath);
                    source = "bundled";
                }

                _harmonyType = assembly.GetType("HarmonyLib.Harmony", true);
                _harmonyMethodType = assembly.GetType("HarmonyLib.HarmonyMethod", true);
                Harmony = Activator.CreateInstance(_harmonyType, HarmonyId);
                if (Harmony == null)
                {
                    error = "HarmonyLib.Harmony 实例创建返回 null";
                    return false;
                }

                Source = source;
                var name = assembly.GetName();
                Debug.Log($"[YGTextTranslator] Harmony 就绪（Source={Source}, id={HarmonyId}, asm={name.Name} {name.Version}）");
                return true;
            }
            catch (Exception ex)
            {
                Harmony = null;
                error = ex.ToString();
                Debug.LogError($"[YGTextTranslator] Harmony 初始化失败：{ex}");
                return false;
            }
        }

        public static bool Patch(MethodInfo original, MethodInfo prefix) => ApplyPatch(original, prefix: prefix);

        public static bool PatchPostfix(MethodInfo original, MethodInfo postfix) => ApplyPatch(original, postfix: postfix);

        public static bool PatchScope(MethodInfo original, MethodInfo prefix, MethodInfo finalizer) =>
            ApplyPatch(original, prefix: prefix, finalizer: finalizer);

        static bool ApplyPatch(MethodInfo original, MethodInfo prefix = null, MethodInfo postfix = null,
            MethodInfo finalizer = null)
        {
            if (Harmony == null || _harmonyType == null || _harmonyMethodType == null)
            {
                Debug.LogError("[YGTextTranslator] Patch 失败：HarmonyLoader 尚未初始化");
                return false;
            }
            if (original == null || (prefix == null && postfix == null && finalizer == null))
            {
                Debug.LogError($"[YGTextTranslator] Patch 失败：目标或补丁方法不存在，original={original}");
                return false;
            }

            try
            {
                var phases = new[] { (Name: "prefix", Method: prefix), (Name: "postfix", Method: postfix),
                    (Name: "finalizer", Method: finalizer) };

                // 各版本 Harmony.Patch 的可选参数个数不同，取签名最贴近 (MethodBase, HarmonyMethod, ...) 的那个。
                var patchMethod = _harmonyType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "Patch")
                    .Where(m => m.GetParameters().Length >= 2
                                && m.GetParameters()[0].ParameterType == typeof(MethodBase)
                                && phases.All(phase => phase.Method == null || m.GetParameters().Any(parameter =>
                                    parameter.Name == phase.Name && parameter.ParameterType == _harmonyMethodType)))
                    .OrderBy(m => m.GetParameters().Length)
                    .FirstOrDefault();
                if (patchMethod == null)
                {
                    Debug.LogError("[YGTextTranslator] Patch 失败：找不到 Harmony.Patch(MethodBase, HarmonyMethod, ...)");
                    return false;
                }

                var parameters = patchMethod.GetParameters();
                var args = new object[parameters.Length];
                args[0] = original;
                foreach (var phase in phases)
                {
                    if (phase.Method == null) continue;
                    var index = Array.FindIndex(parameters, parameter => parameter.Name == phase.Name);
                    args[index] = Activator.CreateInstance(_harmonyMethodType, phase.Method);
                }
                var patched = patchMethod.Invoke(Harmony, args);
                return patched != null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YGTextTranslator] Patch 抛异常（目标 {original.DeclaringType?.FullName}.{original.Name}）：{ex}");
                return false;
            }
        }

        public static void Unpatch(MethodInfo original, MethodInfo patch)
        {
            if (Harmony == null || original == null || patch == null) return;
            try
            {
                var unpatchMethod = _harmonyType.GetMethod("Unpatch", new[] { typeof(MethodBase), typeof(MethodInfo) });
                unpatchMethod?.Invoke(Harmony, new object[] { original, patch });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[YGTextTranslator] 无法撤下 {original.Name} 的显示补丁：{exception.Message}");
            }
        }

        /// <summary>撤掉本插件 id 打过的所有 patch。</summary>
        public static void UnpatchAll()
        {
            if (Harmony == null || _harmonyType == null)
            {
                Debug.LogWarning("[YGTextTranslator] UnpatchAll 跳过：HarmonyLoader 尚未初始化");
                return;
            }

            try
            {
                var unpatchMethod = _harmonyType.GetMethod("UnpatchSelf", Type.EmptyTypes)
                                    ?? _harmonyType.GetMethod("UnpatchAll", new[] { typeof(string) });
                if (unpatchMethod == null)
                {
                    Debug.LogError("[YGTextTranslator] UnpatchAll 失败：Harmony.UnpatchAll 不存在");
                    return;
                }

                var args = unpatchMethod.GetParameters().Length == 0
                    ? Array.Empty<object>()
                    : new object[] { HarmonyId };
                unpatchMethod.Invoke(Harmony, args);
                Debug.Log($"[YGTextTranslator] UnpatchAll 完成（id={HarmonyId}）");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YGTextTranslator] UnpatchAll 抛异常：{ex}");
            }
        }

        static Assembly FindLoadedHarmony()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == HarmonyAssemblyName);
        }

        static string ResolveBundledDllPath()
        {
            var root = TranslatorInstallation.FindRoot();
            if (root == null) return null;
            var packageDll = Path.Combine(root, BundledDllRelativePath);
            if (File.Exists(packageDll)) return packageDll;

            // The imported copy is a TextAsset so Unity does not register a second managed plugin.
            var importedDll = Path.Combine(root, ImportedDllRelativePath);
            return File.Exists(importedDll) ? importedDll : null;
        }
    }
}
