using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace YGTextTranslator
{
    public partial class TranslatorWindow
    {
        sealed class MenuCommand
        {
            internal readonly string Label;
            internal readonly string SourcePath;
            internal readonly string RegisteredPath;

            internal MenuCommand(string label, string sourcePath, string registeredPath = null)
            {
                Label = label;
                SourcePath = sourcePath;
                RegisteredPath = registeredPath ?? sourcePath;
            }
        }

        static readonly MenuCommand[] UnityCommands =
        {
            new MenuCommand("打开控制台", "Window/General/Console"),
            new MenuCommand("打开软件包管理器", "Window/Package Manager"),
            new MenuCommand("打开动画窗口", "Window/Animation/Animation"),
            new MenuCommand("打开性能分析器", "Window/Analysis/Profiler"),
            new MenuCommand("创建空物体", "GameObject/Create Empty"),
            new MenuCommand("创建立方体", "GameObject/3D Object/Cube")
        };

        static readonly MenuCommand[] ModularAvatarCommands =
        {
            new MenuCommand("设置服装", "GameObject/Modular Avatar/Setup Outfit"),
            new MenuCommand("显示适配预览", "GameObject/Modular Avatar/Show Fit Preview"),
            new MenuCommand("显示反应调试器", "GameObject/Modular Avatar/Show Reaction Debugger"),
            new MenuCommand("显示 Modular Avatar 信息", "Tools/Modular Avatar/Show Modular Avatar Information")
        };

        List<MenuCommand> _commands;
        string _commandSearch = "";
        string _commandStatus;
        bool _focusCommandSearch;

        void DrawCommands()
        {
            if (_commands == null) LoadCommands();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.SetNextControlName("YGTranslatorCommandSearch");
                _commandSearch = EditorGUILayout.TextField("搜索中文或原始菜单路径", _commandSearch);
                if (GUILayout.Button("刷新", GUILayout.Width(54))) LoadCommands();
            }
            if (_focusCommandSearch && Event.current.type == EventType.Repaint)
            {
                EditorGUI.FocusTextInControl("YGTranslatorCommandSearch");
                _focusCommandSearch = false;
            }
            if (!string.IsNullOrEmpty(_commandStatus)) EditorGUILayout.HelpBox(_commandStatus, MessageType.Warning);

            var terms = _commandSearch.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var found = false;
            foreach (var command in _commands)
            {
                if (!terms.All(term => Matches(command.Label, term) || Matches(command.SourcePath, term) ||
                    Matches(command.RegisteredPath, term))) continue;
                found = true;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(command.Label, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(command.SourcePath, EditorStyles.wordWrappedMiniLabel);
                        if (command.RegisteredPath != command.SourcePath)
                            EditorGUILayout.LabelField(command.RegisteredPath, EditorStyles.wordWrappedMiniLabel);
                    }
                    if (GUILayout.Button("复制路径", GUILayout.Width(72))) EditorGUIUtility.systemCopyBuffer = command.RegisteredPath;
                    if (GUILayout.Button("运行", GUILayout.Width(50))) ExecuteCommand(command);
                }
            }

            if (!found) EditorGUILayout.LabelField("没有匹配的命令。");
        }

        void LoadCommands()
        {
            _commands = new List<MenuCommand>(UnityCommands);
            var installed = PackageInfo.GetAllRegisteredPackages();
            if (installed.Any(pkg => pkg.name == "com.vrchat.base"))
                _commands.Add(new MenuCommand("打开 VRChat SDK 控制面板", "VRChat SDK/Show Control Panel"));
#if YGT_UNITYAGENT_0_17_0
            foreach (var command in UnityAgentMenuAliases.Commands)
                _commands.Add(new MenuCommand(command.Label, command.SourcePath));
#endif
            if (!installed.Any(pkg => pkg.name == "nadena.dev.modular-avatar")) return;

            var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                foreach (MenuItem item in method.GetCustomAttributes(typeof(MenuItem), false))
                    if (!item.validate && item.menuItem != null) registered.Add(item.menuItem);
            }

            foreach (var command in ModularAvatarCommands)
            {
                var path = registered.FirstOrDefault(candidate =>
                    string.Equals(candidate, command.SourcePath, StringComparison.OrdinalIgnoreCase))
                    ?? registered.FirstOrDefault(candidate => IsBilingualPath(candidate, command.SourcePath));
                if (path != null) _commands.Add(new MenuCommand(command.Label, command.SourcePath, path));
            }
        }

        static bool IsBilingualPath(string path, string sourcePath)
        {
            var candidate = path.Split('/');
            var expected = sourcePath.Split('/');
            if (candidate.Length != expected.Length ||
                !string.Equals(candidate[0], expected[0], StringComparison.OrdinalIgnoreCase)) return false;

            for (var i = 1; i < candidate.Length; i++)
            {
                if (string.Equals(candidate[i], expected[i], StringComparison.OrdinalIgnoreCase)) continue;
                if (!candidate[i].EndsWith(" (" + expected[i] + ")", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        void ExecuteCommand(MenuCommand command)
        {
            _commandStatus = null;
            EditorApplication.delayCall += () =>
            {
                bool handled;
                try { handled = EditorApplication.ExecuteMenuItem(command.RegisteredPath); }
                catch (Exception) { handled = false; }

                if (this == null || handled) return;
                _commandStatus = "无法运行该命令。请确认当前 Unity 版本、插件和选中的物体满足菜单要求。";
                Repaint();
            };
        }
    }
}
