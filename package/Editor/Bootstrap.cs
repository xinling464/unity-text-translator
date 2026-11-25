using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace YGTextTranslator
{
    [InitializeOnLoad]
    internal static class Bootstrap
    {
        static Bootstrap()
        {
            EditorApplication.delayCall += Start;
            AssemblyReloadEvents.beforeAssemblyReload += HookHost.Disable;
        }

        static void Start()
        {
            if (TranslatorPreferences.RuntimeEnabled) Enable();
        }

        internal static void SetEnabled(bool enabled)
        {
            TranslatorPreferences.RuntimeEnabled = enabled;
            if (enabled) Enable();
            else
            {
                HookHost.Disable();
                TranslationApi.CancelQueued();
            }
            InternalEditorUtility.RepaintAllViews();
        }

        internal static void SetShowOriginal(bool showOriginal)
        {
            TranslatorPreferences.ShowOriginal = showOriginal;
            if (showOriginal) TranslationApi.CancelQueued();
            UiTextHook.RefreshTranslations();
            NativeAliasHooks.Refresh();
            InternalEditorUtility.RepaintAllViews();
        }

        internal static void Refresh()
        {
            TextPipeline.Reload();
            UiTextHook.RefreshTranslations();
            NativeAliasHooks.Refresh();
            if (!HookHost.Enabled && TranslatorPreferences.RuntimeEnabled) Enable();
            else UiTextHook.RefreshOpenViews();
            InternalEditorUtility.RepaintAllViews();
        }

        static void Enable()
        {
            try
            {
                if (!HookHost.Enabled) HookHost.Enable();
                if (HookHost.Enabled)
                {
                    UiTextHook.TryInit();
                    NativeAliasHooks.Enable();
                }
                UiTextHook.RefreshOpenViews();
            }
            catch (Exception exception)
            {
                Debug.LogError("[YGTextTranslator] 启用显示层拦截失败：" + exception);
            }
        }
    }
}
