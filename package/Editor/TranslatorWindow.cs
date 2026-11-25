using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace YGTextTranslator
{
    public partial class TranslatorWindow : EditorWindow
    {
        enum Page { Settings, Glossary, Catalog, Commands, Diagnostics }

        const string OfficialRepositoryUrl = "https://github.com/xinling464/unity-text-translator";

        static readonly string[] Tabs = { "显示与在线", "术语词库", "目录改动", "命令搜索", "诊断" };
        static readonly string[] BackendLabels = { "MyMemory", "有道", "DeepL", "百度", "OpenAI 兼容" };

        [SerializeField] Page _page;
        Vector2 _scroll;
        string _version;
        PackageInfo[] _packages = Array.Empty<PackageInfo>();
        string[] _assetScopeIds = Array.Empty<string>();
        bool _showPackageSettings;
        bool _showNativeAliases;
        string _packageSearch = "";
        bool _testing;
        string _testResult;
        TranslationApi.Backend? _editedBackend;
        bool _configurationDirty;
        string _apiKey;
        string _apiSecret;
        string _apiEndpoint;
        string _apiModel;

        [MenuItem("Tools/团子冲击！/插件文本翻译")]
        internal static void OpenSettings() => Open(Page.Settings);

        internal static void OpenCatalog() => Open(Page.Catalog);
        internal static void OpenCommands() => Open(Page.Commands);

        static void Open(Page page)
        {
            var window = GetWindow<TranslatorWindow>("团子冲击！");
            window.SelectPage(page);
            if (page == Page.Commands) window._focusCommandSearch = true;
        }

        void OnEnable()
        {
            minSize = new Vector2(440, 325);
            _version = TranslatorInstallation.ReadVersion();
        }

        void OnInspectorUpdate()
        {
            if (_page == Page.Settings || _page == Page.Diagnostics) Repaint();
        }

        void SelectPage(Page page)
        {
            if (_page == page) return;
            _page = page;
            _scroll = Vector2.zero;
            if (page == Page.Commands) _focusCommandSearch = true;
            Repaint();
        }

        void OnGUI()
        {
            var previousCapture = HookHost.SuppressCapture;
            var previousSuppress = TextPipeline.Suppress;
            HookHost.SuppressCapture = true;
            TextPipeline.Suppress = true;
            try
            {
                GUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"作者 YG（@xinling464） · {_version ?? "未知版本"}", EditorStyles.miniLabel);
                    if (GUILayout.Button("官方仓库", EditorStyles.miniButton, GUILayout.Width(76)))
                        Application.OpenURL(OfficialRepositoryUrl);
                }
                GUILayout.Space(4);
                SelectPage((Page)GUILayout.Toolbar((int)_page, Tabs));
                using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
                {
                    _scroll = scroll.scrollPosition;
                    EditorGUILayout.Space(8);
                    switch (_page)
                    {
                        case Page.Settings: DrawSettings(); break;
                        case Page.Glossary: DrawGlossary(); break;
                        case Page.Catalog: DrawCatalog(); break;
                        case Page.Commands: DrawCommands(); break;
                        case Page.Diagnostics: DrawDiagnostics(); break;
                    }
                }
            }
            finally
            {
                TextPipeline.Suppress = previousSuppress;
                HookHost.SuppressCapture = previousCapture;
            }
        }

        void DrawSettings()
        {
            EditorGUILayout.LabelField("界面翻译", EditorStyles.boldLabel);
            var enabled = EditorGUILayout.ToggleLeft("启用显示层翻译", TranslatorPreferences.RuntimeEnabled);
            if (enabled != TranslatorPreferences.RuntimeEnabled)
                EditorApplication.delayCall += () => Bootstrap.SetEnabled(enabled);

            var original = EditorGUILayout.ToggleLeft("显示原文", TranslatorPreferences.ShowOriginal);
            if (original != TranslatorPreferences.ShowOriginal)
                EditorApplication.delayCall += () => Bootstrap.SetShowOriginal(original);

            var expanded = EditorGUILayout.Foldout(_showPackageSettings, "按插件控制界面翻译", true);
            if (expanded && !_showPackageSettings) RefreshPackages();
            _showPackageSettings = expanded;
            if (expanded) DrawPackages();

            _showNativeAliases = EditorGUILayout.Foldout(_showNativeAliases, "Unity 原生显示别名", true);
            if (_showNativeAliases) DrawNativeAliases();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("在线翻译", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("开启后，未命中本地词库的界面文字会发送给当前翻译服务。关闭后可继续使用离线词库和已保存的译文。", MessageType.Info);
            var online = EditorGUILayout.ToggleLeft("允许发送到在线翻译服务", TranslationApi.Enabled);
            if (online != TranslationApi.Enabled) TranslationApi.Enabled = online;

            var unsavedConfiguration = DrawBackendSettings();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_testing || unsavedConfiguration))
                    if (GUILayout.Button(_testing ? "正在测试…" : "测试连接")) TestConnection();

                using (new EditorGUI.DisabledScope(!TranslationApi.Enabled || TranslationApi.FailedCount == 0))
                    if (GUILayout.Button("重试失败项")) TranslationApi.RetryFailures();

                if (GUILayout.Button("清空机器译文缓存") &&
                    EditorUtility.DisplayDialog("清空缓存", "清空当前机器译文缓存？人工词库将保留。", "清空", "取消"))
                    EditorApplication.delayCall += TranslationApi.ClearCache;
            }

            if (!string.IsNullOrEmpty(_testResult)) EditorGUILayout.HelpBox(_testResult, MessageType.Info);
            var error = TranslationApi.LastError;
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            EditorGUILayout.LabelField($"缓存 {TranslationApi.CachedCount}    等待 {TranslationApi.QueuedCount}    失败 {TranslationApi.FailedCount}");
        }

        static void DrawNativeAliases()
        {
            DrawNativeAliasToggle(NativeAliasSurface.Hierarchy, "Hierarchy 层级对象别名");
            DrawNativeAliasToggle(NativeAliasSurface.Project, "Project 资源和文件夹别名");
            DrawNativeAliasToggle(NativeAliasSurface.BlendShape, "Inspector BlendShape 别名");
            EditorGUILayout.LabelField(NativeAliasHooks.Status, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("打开中文定位…")) NativeAliasWindow.Open();
        }

        static void DrawNativeAliasToggle(NativeAliasSurface surface, string label)
        {
            var wasEnabled = NativeAliases.IsPreferenceEnabled(surface);
            var enabled = EditorGUILayout.ToggleLeft(label, wasEnabled);
            if (enabled == wasEnabled) return;
            NativeAliases.SetEnabled(surface, enabled);
            EditorApplication.delayCall -= NativeAliasHooks.Refresh;
            EditorApplication.delayCall += NativeAliasHooks.Refresh;
        }

        bool DrawBackendSettings()
        {
            using (new EditorGUI.DisabledScope(_testing))
            {
                var current = TranslationApi.CurrentBackend;
                var backend = (TranslationApi.Backend)EditorGUILayout.Popup("翻译源", (int)current, BackendLabels);
                if (backend != current)
                {
                    TranslationApi.CurrentBackend = backend;
                    _testResult = null;
                }

                if (backend != _editedBackend)
                {
                    _editedBackend = backend;
                    _apiKey = TranslationApi.ApiKey;
                    _apiSecret = TranslationApi.ApiSecret;
                    _apiEndpoint = TranslationApi.ApiEndpoint;
                    _apiModel = TranslationApi.ApiModel;
                    _configurationDirty = false;
                }

                EditorGUI.BeginChangeCheck();
                switch (backend)
                {
                    case TranslationApi.Backend.Deepl:
                        _apiKey = EditorGUILayout.PasswordField("DeepL Auth Key", _apiKey);
                        break;
                    case TranslationApi.Backend.Baidu:
                        _apiKey = EditorGUILayout.PasswordField("百度 App ID", _apiKey);
                        _apiSecret = EditorGUILayout.PasswordField("百度 App Secret", _apiSecret);
                        break;
                    case TranslationApi.Backend.OpenAICompat:
                        _apiEndpoint = EditorGUILayout.TextField("完整接口地址", _apiEndpoint);
                        _apiModel = EditorGUILayout.TextField("模型", _apiModel);
                        _apiKey = EditorGUILayout.PasswordField("API Key", _apiKey);
                        break;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    _configurationDirty = true;
                    _testResult = null;
                }
                if (_configurationDirty && GUILayout.Button("保存翻译源配置"))
                {
                    TranslationApi.SaveConfiguration(_apiKey, _apiSecret, _apiEndpoint, _apiModel);
                    _configurationDirty = false;
                }
                if (_configurationDirty)
                    EditorGUILayout.LabelField("保存后可测试新配置。", EditorStyles.wordWrappedMiniLabel);
                return _configurationDirty;
            }
        }

        async void TestConnection()
        {
            _testing = true;
            _testResult = "使用固定示例文本测试当前翻译服务…";
            try
            {
                _testResult = await TranslationApi.TestConnectionAsync();
            }
            catch (Exception)
            {
                _testResult = "测试未完成，请检查设置后重试。";
            }
            finally
            {
                _testing = false;
                if (this != null) Repaint();
            }
        }

        void RefreshPackages()
        {
            _packages = PackageInfo.GetAllRegisteredPackages()
                .Where(pkg => pkg.source != PackageSource.BuiltIn && pkg.name != "com.yg.unity-text-translator")
                .OrderBy(PackageLabel, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _assetScopeIds = DisplayScope.ObservedAssetIds();
        }

        static string PackageLabel(PackageInfo package)
        {
            return string.IsNullOrEmpty(package.displayName) ? package.name : package.displayName;
        }

        void DrawPackages()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _packageSearch = EditorGUILayout.TextField("筛选包名或 ID", _packageSearch);
                if (GUILayout.Button("刷新", GUILayout.Width(54))) RefreshPackages();
            }

            foreach (var package in _packages)
                DrawPackageToggle(PackageLabel(package), package.name);
            foreach (var scopeId in _assetScopeIds)
                DrawPackageToggle("Assets · " + scopeId.Substring("assets:".Length), scopeId);
        }

        void DrawPackageToggle(string label, string packageId)
        {
            if (!Matches(label, _packageSearch) && !Matches(packageId, _packageSearch)) return;
            var wasEnabled = TranslatorPreferences.IsPackageEnabled(packageId);
            var enabled = EditorGUILayout.ToggleLeft(new GUIContent(label, packageId), wasEnabled);
            if (enabled == wasEnabled) return;
            TranslatorPreferences.SetPackageEnabled(packageId, enabled);
            RefreshAfterDrawing();
        }

        void DrawDiagnostics()
        {
            EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
            var summary =
                $"团子冲击！ {_version ?? "未知版本"} / Unity {Application.unityVersion}\n" +
                $"显示层 {(HookHost.Enabled ? "运行中" : "停用")} · IMGUI {HookHost.PatchedCount} 个入口 · UI Toolkit {(UiTextHook.Patched ? "已挂载" : "未挂载")}\n" +
                $"Harmony {HarmonyLoader.Source ?? "未加载"} · 中文字体 {FontFixer.ResolvedFontName ?? "未找到"}\n" +
                $"内置词条 {TextPipeline.EntryCount} · 命中 {TextPipeline.HitCount} · 未命中 {TextPipeline.MissCount} · 拦截异常 {HookHost.TotalErrors}\n" +
                $"在线服务 {TranslationApi.CurrentBackend} · 缓存 {TranslationApi.CachedCount} · 等待 {TranslationApi.QueuedCount} · 失败 {TranslationApi.FailedCount}";
            EditorGUILayout.HelpBox(summary, MessageType.Info);
            if (!string.IsNullOrEmpty(TextPipeline.LoadError))
                EditorGUILayout.HelpBox(TextPipeline.LoadError, MessageType.Warning);
            if (!string.IsNullOrEmpty(TranslationApi.LastError))
                EditorGUILayout.HelpBox(TranslationApi.LastError, MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制诊断摘要")) EditorGUIUtility.systemCopyBuffer = summary;
                if (GUILayout.Button("打开数据目录"))
                {
                    System.IO.Directory.CreateDirectory(CatalogPatcher.DataDir);
                    EditorUtility.RevealInFinder(CatalogPatcher.DataDir);
                }
            }
        }

        static void RefreshAfterDrawing()
        {
            EditorApplication.delayCall -= Bootstrap.Refresh;
            EditorApplication.delayCall += Bootstrap.Refresh;
        }

        static bool Matches(string value, string query)
        {
            return string.IsNullOrWhiteSpace(query) ||
                (!string.IsNullOrEmpty(value) && value.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
