using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YGTextTranslator
{
    public partial class TranslatorWindow
    {
        CatalogPatcher.Plan _catalogPlan;
        readonly HashSet<CatalogPatcher.FileChange> _selectedChanges = new HashSet<CatalogPatcher.FileChange>();
        CatalogPatcher.FileChange _shownChange;
        bool _restoring;
        [SerializeField] string _catalogStatus;
        string _beforeChange;
        string _afterChange;

        void DrawCatalog()
        {
            EditorGUILayout.HelpBox("目录改动会写入所选插件的菜单源码或词典文件，并可能触发脚本重编译。先预览，勾选后再应用。", MessageType.Info);
            var packageChanges = PackageScanner.LastNewPackages;
            if (packageChanges.Count > 0)
            {
                var text = string.Join("\n", packageChanges.Take(6));
                if (packageChanges.Count > 6) text += $"\n…另有 {packageChanges.Count - 6} 项";
                EditorGUILayout.HelpBox("检测到包变动\n" + text, MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("预览可汉化内容")) LoadCatalog(false);
                if (GUILayout.Button("预览可还原内容")) LoadCatalog(true);
            }

            if (!string.IsNullOrEmpty(_catalogStatus))
                EditorGUILayout.HelpBox(_catalogStatus, MessageType.Info);

            if (_catalogPlan == null) return;
            if (_catalogPlan.Messages.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", _catalogPlan.Messages), MessageType.Warning);

            if (_catalogPlan.Files.Count == 0)
            {
                EditorGUILayout.LabelField(_restoring ? "没有可还原的文件。" : "没有可应用的汉化改动。");
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选", GUILayout.Width(60))) _selectedChanges.UnionWith(_catalogPlan.Files);
                if (GUILayout.Button("取消全选", GUILayout.Width(76))) _selectedChanges.Clear();
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_selectedChanges.Count == 0))
                {
                    var label = _restoring ? "还原选中文件" : "应用选中文件";
                    if (GUILayout.Button($"{label}（{_selectedChanges.Count}）")) ConfirmCatalog();
                }
            }

            foreach (var group in _catalogPlan.Files.GroupBy(file => file.PackageName))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var allSelected = group.All(_selectedChanges.Contains);
                    var previousMixedValue = EditorGUI.showMixedValue;
                    EditorGUI.showMixedValue = !allSelected && group.Any(_selectedChanges.Contains);
                    bool selectAll;
                    try { selectAll = EditorGUILayout.ToggleLeft(group.Key, allSelected, EditorStyles.boldLabel); }
                    finally { EditorGUI.showMixedValue = previousMixedValue; }

                    if (selectAll != allSelected)
                    {
                        if (selectAll) _selectedChanges.UnionWith(group);
                        else _selectedChanges.ExceptWith(group);
                    }

                    foreach (var file in group)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var selected = _selectedChanges.Contains(file);
                            var next = EditorGUILayout.ToggleLeft(new GUIContent(file.RelativePath, file.Summary), selected);
                            if (selected && !next) _selectedChanges.Remove(file);
                            if (!selected && next) _selectedChanges.Add(file);
                            if (GUILayout.Button(_shownChange == file ? "已展开" : "看差异", GUILayout.Width(62)))
                                SetShownChange(_shownChange == file ? null : file);
                        }

                        if (_shownChange == file) DrawCatalogDiff(file);
                    }
                }
            }
        }

        void LoadCatalog(bool restore)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                _restoring = restore;
                _selectedChanges.Clear();
                SetShownChange(null);
                _catalogStatus = null;
                try { _catalogPlan = restore ? CatalogPatcher.PreviewRestore() : CatalogPatcher.Preview(); }
                catch (Exception ex)
                {
                    _catalogPlan = null;
                    _catalogStatus = "生成预览失败：" + ex.Message;
                }
                Repaint();
            };
        }

        void ConfirmCatalog()
        {
            var restore = _restoring;
            var files = _catalogPlan.Files.Where(_selectedChanges.Contains).ToArray();
            var action = restore ? "还原" : "应用";
            if (!EditorUtility.DisplayDialog(action, $"确认{action}选中的 {files.Length} 个文件？", action, "取消")) return;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    var result = restore ? CatalogPatcher.Restore(files) : CatalogPatcher.Apply(files);
                    _catalogStatus = string.Join("\n", new[] { result.Report }.Concat(result.Messages));
                }
                catch (Exception ex) { _catalogStatus = action + "失败：" + ex.Message; }

                if (this == null) return;
                _catalogPlan = null;
                _selectedChanges.Clear();
                SetShownChange(null);
                Repaint();
            };
        }

        void DrawCatalogDiff(CatalogPatcher.FileChange file)
        {
            EditorGUILayout.LabelField(file.Summary, EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制完整原文")) EditorGUIUtility.systemCopyBuffer = file.OriginalText;
                if (GUILayout.Button("复制完整改动后文本")) EditorGUIUtility.systemCopyBuffer = file.TranslatedText;
            }
            EditorGUILayout.LabelField("改动前", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_beforeChange, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("改动后", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_afterChange, EditorStyles.wordWrappedLabel);
        }

        void SetShownChange(CatalogPatcher.FileChange file)
        {
            _shownChange = file;
            if (file == null) return;

            var before = Lines(file.OriginalText);
            var after = Lines(file.TranslatedText);
            var shared = Math.Min(before.Length, after.Length);
            var first = 0;
            while (first < shared && before[first] == after[first]) first++;
            var last = 0;
            while (first + last < shared && before[before.Length - last - 1] == after[after.Length - last - 1]) last++;

            // Menu name rewrites replace text in place. Catalog additions can insert new lines.
            var aligned = before.Length == after.Length;
            var removed = new List<string>();
            var added = new List<string>();
            for (var i = first; i < before.Length - last; i++)
                if (!aligned || before[i] != after[i]) removed.Add($"{i + 1}: {before[i]}");
            for (var i = first; i < after.Length - last; i++)
                if (!aligned || after[i] != before[i]) added.Add($"{i + 1}: {after[i]}");

            _beforeChange = RenderChangedLines(removed);
            _afterChange = RenderChangedLines(added);
        }

        static string[] Lines(string text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        static string RenderChangedLines(List<string> lines)
        {
            const int maxLines = 24;
            if (lines.Count == 0) return "（无内容）";
            var text = string.Join("\n", lines.Take(maxLines));
            return lines.Count <= maxLines ? text : text + $"\n…其余 {lines.Count - maxLines} 行可复制查看";
        }
    }
}
