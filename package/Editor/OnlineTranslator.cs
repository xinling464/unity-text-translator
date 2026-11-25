using System;
using UnityEditor;
using UnityEditorInternal;

namespace YGTextTranslator
{
    [InitializeOnLoad]
    internal static class OnlineTranslator
    {
        static double _nextSave;
        static double _nextRepaint;
        static bool _refreshPending;

        static OnlineTranslator()
        {
            EditorApplication.update += Tick;
            EditorApplication.quitting += Flush;
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
        }

        internal static void RequestRefresh() => _refreshPending = true;

        static void Tick()
        {
            if (TranslationApi.Drain() > 0) _refreshPending = true;
            var now = EditorApplication.timeSinceStartup;
            if (_refreshPending && now >= _nextRepaint)
            {
                _refreshPending = false;
                _nextRepaint = now + 0.35;
                if (HookHost.Enabled)
                {
                    UiTextHook.RefreshTranslations();
                    InternalEditorUtility.RepaintAllViews();
                }
            }
            if (now < _nextSave) return;
            _nextSave = now + 2;
            TranslationApi.Flush();
            TranslationGlossary.Flush();
        }

        static void Flush()
        {
            TranslationApi.Drain();
            TranslationApi.CancelQueued();
            TranslationApi.Flush();
            TranslationGlossary.Flush();
        }
    }
}
