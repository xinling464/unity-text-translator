using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;
using Object = UnityEngine.Object;

namespace YGTextTranslator
{
    public sealed class NativeAliasWindow : EditorWindow
    {
        const int PageSize = 30;
        static readonly string[] Filters = { "全部", "层级对象", "项目资源", "BlendShape" };

        readonly List<LocatorEntry> _results = new List<LocatorEntry>();
        readonly LocatorIndex _index = new LocatorIndex();
        string _search = "";
        string[] _terms = Array.Empty<string>();
        int _filter;
        int _searchedCount;
        int _page;
        double _restartAt = -1;
        Vector2 _scroll;
        bool _focusSearch;

        LocatorEntry _editing;
        Object _editTarget;
        string _draftAlias;
        bool _savingAlias;
        string _notice;
        MessageType _noticeType;

        Mesh _shapePickerMesh;
        Object _shapePickerLocation;
        string[] _shapePickerNames;
        int _shapePickerIndex;

        internal static void Open()
        {
            var window = GetWindow<NativeAliasWindow>("中文定位");
            window._focusSearch = true;
            window.Focus();
        }

        void OnEnable()
        {
            minSize = new Vector2(460, 320);
            EditorApplication.update += UpdateIndex;
            EditorApplication.hierarchyChanged += QueueIndex;
            EditorApplication.projectChanged += QueueIndex;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            NativeAliases.Changed += OnAliasesChanged;
            _restartAt = EditorApplication.timeSinceStartup;
        }

        void OnDisable()
        {
            EditorApplication.update -= UpdateIndex;
            EditorApplication.hierarchyChanged -= QueueIndex;
            EditorApplication.projectChanged -= QueueIndex;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            NativeAliases.Changed -= OnAliasesChanged;
            _index.Clear();
            _results.Clear();
        }

        void QueueIndex()
        {
            _restartAt = EditorApplication.timeSinceStartup + 0.25;
        }

        void OnPrefabStageChanged(PrefabStage _) => QueueIndex();

        void OnAliasesChanged()
        {
            if (!_savingAlias) QueueIndex();
        }

        void UpdateIndex()
        {
            if (_restartAt >= 0 && EditorApplication.timeSinceStartup >= _restartAt)
            {
                _restartAt = -1;
                _index.Begin();
                ResetFilter();
                Repaint();
            }

            if (!_index.Building && _searchedCount >= _index.Entries.Count) return;
            var budget = Stopwatch.StartNew();
            var previousEntryCount = _index.Entries.Count;
            var wasBuilding = _index.Building;
            var hadUnsearchedEntries = _searchedCount < previousEntryCount;
            _index.Grow(budget);
            var entries = _index.Entries;
            for (var i = 0; i < 350 && _searchedCount < entries.Count; i++)
            {
                var entry = entries[_searchedCount++];
                if (Matches(entry)) _results.Add(entry);
                if (budget.ElapsedMilliseconds >= 4) break;
            }

            if (wasBuilding || hadUnsearchedEntries || previousEntryCount != entries.Count) Repaint();
        }

        void ResetFilter()
        {
            _results.Clear();
            _terms = _search.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            _searchedCount = 0;
            _page = 0;
            _scroll = Vector2.zero;
        }

        bool Matches(LocatorEntry entry)
        {
            if (_filter != 0 && entry.Surface != FilterSurface(_filter)) return false;

            foreach (var term in _terms)
                if (!Contains(entry.Original, term) && !Contains(entry.Alias, term) && !Contains(entry.Location, term))
                    return false;
            return true;
        }

        static bool Contains(string text, string query)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static NativeAliasSurface FilterSurface(int filter)
        {
            switch (filter)
            {
                case 1: return NativeAliasSurface.Hierarchy;
                case 2: return NativeAliasSurface.Project;
                default: return NativeAliasSurface.BlendShape;
            }
        }

