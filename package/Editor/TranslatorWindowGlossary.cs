using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YGTextTranslator
{
    public partial class TranslatorWindow
    {
        static readonly string[] GlossaryTabs = { "待校对", "自定义词条" };
        const int GlossaryPageSize = 35;

        TranslationGlossary.Entry[] _glossaryRows;
        TranslationGlossary.Entry[] _filteredGlossaryRows;
        int _glossaryTab;
        int _glossaryPage;
        string _glossarySearch = "";
        string _glossaryStatus;

        bool _editingTerm;
        TranslationGlossary.Entry _editingEntry;
        string _termPackage = "";
        string _termSource = "";
        string _termTranslation = "";
        bool _ignoreTerm;

        void DrawGlossary()
        {
            EditorGUILayout.HelpBox("待校对会收集界面中没有本地译文的词条。保存的词条立即生效，指定包的词条优先于通用词条。", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("新增词条")) EditTerm(null);
                if (GUILayout.Button("刷新列表")) ReloadGlossary();
                if (GUILayout.Button("导入 JSON")) TransferGlossary(true);
                if (GUILayout.Button("导出 JSON")) TransferGlossary(false);
            }

            if (!string.IsNullOrEmpty(_glossaryStatus))
                EditorGUILayout.LabelField(_glossaryStatus, EditorStyles.wordWrappedMiniLabel);

            if (_editingTerm) DrawTermEditor();

            var tab = GUILayout.Toolbar(_glossaryTab, GlossaryTabs);
            if (_glossaryRows == null || tab != _glossaryTab)
            {
                _glossaryTab = tab;
                ReloadGlossary();
            }

            var query = EditorGUILayout.TextField("搜索原文、译文或包 ID", _glossarySearch);
            if (query != _glossarySearch)
            {
                _glossarySearch = query;
                _filteredGlossaryRows = null;
                _glossaryPage = 0;
            }

            var filtered = _filteredGlossaryRows ?? (_filteredGlossaryRows = _glossaryRows.Where(entry =>
                Matches(entry.source, query) || Matches(entry.translation, query) || Matches(entry.packageId, query)).ToArray());
            if (filtered.Length == 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("没有符合条件的词条。");
                return;
            }

            var pageCount = (filtered.Length + GlossaryPageSize - 1) / GlossaryPageSize;
            _glossaryPage = Mathf.Clamp(_glossaryPage, 0, pageCount - 1);
            if (pageCount > 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_glossaryPage == 0))
                        if (GUILayout.Button("上一页", GUILayout.Width(70))) _glossaryPage--;
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"第 {_glossaryPage + 1} / {pageCount} 页 · {filtered.Length} 条", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(_glossaryPage == pageCount - 1))
                        if (GUILayout.Button("下一页", GUILayout.Width(70))) _glossaryPage++;
                }
            }

            foreach (var entry in filtered.Skip(_glossaryPage * GlossaryPageSize).Take(GlossaryPageSize))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(entry.source, EditorStyles.wordWrappedLabel);
                        var scope = string.IsNullOrEmpty(entry.packageId) ? "通用" : entry.packageId;
                        var translation = entry.ignored ? "保留原文" : entry.translation;
                        var details = string.IsNullOrEmpty(translation) ? scope : scope + " · " + translation;
                        EditorGUILayout.LabelField(details, EditorStyles.wordWrappedMiniLabel);
                    }

                    if (GUILayout.Button("编辑", GUILayout.Width(48))) EditTerm(entry);
                }
            }
        }

        void ReloadGlossary()
        {
            _glossaryRows = _glossaryTab == 0 ? TranslationGlossary.Pending() : TranslationGlossary.Entries();
            _filteredGlossaryRows = null;
            _glossaryPage = 0;
        }

        void EditTerm(TranslationGlossary.Entry entry)
        {
            _editingEntry = _glossaryTab == 0 ? null : entry;
            _termPackage = entry?.packageId ?? "";
            _termSource = entry?.source ?? "";
            _termTranslation = entry?.translation ?? "";
            _ignoreTerm = entry?.ignored == true;
            _editingTerm = true;
            _scroll = Vector2.zero;
            GUI.FocusControl(null);
        }

        void DrawTermEditor()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("编辑词条", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(_editingEntry != null))
                {
                    _termPackage = EditorGUILayout.TextField("包 ID（留空为通用）", _termPackage);
                    EditorGUILayout.LabelField("原文");
                    _termSource = EditorGUILayout.TextArea(_termSource, GUILayout.MinHeight(34));
                }
                _ignoreTerm = EditorGUILayout.ToggleLeft("始终保留该词条的原文", _ignoreTerm);
                using (new EditorGUI.DisabledScope(_ignoreTerm))
                {
                    EditorGUILayout.LabelField("译文");
                    _termTranslation = EditorGUILayout.TextArea(_termTranslation, GUILayout.MinHeight(42));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_termSource) ||
                        (!_ignoreTerm && string.IsNullOrWhiteSpace(_termTranslation))))
                    {
                        if (GUILayout.Button("保存"))
                        {
                            try
                            {
                                TranslationGlossary.Set(_termPackage.Trim(), _termSource, _termTranslation, _ignoreTerm);
                                FinishTermEdit("已保存词条。");
                            }
                            catch (Exception ex) { _glossaryStatus = "保存失败：" + ex.Message; }
                        }
                    }

                    if (GUILayout.Button("取消")) _editingTerm = false;
                    if (_editingEntry != null && GUILayout.Button("删除") &&
                        EditorUtility.DisplayDialog("删除词条", "删除自定义词条后，将按普通规则处理其原文。", "删除", "取消"))
                    {
                        try
                        {
                            TranslationGlossary.Remove(_editingEntry.packageId, _editingEntry.source);
                            FinishTermEdit("已删除词条。");
                        }
                        catch (Exception ex) { _glossaryStatus = "删除失败：" + ex.Message; }
                    }
                }
            }
        }

        void FinishTermEdit(string status)
        {
            _editingTerm = false;
            _glossaryStatus = status;
            ReloadGlossary();
            RefreshAfterDrawing();
        }

        void TransferGlossary(bool import)
        {
            var path = import
                ? EditorUtility.OpenFilePanel("导入自定义词库", "", "json")
                : EditorUtility.SaveFilePanel("导出自定义词库", "", "团子冲击-词库", "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (import)
                {
                    var count = TranslationGlossary.Import(path);
                    FinishTermEdit($"已导入 {count} 条词条。");
                }
                else
                {
                    TranslationGlossary.Export(path);
                    _glossaryStatus = "已导出自定义词库。";
                }
            }
            catch (Exception ex)
            {
                _glossaryStatus = (import ? "导入失败：" : "导出失败：") + ex.Message;
            }
        }
    }
}
