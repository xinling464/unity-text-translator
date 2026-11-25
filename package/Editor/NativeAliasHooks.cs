using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace YGTextTranslator
{
    internal static class NativeAliasHooks
    {
        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Assembly EditorAssembly = typeof(EditorWindow).Assembly;
        static readonly Type HierarchyGui = EditorAssembly.GetType("UnityEditor.GameObjectTreeViewGUI");
        static readonly Type HierarchyItem = EditorAssembly.GetType("UnityEditor.GameObjectTreeViewItem");
        static readonly PropertyInfo SceneHeader = HierarchyItem?.GetProperty("isSceneHeader", InstanceMembers);
        static readonly Type TreeGui = EditorAssembly.GetType("UnityEditor.IMGUI.Controls.TreeViewGUI");
        static readonly Type TreeController = EditorAssembly.GetType("UnityEditor.IMGUI.Controls.TreeViewController");
        static readonly FieldInfo TreeGuiController = TreeGui?.GetField("m_TreeView", InstanceMembers);
        static readonly FieldInfo TreeControllerView = TreeController?.GetField("m_GUIView", InstanceMembers);
        static readonly MethodInfo ContentIndent = Find(TreeGui, "GetContentIndent", typeof(TreeViewItem));
        static readonly PropertyInfo ExtraTreeIndent = TreeGui?.GetProperty("extraSpaceBeforeIconAndLabel", InstanceMembers);
        static readonly Type HostView = EditorAssembly.GetType("UnityEditor.HostView");
        static readonly PropertyInfo ActualView = HostView?.GetProperty("actualView", InstanceMembers);
        static readonly Type ProjectBrowser = EditorAssembly.GetType("UnityEditor.ProjectBrowser");
        static readonly Type AssetsTreeGui = EditorAssembly.GetType("UnityEditor.AssetsTreeViewGUI");
        static readonly Type OneColumnTreeGui = EditorAssembly.GetType("UnityEditor.ProjectBrowserColumnOneTreeViewGUI");

        static readonly Type ObjectList = EditorAssembly.GetType("UnityEditor.ObjectListArea");
        static readonly Type LocalGroup = ObjectList?.GetNestedType("LocalGroup", BindingFlags.NonPublic);
        static readonly Type ListGroup = ObjectList?.GetNestedType("Group", BindingFlags.NonPublic);
        static readonly FieldInfo ListWindow = ObjectList?.GetField("m_Owner", InstanceMembers);
        static readonly FieldInfo GroupList = ListGroup?.GetField("m_Owner", InstanceMembers);
        static readonly PropertyInfo ListMode = ListGroup?.GetProperty("ListMode", InstanceMembers);
        static readonly MethodInfo ListRenameOverlay = Find(ObjectList, "GetRenameOverlay");
        static readonly Type RenameOverlay = EditorAssembly.GetType("UnityEditor.RenameOverlay");
        static readonly MethodInfo IsRenaming = Find(RenameOverlay, "IsRenaming");
        static readonly Type FilterResult = EditorAssembly.GetType("UnityEditor.FilteredHierarchy+FilterResult");
        static readonly FieldInfo FilterInstanceId = FilterResult?.GetField("instanceID", InstanceMembers);
        static readonly FieldInfo FilterGuid = FilterResult?.GetField("m_Guid", InstanceMembers);
        static readonly FieldInfo FilterName = FilterResult?.GetField("name", InstanceMembers);
        static readonly Type BuiltinResource = EditorAssembly.GetType("UnityEditor.BuiltinResource");

        static readonly Type AssetReference = EditorAssembly.GetType("UnityEditorInternal.InternalEditorUtility+AssetReference");
        static readonly FieldInfo ReferenceInstanceId = AssetReference?.GetField("instanceID", InstanceMembers);
        static readonly FieldInfo ReferenceGuid = AssetReference?.GetField("guid", InstanceMembers);
        static readonly FieldInfo GridLabelStyle = ObjectList?.GetNestedType("Styles", BindingFlags.NonPublic)
            ?.GetField("resultsGridLabel", StaticMembers);
        static readonly MethodInfo CharactersThatFit = typeof(GUIStyle).GetMethod("GetNumCharactersThatFitWithinWidth",
            InstanceMembers, null, new[] { typeof(string), typeof(float) }, null);

        static readonly List<(MethodInfo Target, MethodInfo Patch)> Installed =
            new List<(MethodInfo Target, MethodInfo Patch)>();
        static readonly HashSet<NativeAliasSurface> ReportedFailures = new HashSet<NativeAliasSurface>();

        static bool _active;
        static bool _waitingForGui;
        [ThreadStatic] static UnityObject[] _blendShapeTargets;

        internal static string Status { get; private set; } = "原生名称显示补丁已暂停。";

        internal static void Enable()
        {
            if (_active || _waitingForGui) return;
            if (Application.isBatchMode)
            {
                Status = "批处理模式未挂载原生名称显示补丁。";
                return;
            }

            _waitingForGui = true;
            Status = "原生名称显示补丁等待界面首次重绘。";
            SceneView.duringSceneGui += OnSceneGui;
            Editor.finishedDefaultHeaderGUI += OnInspectorGui;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGui;
            EditorApplication.projectWindowItemOnGUI += OnProjectGui;
        }

        static void OnSceneGui(SceneView view) => TryInstallDuringGui();
        static void OnInspectorGui(Editor inspector) => TryInstallDuringGui();
        static void OnHierarchyGui(int instanceId, Rect rect) => TryInstallDuringGui();
        static void OnProjectGui(string guid, Rect rect) => TryInstallDuringGui();

        static void TryInstallDuringGui()
        {
            if (!_waitingForGui || !HookHost.Enabled || !IsRepaint) return;
            try
            {
                if (GUI.skin == null) return;
            }
            catch (ArgumentException)
            {
                return;
            }

            // Mono detours can initialize Unity's native GUI styles while compiling the replacement.
            // Mount them only while a real editor OnGUI callback has a current skin.
            StopWaitingForGui();
            Install();
            EditorApplication.delayCall += Refresh;
        }

        static void StopWaitingForGui()
        {
            if (!_waitingForGui) return;
            _waitingForGui = false;
            SceneView.duringSceneGui -= OnSceneGui;
            Editor.finishedDefaultHeaderGUI -= OnInspectorGui;
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGui;
            EditorApplication.projectWindowItemOnGUI -= OnProjectGui;
        }

        static void Install()
        {
            var treeParameters = new[] { typeof(Rect), typeof(int), typeof(TreeViewItem), typeof(string),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool) };
            var hierarchy = SceneHeader != null && AddPrefix(Find(HierarchyGui, "OnContentGUI", treeParameters),
                nameof(HierarchyContent));
            var tree = OneColumnTreeGui != null && AddPrefix(Find(TreeGui, "OnContentGUI", treeParameters),
                nameof(ProjectTreeContent));
            var list = GroupList != null && ListWindow != null && FilterInstanceId != null && FilterGuid != null &&
                AddPrefix(Find(LocalGroup, "DrawIconAndLabel", typeof(Rect), FilterResult, typeof(string),
                    typeof(Texture2D), typeof(bool), typeof(bool)), nameof(ProjectListContent));
            var grid = ListWindow != null && ReferenceInstanceId != null && ReferenceGuid != null &&
                GridLabelStyle != null && CharactersThatFit != null &&
                AddPostfix(Find(ObjectList, "GetCroppedLabelText", AssetReference, typeof(string), typeof(float)),
                    nameof(ProjectGridContent));
            if (grid && GroupList != null && ListMode != null && FilterName != null && FilterInstanceId != null &&
                FilterGuid != null && ListRenameOverlay != null && IsRenaming != null)
            {
                AddPostfix(Find(LocalGroup, "DrawItem", typeof(Rect), FilterResult, BuiltinResource, typeof(bool)),
                    nameof(ProjectGridTooltip));
            }

            var blendShapeEditor = EditorAssembly.GetType("UnityEditor.SkinnedMeshRendererEditor");
            var blendScope = AddScope(Find(blendShapeEditor, "OnBlendShapeUI"),
                nameof(EnterBlendShapes), nameof(LeaveBlendShapes));
            var blendProperty = blendScope && AddPrefix(Find(typeof(EditorGUILayout), "Slider", typeof(SerializedProperty),
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(GUIContent), typeof(GUILayoutOption[])),
                nameof(BlendShapePropertySlider));
            var blendValue = blendScope && AddPrefix(Find(typeof(EditorGUILayout), "Slider", typeof(GUIContent),
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(GUILayoutOption[])),
                nameof(BlendShapeValueSlider));
            if (blendScope)
                AddPrefix(Find(typeof(EditorGUILayout), "PropertyField", typeof(SerializedProperty), typeof(GUIContent),
                    typeof(bool), typeof(GUILayoutOption[])), nameof(BlendShapeHeading));

            _active = true;
            Status = "原生显示入口　层级 " + Availability(hierarchy) + " · 项目树 " + Availability(tree) +
                " · 列表 " + Availability(list) + " · 网格 " + Availability(grid) +
                " · 形态键 " + (blendProperty && blendValue ? "可用" : blendProperty || blendValue ? "部分可用" : "不可用");
        }

        internal static void Disable()
        {
            StopWaitingForGui();
            EditorApplication.delayCall -= Refresh;
            _active = false;
            _blendShapeTargets = null;
            for (var i = Installed.Count - 1; i >= 0; i--)
                HarmonyLoader.Unpatch(Installed[i].Target, Installed[i].Patch);
            Installed.Clear();
            Status = "原生名称显示补丁已暂停。";
        }

        internal static void Refresh()
        {
            InternalEditorUtility.RepaintAllViews();
        }

        static string Availability(bool available) => available ? "可用" : "不可用";

        static MethodInfo Find(Type type, string name, params Type[] parameters)
        {
            foreach (var parameter in parameters)
                if (parameter == null) return null;
            return type?.GetMethod(name, InstanceMembers | BindingFlags.Static | BindingFlags.DeclaredOnly,
                null, parameters, null);
        }

        static MethodInfo PatchMethod(string name) => typeof(NativeAliasHooks).GetMethod(name, StaticMembers);

        static bool AddPrefix(MethodInfo target, string patchName)
        {
            if (target == null) return false;
            var patch = PatchMethod(patchName);
            if (!HarmonyLoader.Patch(target, patch)) return false;
            Installed.Add((target, patch));
            return true;
        }

        static bool AddPostfix(MethodInfo target, string patchName)
        {
            if (target == null) return false;
            var patch = PatchMethod(patchName);
            if (!HarmonyLoader.PatchPostfix(target, patch)) return false;
            Installed.Add((target, patch));
            return true;
        }

        static bool AddScope(MethodInfo target, string prefixName, string finalizerName)
        {
            if (target == null) return false;
            var prefix = PatchMethod(prefixName);
            var finalizer = PatchMethod(finalizerName);
            if (!HarmonyLoader.PatchScope(target, prefix, finalizer)) return false;
            Installed.Add((target, prefix));
            Installed.Add((target, finalizer));
            return true;
        }

        static bool CanDraw(NativeAliasSurface surface) =>
            _active && HookHost.Enabled && !TextPipeline.Suppress && NativeAliases.IsEnabled(surface);

        static bool IsRepaint => Event.current?.type == EventType.Repaint;

        static void HierarchyContent(object __instance, Rect __0, TreeViewItem __2, ref string __3, bool __7)
        {
            if (!IsRepaint || __2 == null || !CanDraw(NativeAliasSurface.Hierarchy)) return;
            try
            {
                if (!HierarchyItem.IsInstanceOfType(__2) || !(SceneHeader.GetValue(__2) is false)) return;
                var original = __3;
                __3 = NativeAliases.ResolveHierarchy(__2.id, original);
                if (!__7) DrawTreeTooltip(__instance, __0, __2, original, __3);
            }
            catch (Exception exception) { Report(NativeAliasSurface.Hierarchy, exception); }
        }

        static void ProjectTreeContent(object __instance, Rect __0, TreeViewItem __2, ref string __3, bool __7)
        {
            if (!IsRepaint || __2 == null || !CanDraw(NativeAliasSurface.Project)) return;
            try
            {
                if (!OneColumnTreeGui.IsInstanceOfType(__instance) && !IsProjectFolderTree(__instance)) return;
                var original = __3;
                __3 = NativeAliases.ResolveAsset(__2.id, original);
                if (!__7) DrawTreeTooltip(__instance, __0, __2, original, __3);
            }
            catch (Exception exception) { Report(NativeAliasSurface.Project, exception); }
        }

        static bool IsProjectFolderTree(object treeGui)
        {
            if (AssetsTreeGui?.IsInstanceOfType(treeGui) != true) return false;
            var controller = TreeGuiController?.GetValue(treeGui);
            var view = controller == null ? null : TreeControllerView?.GetValue(controller);
            return HostView?.IsInstanceOfType(view) == true &&
                ProjectBrowser?.IsInstanceOfType(ActualView?.GetValue(view)) == true;
        }

        static bool IsProjectList(object list) =>
            list != null && ProjectBrowser?.IsInstanceOfType(ListWindow?.GetValue(list)) == true;

        static void ProjectListContent(object __instance, Rect __0, object __1, ref string __2)
        {
            if (!IsRepaint || __1 == null || !CanDraw(NativeAliasSurface.Project)) return;
            try
            {
                if (!IsProjectList(GroupList.GetValue(__instance))) return;
                var original = __2;
                __2 = ResolveProjectName(__1, original);
                __0.xMin += 18f;
                DrawOriginalTooltip(__0, original, __2, EditorStyles.label);
            }
            catch (Exception exception) { Report(NativeAliasSurface.Project, exception); }
        }

        static void ProjectGridContent(object __instance, object __0, string __1, float __2, ref string __result)
        {
            if (!IsRepaint || __0 == null || !CanDraw(NativeAliasSurface.Project)) return;
            try
            {
                if (!IsProjectList(__instance)) return;
                var alias = NativeAliases.ResolveAsset((int)ReferenceInstanceId.GetValue(__0),
                    ReferenceGuid.GetValue(__0) as string, __1);
                if (string.IsNullOrEmpty(alias) || string.Equals(alias, __1, StringComparison.Ordinal)) return;

                // Keep Unity's crop cache in its original language so pausing restores the row immediately.
                var style = GridLabelStyle.GetValue(null) as GUIStyle;
                if (style == null) return;
                var fit = (int)CharactersThatFit.Invoke(style, new object[] { alias, __2 });
                if (fit < 2 || fit >= alias.Length)
                {
                    __result = alias;
                    return;
                }

                var length = fit - 1;
                if (char.IsHighSurrogate(alias[length - 1])) length--;
                __result = alias.Substring(0, length) + "…";
            }
            catch (Exception exception) { Report(NativeAliasSurface.Project, exception); }
        }

        static string ResolveProjectName(object filterResult, string original) =>
            NativeAliases.ResolveAsset((int)FilterInstanceId.GetValue(filterResult), FilterGuid.GetValue(filterResult) as string, original);

        static void ProjectGridTooltip(object __instance, Rect __0, object __1)
        {
            if (!IsRepaint || __1 == null || !CanDraw(NativeAliasSurface.Project)) return;
            __0.yMin = Mathf.Max(__0.yMin, __0.yMax - EditorGUIUtility.singleLineHeight - 2f);
            if (!__0.Contains(Event.current.mousePosition)) return;
            try
            {
                var list = GroupList.GetValue(__instance);
                if (ListMode.GetValue(__instance) is true || !IsProjectList(list)) return;

                var rename = ListRenameOverlay.Invoke(list, null);
                if (rename != null && IsRenaming.Invoke(rename, null) is true) return;

                var original = FilterName.GetValue(__1) as string;
                var alias = ResolveProjectName(__1, original);
                DrawOriginalTooltip(__0, original, alias, GridLabelStyle.GetValue(null) as GUIStyle, true);
            }
            catch (Exception exception) { Report(NativeAliasSurface.Project, exception); }
        }

        static void DrawTreeTooltip(object treeGui, Rect rect, TreeViewItem item, string original, string alias)
        {
            if (original == alias || !rect.Contains(Event.current.mousePosition) || ContentIndent == null) return;
            var indent = (float)ContentIndent.Invoke(treeGui, new object[] { item });
            var extra = ExtraTreeIndent?.GetValue(treeGui) is float padding ? padding : 0f;
            rect.xMin += indent + extra + 18f;
            DrawOriginalTooltip(rect, original, alias, EditorStyles.label);
        }

        static void DrawOriginalTooltip(Rect rect, string original, string alias, GUIStyle style, bool centered = false)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(alias) || original == alias ||
                rect.width <= 0 || !rect.Contains(Event.current.mousePosition)) return;
            var width = Mathf.Min(rect.width, (style ?? EditorStyles.label).CalcSize(new GUIContent(alias)).x + 4f);
            if (centered) rect.x += (rect.width - width) / 2f;
            rect.width = width;
            if (rect.Contains(Event.current.mousePosition))
                GUI.Label(rect, new GUIContent(string.Empty, original), GUIStyle.none);
        }

        static void EnterBlendShapes(Editor __instance, out UnityObject[] __state)
        {
            __state = _blendShapeTargets;
            _blendShapeTargets = null;
            if (!CanDraw(NativeAliasSurface.BlendShape)) return;
            try { _blendShapeTargets = __instance.targets; }
            catch (Exception exception) { Report(NativeAliasSurface.BlendShape, exception); }
        }

        static Exception LeaveBlendShapes(Exception __exception, UnityObject[] __state)
        {
            _blendShapeTargets = __state;
            return __exception;
        }

        static void BlendShapePropertySlider(ref GUIContent __5) => BlendShapeLabel(ref __5);
        static void BlendShapeValueSlider(ref GUIContent __0) => BlendShapeLabel(ref __0);

        static void BlendShapeHeading(SerializedProperty __0, ref GUIContent __1)
        {
            if (_blendShapeTargets != null && __0 != null && __0.propertyPath == "m_BlendShapeWeights")
                BlendShapeLabel(ref __1);
        }

        static void BlendShapeLabel(ref GUIContent content)
        {
            if (_blendShapeTargets == null || content == null) return;
            try
            {
                var original = content.text;
                var alias = NativeAliases.ResolveBlendShape(_blendShapeTargets, original);
                if (!string.IsNullOrEmpty(alias) && !string.Equals(alias, original, StringComparison.Ordinal))
                    content = new GUIContent(alias, content.image, string.IsNullOrEmpty(content.tooltip)
                        ? "原文：" + original
                        : content.tooltip + "\n原文：" + original);
            }
            catch (Exception exception) { Report(NativeAliasSurface.BlendShape, exception); }
        }

        static void Report(NativeAliasSurface surface, Exception exception)
        {
            if (ReportedFailures.Add(surface))
                Debug.LogWarning($"[YGTextTranslator] {surface} 名称绘制保持原文：{exception.Message}");
        }
    }
}
