using System.IO;
using UnityEditor;

namespace YGTextTranslator
{
    internal static class QuickMenu
    {
        const string Root = "团子冲击！";

        [MenuItem(Root + "/刷新翻译", false, 1)]
        static void RefreshNow()
        {
            TranslationApi.RetryFailures();
            Bootstrap.SetEnabled(true);
            Bootstrap.Refresh();
        }

        [MenuItem(Root + "/暂停翻译", false, 2)]
        static void Pause()
        {
            Bootstrap.SetEnabled(false);
        }

        [MenuItem(Root + "/显示原文 %#y", false, 3)]
        static void ToggleOriginal()
        {
            Bootstrap.SetShowOriginal(!TranslatorPreferences.ShowOriginal);
        }

        [MenuItem(Root + "/显示原文 %#y", true)]
        static bool ValidateToggleOriginal()
        {
            Menu.SetChecked(Root + "/显示原文", TranslatorPreferences.ShowOriginal);
            return true;
        }

        [MenuItem(Root + "/打开设置面板…", false, 21)]
        static void OpenSettings() => TranslatorWindow.OpenSettings();

        [MenuItem(Root + "/中英命令搜索…", false, 22)]
        static void OpenCommands() => TranslatorWindow.OpenCommands();

        [MenuItem(Root + "/目录补丁…", false, 23)]
        static void OpenCatalog() => TranslatorWindow.OpenCatalog();

        [MenuItem(Root + "/中文定位…", false, 24)]
        static void OpenNativeAliasSearch() => NativeAliasWindow.Open();

        [MenuItem(Root + "/打开数据目录", false, 40)]
        static void OpenDataDir()
        {
            Directory.CreateDirectory(CatalogPatcher.DataDir);
            EditorUtility.RevealInFinder(CatalogPatcher.DataDir);
        }
    }
}