        void OnGUI()
        {
            var previousSuppress = TextPipeline.Suppress;
            TextPipeline.Suppress = true;
            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.SetNextControlName("YGNativeAliasSearch");
                    var text = EditorGUILayout.TextField("搜索中文、原名或路径", _search);
                    var filter = EditorGUILayout.Popup(_filter, Filters, GUILayout.Width(86));
                    if (text != _search || filter != _filter)
                    {
                        _search = text;
                        _filter = filter;
                        ResetFilter();
                    }
                    if (GUILayout.Button("刷新", GUILayout.Width(52))) QueueIndex();
                }
                if (_focusSearch && Event.current.type == EventType.Repaint)
                {
                    EditorGUI.FocusTextInControl("YGNativeAliasSearch");
                    _focusSearch = false;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("编辑当前选中的对象或资源")) EditCurrentSelection();
                    if (GUILayout.Button("编辑当前所选网格的 BlendShape")) PickCurrentBlendShape();
                }

                if (!string.IsNullOrEmpty(_notice)) EditorGUILayout.HelpBox(_notice, _noticeType);
                if (_shapePickerNames != null) DrawShapePicker();
                if (_editing != null) DrawEditor();

                using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
                {
                    _scroll = scroll.scrollPosition;
                    DrawResults();
                }
            }
            finally { TextPipeline.Suppress = previousSuppress; }
        }

        void DrawResults()
        {
            var busy = _restartAt >= 0 || _index.Building || _searchedCount < _index.Entries.Count;
            EditorGUILayout.LabelField(busy ? $"正在索引或搜索 · 已找到 {_results.Count} 项" : $"找到 {_results.Count} 项");
            if (_results.Count == 0)
            {
                if (!busy) EditorGUILayout.LabelField("没有符合条件的对象、资源或 BlendShape。");
                return;
            }

            var pages = (_results.Count - 1) / PageSize + 1;
            _page = Mathf.Clamp(_page, 0, pages - 1);
            if (pages > 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_page == 0))
                        if (GUILayout.Button("上一页", GUILayout.Width(62))) _page--;
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"第 {_page + 1} / {pages} 页", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(_page == pages - 1))
                        if (GUILayout.Button("下一页", GUILayout.Width(62))) _page++;
                }
            }

            var first = _page * PageSize;
            var end = Math.Min(first + PageSize, _results.Count);
            for (var i = first; i < end; i++)
            {
                var entry = _results[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(entry.Title, EditorStyles.wordWrappedLabel);
                        GUILayout.Label(entry.Location, EditorStyles.wordWrappedMiniLabel);
                    }
                    if (GUILayout.Button("定位", GUILayout.Width(50))) Ping(entry);
                    if (GUILayout.Button("编辑别名", GUILayout.Width(66))) Edit(entry);
                }
            }
        }

        void Ping(LocatorEntry entry)
        {
            var target = entry.Locate();
            if (target == null)
            {
                Notify("目标已移动或删除，请刷新索引。", MessageType.Warning);
                return;
            }

            _notice = null;
            EditorApplication.delayCall += () =>
            {
                if (target == null) return;
                if (EditorUtility.IsPersistent(target)) EditorUtility.FocusProjectWindow();
                Selection.activeObject = target;
                EditorGUIUtility.PingObject(target);
            };
        }

        void EditCurrentSelection()
        {
            var selection = Selection.activeObject;
            if (selection != null && EditorUtility.IsPersistent(selection))
            {
                var entry = LocatorIndex.AssetSelection(selection);
                if (entry != null)
                {
                    Edit(entry);
                    return;
                }
            }

            var gameObject = Selection.activeGameObject;
            if (gameObject != null && !EditorUtility.IsPersistent(gameObject))
            {
                Edit(LocatorIndex.Hierarchy(gameObject, LocatorIndex.HierarchyPath(gameObject.transform)));
                return;
            }

            Notify("请先在 Hierarchy 或 Project 选择对象或资源。", MessageType.Info);
        }

        void PickCurrentBlendShape()
        {
            var selection = Selection.activeObject;
            var renderer = selection as SkinnedMeshRenderer;
            if (renderer == null && Selection.activeGameObject != null)
                renderer = Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();

            var mesh = selection as Mesh;
            if (mesh == null && renderer != null) mesh = renderer.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0)
            {
                Notify("请选择带 BlendShape 的网格资源或带 SkinnedMeshRenderer 的对象。", MessageType.Info);
                return;
            }

            _shapePickerMesh = mesh;
            _shapePickerLocation = renderer != null ? (Object)renderer.gameObject : mesh;
            _shapePickerNames = new string[mesh.blendShapeCount];
            for (var i = 0; i < _shapePickerNames.Length; i++)
                _shapePickerNames[i] = mesh.GetBlendShapeName(i);
            _shapePickerIndex = 0;
            _editing = null;
            _notice = null;
        }

        void DrawShapePicker()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                _shapePickerIndex = EditorGUILayout.Popup("BlendShape 原名", _shapePickerIndex, _shapePickerNames);
                if (GUILayout.Button("编辑", GUILayout.Width(52)))
                {
                    var name = _shapePickerNames[_shapePickerIndex];
                    var target = _shapePickerLocation;
                    var location = target is GameObject gameObject
                        ? LocatorIndex.HierarchyPath(gameObject.transform)
                        : AssetDatabase.GetAssetPath(_shapePickerMesh);
                    Edit(LocatorIndex.Shape(_shapePickerMesh, target, name, location));
                }
                if (GUILayout.Button("取消", GUILayout.Width(52))) _shapePickerNames = null;
            }
        }

        void Edit(LocatorEntry entry)
        {
            var target = entry.AliasTarget();
            if (target == null)
            {
                Notify("目标已移动或删除，请刷新索引。", MessageType.Warning);
                return;
            }

            _editing = entry;
            _editTarget = target;
            switch (entry.Surface)
            {
                case NativeAliasSurface.Hierarchy: _draftAlias = NativeAliases.GetHierarchyAlias((GameObject)target); break;
                case NativeAliasSurface.Project: _draftAlias = NativeAliases.GetAssetAlias(target); break;
                default: _draftAlias = NativeAliases.GetBlendShapeAlias((Mesh)target, entry.Original); break;
            }
            _draftAlias = _draftAlias ?? "";
            _shapePickerNames = null;
            _notice = null;
            GUI.FocusControl(null);
        }

        void DrawEditor()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("原名  " + _editing.Original, EditorStyles.wordWrappedLabel);
                GUILayout.Label(_editing.Location, EditorStyles.wordWrappedMiniLabel);
                var alias = string.IsNullOrEmpty(_editing.Alias) || _editing.Alias == _editing.Original
                    ? "沿用原名" : _editing.Alias;
                GUILayout.Label("中文显示预览  " + alias,
                    EditorStyles.wordWrappedLabel);
                _draftAlias = EditorGUILayout.TextField("自定义中文别名", _draftAlias);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_draftAlias)))
                        if (GUILayout.Button("保存别名")) SaveAlias(_draftAlias.Trim());
                    if (GUILayout.Button("清除自定义别名")) SaveAlias("");
                    if (GUILayout.Button("取消")) _editing = null;
                }
            }
        }

        void SaveAlias(string alias)
        {
            _savingAlias = true;
            try
            {
                bool saved;
                switch (_editing.Surface)
                {
                    case NativeAliasSurface.Hierarchy: saved = NativeAliases.SetHierarchy(_editTarget as GameObject, alias); break;
                    case NativeAliasSurface.Project: saved = NativeAliases.SetAsset(_editTarget, alias); break;
                    default:
                        var mesh = _editTarget as Mesh;
                        if (mesh == null || mesh.GetBlendShapeIndex(_editing.Original) < 0)
                        {
                            Notify("这个 BlendShape 已变更，请刷新索引再试。", MessageType.Warning);
                            return;
                        }
                        saved = NativeAliases.SetBlendShape(mesh, _editing.Original, alias);
                        break;
                }
                if (!saved)
                {
                    var detail = _editing.Surface == NativeAliasSurface.Hierarchy
                        ? "请先保存场景，再为该对象设置别名。"
                        : "此目标没有可保存的资源标识。请选择已导入项目的资源。";
                    Notify(detail, MessageType.Warning);
                    return;
                }

                _index.RefreshAlias(_editing);
                _editing = null;
                ResetFilter();
                NativeAliasHooks.Refresh();
                Notify(alias.Length == 0 ? "已清除自定义别名。" : "已保存中文显示别名。", MessageType.Info);
            }
            catch (Exception ex)
            {
                Notify("保存别名失败：" + ex.Message, MessageType.Error);
            }
            finally { _savingAlias = false; }
        }

        void Notify(string message, MessageType type)
        {
            _notice = message;
            _noticeType = type;
        }

        sealed class LocatorEntry
        {
            internal NativeAliasSurface Surface;
            internal string Original;
            internal string Alias;
            internal string Location;
            internal string Guid;
            internal Object Target;
            internal Mesh Mesh;

            internal string Title => string.IsNullOrEmpty(Alias) || Alias == Original ? Original : Alias + "  (" + Original + ")";

            internal Object Locate()
            {
                if (Surface != NativeAliasSurface.Project || string.IsNullOrEmpty(Guid)) return Target;
                var path = AssetDatabase.GUIDToAssetPath(Guid);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
            }

            internal Object AliasTarget() => Surface == NativeAliasSurface.BlendShape ? Mesh : Locate();
        }

        sealed class LocatorIndex
        {
            readonly struct SceneNode
            {
                internal readonly Transform Transform;
                internal readonly string ParentPath;

                internal SceneNode(Transform transform, string parentPath)
                {
                    Transform = transform;
                    ParentPath = parentPath;
                }
            }

            readonly struct ShapeSource
            {
                internal readonly Mesh Mesh;
                internal readonly GameObject Target;
                internal readonly string Location;

                internal ShapeSource(Mesh mesh, GameObject target, string location)
                {
                    Mesh = mesh;
                    Target = target;
                    Location = location;
                }
            }

            internal readonly List<LocatorEntry> Entries = new List<LocatorEntry>();
            readonly Stack<SceneNode> _sceneNodes = new Stack<SceneNode>();
            readonly Queue<ShapeSource> _shapeSources = new Queue<ShapeSource>();
            readonly List<SkinnedMeshRenderer> _renderers = new List<SkinnedMeshRenderer>();
            string[] _assets = Array.Empty<string>();
            int _assetIndex;
            int _shapeIndex;
            bool _nextAsset;

            internal bool Building => _assetIndex < _assets.Length || _sceneNodes.Count > 0 || _shapeSources.Count > 0;

            internal void Begin()
            {
                Clear();
                _assets = AssetDatabase.GetAllAssetPaths();
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded || (prefabStage != null && prefabStage.scene == scene)) continue;
                    AddRoots(scene);
                }
                if (prefabStage != null) AddRoots(prefabStage.scene);
            }

            internal void Clear()
            {
                Entries.Clear();
                _sceneNodes.Clear();
                _shapeSources.Clear();
                _assets = Array.Empty<string>();
                _assetIndex = 0;
                _shapeIndex = 0;
            }

            void AddRoots(Scene scene)
            {
                if (!scene.IsValid() || !scene.isLoaded) return;
                var roots = scene.GetRootGameObjects();
                var sceneName = string.IsNullOrEmpty(scene.name) ? "未保存场景" : scene.name;
                for (var i = roots.Length - 1; i >= 0; i--)
                    _sceneNodes.Push(new SceneNode(roots[i].transform, sceneName));
            }

            internal void Grow(Stopwatch budget)
            {
                for (var i = 0; i < 175 && Building; i++)
                {
                    var hasSceneContent = _sceneNodes.Count != 0 || _shapeSources.Count != 0;
                    if (_assetIndex < _assets.Length && (_nextAsset || !hasSceneContent))
                    {
                        var path = _assets[_assetIndex++];
                        var entry = path.StartsWith("Assets/", StringComparison.Ordinal) ? Asset(path) : null;
                        if (entry != null) Entries.Add(entry);
                    }
                    else if (_shapeSources.Count > 0) AddNextShape();
                    else if (_sceneNodes.Count > 0) AddNextGameObject();

                    _nextAsset = !_nextAsset;
                    if (budget.ElapsedMilliseconds >= 3) return;
                }
            }

            void AddNextGameObject()
            {
                var node = _sceneNodes.Pop();
                if (node.Transform == null) return;
                var target = node.Transform.gameObject;
                var location = node.ParentPath + "/" + target.name;
                if ((target.hideFlags & HideFlags.HideInHierarchy) == 0)
                {
                    Entries.Add(Hierarchy(target, location));
                    target.GetComponents(_renderers);
                    foreach (var renderer in _renderers)
                    {
                        var mesh = renderer.sharedMesh;
                        if (mesh != null && mesh.blendShapeCount > 0)
                            _shapeSources.Enqueue(new ShapeSource(mesh, target, location));
                    }
                }
                for (var i = node.Transform.childCount - 1; i >= 0; i--)
                    _sceneNodes.Push(new SceneNode(node.Transform.GetChild(i), location));
            }

            void AddNextShape()
            {
                var current = _shapeSources.Peek();
                if (current.Mesh == null || current.Target == null || _shapeIndex >= current.Mesh.blendShapeCount)
                {
                    _shapeSources.Dequeue();
                    _shapeIndex = 0;
                    return;
                }
                var name = current.Mesh.GetBlendShapeName(_shapeIndex++);
                Entries.Add(Shape(current.Mesh, current.Target, name, current.Location));
            }

            internal static LocatorEntry Asset(string path)
            {
                if (string.IsNullOrEmpty(path) ||
                    (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)))
                    return null;

                var guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) return null;
                var original = NativeAliases.GetAssetDisplayName(path);
                return new LocatorEntry
                {
                    Surface = NativeAliasSurface.Project,
                    Original = original,
                    Alias = NativeAliases.ResolveAssetPath(guid, original, false),
                    Location = path,
                    Guid = guid
                };
            }

            internal static LocatorEntry AssetSelection(Object target)
            {
                var path = AssetDatabase.GetAssetPath(target);
                if (string.IsNullOrEmpty(path)) return null;
                if (!AssetDatabase.IsSubAsset(target)) return Asset(path);

                var original = target.name;
                return new LocatorEntry
                {
                    Surface = NativeAliasSurface.Project,
                    Original = original,
                    Alias = NativeAliases.ResolveAsset(target.GetInstanceID(), original, false),
                    Location = path + " · " + original,
                    Target = target
                };
            }

            internal void RefreshAlias(LocatorEntry changed)
            {
                foreach (var entry in Entries)
                {
                    if (entry.Surface != changed.Surface) continue;
                    switch (entry.Surface)
                    {
                        case NativeAliasSurface.Hierarchy:
                            if (entry.Target == changed.Target)
                                entry.Alias = NativeAliases.ResolveHierarchy(entry.Target.GetInstanceID(), entry.Original, false);
                            break;
                        case NativeAliasSurface.Project:
                            if (entry.Guid == changed.Guid && entry.Target == changed.Target)
                                entry.Alias = string.IsNullOrEmpty(entry.Guid)
                                    ? NativeAliases.ResolveAsset(entry.Target.GetInstanceID(), entry.Original, false)
                                    : NativeAliases.ResolveAssetPath(entry.Guid, entry.Original, false);
                            break;
                        default:
                            if (entry.Mesh == changed.Mesh && entry.Original == changed.Original)
                                entry.Alias = NativeAliases.ResolveBlendShape(entry.Mesh, entry.Original, false);
                            break;
                    }
                }
            }

            internal static LocatorEntry Hierarchy(GameObject target, string path)
            {
                var original = target.name;
                return new LocatorEntry
                {
                    Surface = NativeAliasSurface.Hierarchy,
                    Original = original,
                    Alias = NativeAliases.ResolveHierarchy(target.GetInstanceID(), original, false),
                    Location = path,
                    Target = target
                };
            }

            internal static LocatorEntry Shape(Mesh mesh, Object target, string original, string path)
            {
                return new LocatorEntry
                {
                    Surface = NativeAliasSurface.BlendShape,
                    Original = original,
                    Alias = NativeAliases.ResolveBlendShape(mesh, original, false),
                    Location = path + " · BlendShape/" + original,
                    Target = target,
                    Mesh = mesh
                };
            }

            internal static string HierarchyPath(Transform target)
            {
                var names = new Stack<string>();
                var scene = target.gameObject.scene.name;
                for (var current = target; current != null; current = current.parent) names.Push(current.name);
                names.Push(string.IsNullOrEmpty(scene) ? "未保存场景" : scene);
                return string.Join("/", names);
            }
        }
    }
}
