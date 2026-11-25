using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YGTextTranslator
{
    internal static class UiTextHook
    {
        const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static readonly PropertyInfo InspectorEditor = typeof(InspectorElement).GetProperty("editor", InstanceMembers);
        static readonly Type HostView = typeof(EditorWindow).Assembly.GetType("UnityEditor.HostView");
        static readonly PropertyInfo ActualView = HostView?.GetProperty("actualView", InstanceMembers);
        static readonly PropertyInfo PanelOwner = typeof(VisualElement).Assembly
            .GetType("UnityEngine.UIElements.BaseVisualElementPanel")?.GetProperty("ownerObject", InstanceMembers);
        static readonly MethodInfo IncrementVersion = typeof(VisualElement).GetMethod("IncrementVersion", InstanceMembers);
        static readonly object[] LayoutAndRepaint = GetRefreshFlags();

        static readonly List<WeakReference<TextElement>> Observed = new List<WeakReference<TextElement>>();
        static ConditionalWeakTable<TextElement, TranslationState> _states =
            new ConditionalWeakTable<TextElement, TranslationState>();

        static bool _attempted;
        static bool _patched;
        static int _mainThreadId;

        internal static bool Patched => _patched;

        sealed class TranslationState
        {
            public string Original;
            public string PackageId;
            public string Translation;
        }

        internal static bool TryInit()
        {
            if (_attempted) return _patched;
            _attempted = true;
            _mainThreadId = Environment.CurrentManagedThreadId;

            var getter = typeof(TextElement).GetProperty("renderedText", InstanceMembers)?.GetGetMethod(true);
            if (getter == null)
            {
                Debug.LogWarning("[YGTextTranslator] 当前 Unity 版本没有 UI Toolkit 的显示文字入口。");
                return false;
            }

            var postfix = typeof(UiTextHook).GetMethod(nameof(TranslateRenderedText),
                BindingFlags.Static | BindingFlags.NonPublic);
            _patched = HarmonyLoader.PatchPostfix(getter, postfix);
            return _patched;
        }

        internal static void NotifyUnpatched()
        {
            _attempted = false;
            _patched = false;
            RefreshTranslations();
            Observed.Clear();
            _states = new ConditionalWeakTable<TextElement, TranslationState>();
        }

        // Invalidate existing panels when enabling, including labels whose meshes were already built.
        internal static void RefreshOpenViews()
        {
            if (!_patched) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null) continue;
                var root = window.rootVisualElement;
                if (root == null) continue;
                foreach (var element in root.Query<TextElement>().ToList())
                {
                    if (IsDisplayControl(element) && ResolvePackageId(element) != null)
                        Invalidate(element);
                }
            }
        }

        // Results, glossary changes and pause affect the controls encountered by the render hook.
        internal static void RefreshTranslations()
        {
            for (var i = Observed.Count - 1; i >= 0; i--)
            {
                if (!Observed[i].TryGetTarget(out var element))
                {
                    Observed.RemoveAt(i);
                    continue;
                }

                if (_states.TryGetValue(element, out var state)) state.Original = null;
                if (element.panel != null) Invalidate(element);
            }
        }

        static void TranslateRenderedText(TextElement __instance, ref string __result)
        {
            if (!HookHost.Enabled || TextPipeline.Suppress || __result == null ||
                Environment.CurrentManagedThreadId != _mainThreadId || !IsDisplayControl(__instance))
                return;

            try
            {
                var original = __instance.text;
                if (string.IsNullOrWhiteSpace(original)) return;

                // Unity appends a zero-width space for layout. Leave custom text shaping and masking intact.
                var zeroWidthSuffix = __result.Length == original.Length + 1 &&
                    __result[original.Length] == '\u200B' && __result.StartsWith(original, StringComparison.Ordinal);
                if (!zeroWidthSuffix && !string.Equals(__result, original, StringComparison.Ordinal)) return;

                var packageId = ResolvePackageId(__instance);
                if (packageId == null) return;

                if (!_states.TryGetValue(__instance, out var state))
                {
                    state = new TranslationState();
                    _states.Add(__instance, state);
                    if (Observed.Count >= 256 && Observed.Count % 256 == 0)
                        Observed.RemoveAll(reference => !reference.TryGetTarget(out _));
                    Observed.Add(new WeakReference<TextElement>(__instance));
                }

                if (!string.Equals(state.Original, original, StringComparison.Ordinal) || state.PackageId != packageId)
                {
                    state.Translation = TextPipeline.TranslateDisplay(original, packageId);
                    state.Original = original;
                    state.PackageId = packageId;
                }

                if (state.Translation != original)
                    __result = zeroWidthSuffix ? state.Translation + '\u200B' : state.Translation;
            }
            catch (Exception exception)
            {
                HookHost.ReportTranslationFailure(nameof(TranslateRenderedText), exception);
            }
        }

        static bool IsDisplayControl(TextElement element)
        {
            return (element is Label || element is Button) && string.IsNullOrEmpty(element.bindingPath) &&
                element.panel?.contextType == ContextType.Editor;
        }

        internal static string ResolvePackageId(VisualElement element)
        {
            if (element.panel?.contextType != ContextType.Editor) return null;
            string packageId = null;
            for (var current = element; current != null; current = current.parent)
            {
                // Native collection rows, object references, and popup values can show project data.
                if (current is BaseVerticalCollectionView || current is ObjectField ||
                    current.ClassListContains("unity-base-popup-field__input"))
                    return null;

                packageId = packageId ?? DisplayScope.GetPackageId(current.GetType());
                if (current is InspectorElement)
                {
                    var editor = InspectorEditor?.GetValue(current) as Editor;
                    return packageId ?? (editor != null ? DisplayScope.GetPackageId(editor.GetType()) : null);
                }
            }

            if (packageId != null) return packageId;
            var owner = PanelOwner?.GetValue(element.panel);
            var window = owner as EditorWindow;
            if (window == null && owner != null && HostView?.IsInstanceOfType(owner) == true)
                window = ActualView?.GetValue(owner) as EditorWindow;
            return window != null ? DisplayScope.GetPackageId(window.GetType()) : null;
        }

        static object[] GetRefreshFlags()
        {
            if (IncrementVersion == null) return null;
            var enumType = IncrementVersion.GetParameters()[0].ParameterType;
            if (!Enum.IsDefined(enumType, "Layout") || !Enum.IsDefined(enumType, "Repaint")) return null;
            return new[] { Enum.Parse(enumType, "Layout, Repaint") };
        }

        static void Invalidate(TextElement element)
        {
            if (LayoutAndRepaint != null)
            {
                try
                {
                    IncrementVersion.Invoke(element, LayoutAndRepaint);
                    return;
                }
                catch (TargetInvocationException) { }
            }
            element.MarkDirtyRepaint();
        }
    }
}
