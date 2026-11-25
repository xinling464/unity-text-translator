using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace YGTextTranslator
{
    /// <summary>
    /// 插件目录级汉化：对「自带本地化词典」的包补词条，对「菜单写死英文」的包改菜单常量。
    /// 预览并应用受支持的内嵌包文件改动，使用带校验信息的 .ygzh.bak 还原。
    /// </summary>
    internal static class CatalogPatcher
    {
        internal const string BackupSuffix = ".ygzh.bak";

        // ---- MA 词典词表（en-US 有、zh-Hans 缺的那些） ----
        static readonly Dictionary<string, string> MaLexicon = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "params.representation.Unsigned", "无符号" },
            { "params.representation.Signed", "有符号" },
            { "params.bitWidth", "{0} 位" },
            { "merge_parameter.ui.bitWidth.tooltip", "决定此参数可以表示多大的数值范围（位宽）" },
            { "merge_parameter.ui.representation.tooltip", "“有符号”参数可以表示负数值" },
            { "merge_parameter.ui.useCustomLerpDuration", "插值时长" },
            { "merge_armature.match_scale_adjusters", "匹配基础 Avatar 的 Scale Adjusters 设置" },
            { "merge_armature.match_scale_adjusters.tooltip", "当你使用 Scale Adjusters 调整过基础 Avatar 的身材比例后，勾选此项会把服装同步为与之匹配的设置。" },
            { "outfit_root.armature_root", "骨架（Armature）" },
            { "outfit_root.armature_root.tooltip", "这套服装骨架的根骨骼" },
            { "move_to.target", "目标" },
            { "move_to.match_position", "匹配位置" },
            { "move_to.match_rotation", "匹配旋转" },
            { "move_to.match_scale", "匹配缩放比例" },
            { "validation.syncparamsequence.primary_platform_changed", "[MA-0027] Sync Parameter Sequence 的主平台已变更" },
            { "validation.syncparamsequence.primary_platform_changed:description", "主平台当前设置为 {0}，但已记录的主平台构建使用的是 {1}。" },
            { "validation.syncparamsequence.primary_platform_changed:hint", "请检查同一 Avatar 不同变体上的所有 Sync Parameter Sequence 组件是否使用了相同的主平台设置。" },
            { "validation.syncparamsequence.no_primary_record", "[MA-0026] 未找到主平台参数记录" },
            { "validation.syncparamsequence.no_primary_record:description", "此 Avatar 没有保存过主平台构建的记录，但当前构建目标是 {0}、配置的主平台是 {1}。" },
            { "validation.syncparamsequence.no_primary_record:hint", "请先构建主平台变体。" },
            { "validation.legacy_vrcfury_error:hint", "使用 Reactive 组件需要 VRCFury 1.1373.0 或更高版本。请将 VRCFury 更新到最新版本。" },
            { "menuitem.default_override_warning", "此菜单项会把参数 {0} 的默认值从 {1} 改写为 {2}。\n如果不希望这样，请把 Is Default 设置改为与该参数一致，或为此菜单项手动设置零值。" },
            { "reactive_object.reactive_move.to_move", "要移动的对象" },
            { "reactive_object.reactive_move.where_to", "目标" },
            { "reactive_object.reactive_move.fix_to_world", "固定到世界坐标" },
            { "reactive_object.reactive_move.transition_time", "过渡时间" },
            { "reactive_object.reactive_move.sets_position", "设定位置" },
            { "reactive_object.reactive_move.sets_rotation", "设定旋转" },
            { "reactive_object.reactive_move.sets_scale", "设定缩放" },
            { "reactive_object.delete-mesh.uv-channel", "UV 通道" },
            { "reactive_object.delete-mesh-by-uvtile.invert", "反转" },
            { "ro_sim.effect.reactive_move.destination", "目的地：{0}" },
            { "error.parameter_assigner.value_out_of_range", "[MA-0025] 参数“{0}”的自动值过多" },
            { "error.parameter_assigner.value_out_of_range:description", "参数“{0}”的自动取值菜单项数量超过了 VRChat Int 参数的取值范围（0-255）。" },
            { "error.parameter_assigner.value_out_of_range:hint", "请减少使用此参数的自动取值菜单项，或在有效范围内为其指定明确的值。" },
            { "version_mismatch.window_title", "需要更新 Modular Avatar" },
            { "version_mismatch.headline", "你的 Modular Avatar 版本过旧" },
            { "version_mismatch.warning", "若继续操作，你的数据可能会损坏。" },
            { "version_mismatch.instructions", "请将 Modular Avatar 更新到以下版本：" },
            { "version_mismatch.required", "需要：" },
            { "version_mismatch.installed", "已安装：" },
            { "version_mismatch.affected_assets", "以下资源要求此版本：" },
            { "version_mismatch.upgrade_help", "如何升级？" },
            { "version_mismatch.refresh_asset_database", "刷新资源数据库" },
            { "version_mismatch.close_unity", "关闭 Unity" },
            { "version_mismatch.ignore", "忽略" },
            { "version_mismatch.at_your_own_risk", "（风险自负）" },
            { "version_mismatch.export_warning", "你正在从 Modular Avatar 的预发布版本导出包。这在当前稳定版本上可能导致异常行为。请务必在最新稳定版本上测试你的资源。" },
            { "version_reset.window_title", "重置版本信息" },
            { "version_reset.confirmation", "你确定要重置这些资源要求的最低版本吗？" },
            { "version_reset.current_version", "当前版本：" },
            { "version_reset.highest_required_version", "要求的最高版本：" },
            { "version_reset.none", "无" },
            { "version_reset.warning", "强制降级已有的资源可能损坏你保存的数据。重置后请检查行为是否正常。" },
            { "version_reset.set_current", "将要求的版本设为当前版本" },
            { "version_reset.clear_required_version", "清除要求的版本数据" },
            { "version_reset.cancel", "取消" },
            { "error.rename_params.bit_width_conflict", "[MA-0028] 位宽冲突" },
            { "error.rename_params.bit_width_conflict:description", "参数 {0} 指定了多个位宽：{1} != {2}" },
            { "error.rename_params.bit_width_conflict:hint", "为避免不可预期的行为，请只在一个 MA Parameters 组件里填写位宽，其余留空。若存在多个值，Modular Avatar 会按层级顺序采用第一个指定的位宽。" },
            { "error.rename_params.representation_conflict", "[MA-0029] 表示法冲突" },
            { "error.rename_params.representation_conflict:description", "参数 {0} 指定了多个表示法：{1} != {2}" },
            { "error.rename_params.representation_conflict:hint", "为避免不可预期的行为，请只在一个 MA Parameters 组件里填写表示法，其余留空。若存在多个值，Modular Avatar 会按层级顺序采用第一个指定的表示法。" },
            { "error.rename_params.lerp_duration_conflict", "[MA-0030] 插值时长冲突" },
            { "error.rename_params.lerp_duration_conflict:description", "参数 {0} 指定了多个插值时长：{1} != {2}" },
            { "error.rename_params.lerp_duration_conflict:hint", "为避免不可预期的行为，请只在一个 MA Parameters 组件里填写插值时长，其余留空。若存在多个值，Modular Avatar 会按层级顺序采用第一个指定的插值时长。" },
            { "error.rename_collision_tags.ambiguous_parameter", "[MA-0031] 碰撞标签参数名有歧义" },
            { "error.rename_collision_tags.ambiguous_parameter:description", "参数 {0} 中包含两个或更多 `_CollisionTag_`。" },
            { "error.rename_collision_tags.ambiguous_parameter:hint", "请从前缀或标签中移除字面量 `_CollisionTag_`。" },
        };

        // ---- 菜单常量词表：英文路径 -> 中文（英文括注保留，方便对照教程） ----
        static readonly (string en, string zh)[] MenuItems = new[]
        {
            ("Assets/Create/BlendTree", "Assets/Create/混合树 (BlendTree)"),
            ("Assets/Modular Avatar/Reset version information", "Assets/Modular Avatar/重置版本信息 (Reset version information)"),
            ("GameObject/Modular Avatar/Create Toggle", "GameObject/Modular Avatar/创建开关 (Create Toggle)"),
            ("GameObject/Modular Avatar/Create Toggle for Selection", "GameObject/Modular Avatar/为所选对象创建开关 (Create Toggle for Selection)"),
            ("GameObject/Modular Avatar/Dump RC Blend Tree", "GameObject/Modular Avatar/导出 RC 混合树 (Dump RC Blend Tree)"),
            ("GameObject/Modular Avatar/Extract Menu", "GameObject/Modular Avatar/提取菜单 (Extract Menu)"),
            ("GameObject/Modular Avatar/Manual Bake Avatar", "GameObject/Modular Avatar/手动烘焙 Avatar (Manual Bake Avatar)"),
            ("GameObject/Modular Avatar/Reset version information", "GameObject/Modular Avatar/重置版本信息 (Reset version information)"),
            ("GameObject/Modular Avatar/Setup Outfit", "GameObject/Modular Avatar/设置服装 (Setup Outfit)"),
            ("GameObject/Modular Avatar/Show Fit Preview", "GameObject/Modular Avatar/预览适配效果 (Show Fit Preview)"),
            ("GameObject/Modular Avatar/Show Modular Avatar Information", "GameObject/Modular Avatar/显示 MA 信息 (Show Modular Avatar Information)"),
            ("GameObject/Modular Avatar/Show Reaction Debugger", "GameObject/Modular Avatar/显示反应调试器 (Show Reaction Debugger)"),
            ("Tools/Modular Avatar/Debug/Log Sync Parameter Sequence", "Tools/Modular Avatar/Debug/记录同步参数序列 (Log Sync Parameter Sequence)"),
            ("Tools/Modular Avatar/Debug/Show Version Mismatch Warning", "Tools/Modular Avatar/Debug/显示版本不匹配警告 (Show Version Mismatch Warning)"),
            ("Tools/Modular Avatar/Debug/Show Version Mismatch Warning (Non-Modal)", "Tools/Modular Avatar/Debug/显示版本不匹配警告（非模态） (Show Version Mismatch Warning (Non-Modal))"),
            ("Tools/Modular Avatar/Manual Bake Avatar", "Tools/Modular Avatar/手动烘焙 Avatar (Manual Bake Avatar)"),
            ("Tools/Modular Avatar/Manual bake avatar", "Tools/Modular Avatar/手动烘焙 Avatar (Manual bake avatar)"),
            ("Tools/Modular Avatar/Reload Localizations", "Tools/Modular Avatar/重新加载本地化 (Reload Localizations)"),
            ("Tools/Modular Avatar/Show Modular Avatar Information", "Tools/Modular Avatar/显示 MA 信息 (Show Modular Avatar Information)"),
            ("Tools/Modular Avatar/Sync Bones in Edit Mode", "Tools/Modular Avatar/编辑模式同步骨骼 (Sync Bones in Edit Mode)"),
            ("Tools/NDM Framework/Debug Tools/Show Preview Scene", "Tools/NDM Framework/Debug Tools/显示预览场景 (Show Preview Scene)")
        };

        const string ModularAvatar = "nadena.dev.modular-avatar";
        const string Ndmf = "nadena.dev.ndmf";
        const string ZhLexicon = "Editor/Localization/zh-Hans.json";
        const string EnLexicon = "Editor/Localization/en-US.json";
        const string MaMenuConstants = "Runtime/UI/UnityMenuItems.cs";
        const string NdmfPreviewMenu = "Editor/PreviewSystem/Rendering/NDMFPreviewSceneManager.cs";
        const string ReceiptSuffix = ".info";

        static readonly Dictionary<string, string> MenuNames = MenuItems.ToDictionary(item => item.en, item => item.zh, StringComparer.Ordinal);
        static readonly Regex MenuAttribute = new Regex(@"(?m)^(?<prefix>[ \t]*\[\s*(?:UnityEditor\.)?MenuItem(?:Attribute)?\s*\(\s*)""(?<menu>[^""\r\n]*)""(?=\s*[,)\]])");
        static readonly Regex MaMenuConstant = new Regex(@"(?m)^(?<prefix>[ \t]*(?:(?:internal|public|private)\s+)?const\s+string\s+(?:GameObject|TopMenu)_\w+\s*=\s*)""(?<menu>[^""\r\n]*)""(?=\s*;)");
        static readonly Regex NdmfMenuCheck = new Regex(@"(?m)^(?<prefix>[ \t]*(?:UnityEditor\.)?Menu\.SetChecked\(\s*)""(?<menu>[^""\r\n]*)""(?=\s*,)");
        static readonly Regex FormatPlaceholder = new Regex(@"(?<!\{)\{\d+(?:,[^}:]+)?(?::[^}]+)?\}(?!\})");

        internal sealed class FileChange
        {
            internal readonly string PackageName;
            internal readonly string RelativePath;
            internal readonly string Summary;
            internal readonly string OriginalText;
            internal readonly string TranslatedText;
            internal readonly string Version;
            internal readonly string Path;
            internal readonly byte[] Original;
            internal readonly byte[] Translated;
            internal readonly bool Restoring;

            internal FileChange(string packageName, string version, string path, string summary,
                byte[] original, byte[] translated, bool restoring = false)
            {
                PackageName = packageName;
                Version = version;
                Path = path;
                RelativePath = path.Substring(ProjectRoot.Length + 1).Replace('\\', '/');
                Summary = summary;
                Original = original;
                Translated = translated;
                OriginalText = ReadText(original, out _);
                TranslatedText = ReadText(translated, out _);
                Restoring = restoring;
            }
        }

        internal sealed class Plan
        {
            internal List<FileChange> Files { get; } = new List<FileChange>();
            internal List<string> Messages { get; } = new List<string>();
        }

        internal sealed class Result
        {
            internal int FilesChanged { get; set; }
            internal List<string> Messages { get; } = new List<string>();
            internal string Report { get; set; }
        }

        [Serializable]
        sealed class PackageManifest
        {
            public string name = "";
            public string version = "";
        }

        internal static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YGUnityTranslator");
        internal static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string PackagesRoot => Path.Combine(ProjectRoot, "Packages");

        internal static Plan Preview()
        {
            var plan = new Plan();
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name != ModularAvatar && package.name != Ndmf) continue;
                if (!TryGetEmbeddedRoot(package, out var root))
                {
                    plan.Messages.Add($"{package.name} {package.version}：不是项目内嵌包，跳过目录改写。");
                    continue;
                }

                try
                {
                    var version = ReadPackageVersion(root, package.name);
                    if (package.name == ModularAvatar)
                    {
                        PreviewFile(plan, package.name, version, root, ZhLexicon, text =>
                        {
                            var english = Path.Combine(root, EnLexicon);
                            CheckPath(english);
                            return AddMissingTranslations(text, File.ReadAllText(english), false);
                        });
                        PreviewFile(plan, package.name, version, root, MaMenuConstants, text =>
                            TranslateMenus(text, MaMenuConstant));
                        foreach (var file in FindFiles(Path.Combine(root, "Editor"), "*.cs", plan.Messages))
                        {
                            var relative = file.Substring(root.Length + 1);
                            PreviewFile(plan, package.name, version, root, relative, text => TranslateMenus(text, MenuAttribute));
                        }
                    }
                    else
                    {
                        PreviewFile(plan, package.name, version, root, NdmfPreviewMenu, text =>
                        {
                            var attributes = TranslateMenus(text, MenuAttribute);
                            if (attributes.count == 0) return attributes;
                            var references = TranslateMenus(attributes.text, NdmfMenuCheck);
                            return (references.text, attributes.count + references.count);
                        });
                    }
                }
                catch (Exception e)
                {
                    plan.Messages.Add($"{package.name}：{e.Message}");
                }
            }
            return plan;
        }

        static void PreviewFile(Plan plan, string package, string version, string root, string relative,
            Func<string, (string text, int count)> translate)
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!File.Exists(path)) return;
            try
            {
                CheckPath(path);
                var backup = path + BackupSuffix;
                CheckPath(backup);
                CheckPath(backup + ReceiptSuffix);
                if (File.Exists(backup))
                {
                    plan.Messages.Add($"{package}/{relative}：已有备份，先打开还原预览。");
                    return;
                }

                var original = File.ReadAllBytes(path);
                var text = ReadText(original, out var encoding);
                var change = translate(text);
                if (change.count > 0)
                    plan.Files.Add(new FileChange(package, version, path, $"{change.count} 处改动",
                        original, EncodeText(change.text, encoding)));
            }
            catch (Exception e)
            {
                plan.Messages.Add($"{package}/{relative}：{e.Message}");
            }
        }

        static (string text, int count) TranslateMenus(string text, Regex matcher)
        {
            int count = 0;
            var translated = matcher.Replace(text, match =>
            {
                var menu = match.Groups["menu"].Value;
                if (!MenuNames.TryGetValue(menu, out var zh)) return match.Value;
                count++;
                return match.Groups["prefix"].Value + "\"" + zh + "\"";
            });
            return (translated, count);
        }

        static (string text, int count) AddMissingTranslations(string chinese, string english, bool legacy)
        {
            var zh = FlatJson.Parse(chinese);
            var en = FlatJson.Parse(english);
            if (zh == null || en == null)
            {
                if (legacy) return (chinese, 0);
                throw new IOException("词典格式已变化，当前只支持平铺字符串词典。");
            }

            var added = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in MaLexicon)
            {
                if (!en.TryGetValue(pair.Key, out var source) || zh.ContainsKey(pair.Key)) continue;
                if (!legacy && !Placeholders(source).SequenceEqual(Placeholders(pair.Value))) continue;
                added.Add(pair.Key, pair.Value);
            }
            if (added.Count == 0) return (chinese, 0);
            if (legacy)
            {
                foreach (var pair in added) zh.Add(pair.Key, pair.Value);
                return (FlatJson.Serialize(zh, true), added.Count);
            }

            // Insert only missing entries and preserve existing translations and formatting.
            int brace = chinese.LastIndexOf('}');
            int insertion = brace;
            while (insertion > 0 && char.IsWhiteSpace(chinese[insertion - 1])) insertion--;
            var newline = chinese.Contains("\r\n") ? "\r\n" : "\n";
            var entries = added.Select(pair => $"  \"{FlatJson.Escape(pair.Key)}\": \"{FlatJson.Escape(pair.Value)}\"");
            var suffix = chinese.Substring(insertion, brace - insertion).Contains("\n") ? "" : newline;
            return (chinese.Insert(insertion, (zh.Count == 0 ? "" : ",") + newline +
                string.Join("," + newline, entries) + suffix), added.Count);
        }

        static IEnumerable<string> Placeholders(string text)
        {
            return FormatPlaceholder.Matches(text).Cast<Match>().Select(m => m.Value)
                .OrderBy(value => value, StringComparer.Ordinal);
        }

        internal static Result Apply(IEnumerable<FileChange> selected) => Run(selected, false);

        internal static Plan PreviewRestore()
        {
            var plan = new Plan();
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name.StartsWith("com.unity.", StringComparison.Ordinal) ||
                    package.name.StartsWith("com.vrchat.", StringComparison.Ordinal)) continue;
                if (!TryGetEmbeddedRoot(package, out var root)) continue;
                try
                {
                    var version = ReadPackageVersion(root, package.name);
                    foreach (var backup in FindFiles(root, "*" + BackupSuffix, plan.Messages))
                    {
                        var original = backup.Substring(0, backup.Length - BackupSuffix.Length);
                        try
                        {
                            CheckPath(original);
                            CheckPath(backup);
                            var current = File.ReadAllBytes(original);
                            var saved = File.ReadAllBytes(backup);
                            ValidateBackup(package.name, version, original, saved, current);
                            plan.Files.Add(new FileChange(package.name, version, original, "恢复备份", current, saved, true));
                        }
                        catch (Exception e)
                        {
                            plan.Messages.Add($"{original}：{e.Message}");
                        }
                    }
                }
                catch (Exception e)
                {
                    plan.Messages.Add($"{package.name}：{e.Message}");
                }
            }
            return plan;
        }

        internal static Result Restore(IEnumerable<FileChange> selected) => Run(selected, true);

        internal static Result RestoreAll()
        {
            var plan = PreviewRestore();
            var result = Restore(plan.Files);
            result.Messages.InsertRange(0, plan.Messages);
            return result;
        }

        static Result Run(IEnumerable<FileChange> selected, bool restore)
        {
            var result = new Result();
            var packages = PackageInfo.GetAllRegisteredPackages().ToDictionary(p => p.name, StringComparer.Ordinal);
            foreach (var change in selected)
            {
                try
                {
                    if (change.Restoring != restore)
                        throw new InvalidOperationException("预览类型已变更，请重新预览。");
                    if (!packages.TryGetValue(change.PackageName, out var package) ||
                        !TryGetEmbeddedRoot(package, out var root) ||
                        !change.Path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison) ||
                        ReadPackageVersion(root, change.PackageName) != change.Version)
                        throw new InvalidOperationException("包版本或安装路径已变化，请重新预览。");

                    var path = change.Path;
                    var backup = path + BackupSuffix;
                    var receipt = backup + ReceiptSuffix;
                    CheckPath(path);
                    CheckPath(backup);
                    CheckPath(receipt);
                    var current = File.ReadAllBytes(path);
                    if (!current.SequenceEqual(change.Original))
                        throw new IOException("文件在预览后已变化。");

                    if (restore)
                    {
                        var saved = File.ReadAllBytes(backup);
                        if (!saved.SequenceEqual(change.Translated))
                            throw new IOException("备份在预览后已变化。");
                        ValidateBackup(change.PackageName, change.Version, path, saved, current);
                        if (!current.SequenceEqual(saved))
                            ReplaceFile(path, saved, null);
                        result.FilesChanged++;
                        File.Delete(backup);
                        File.Delete(receipt);
                    }
                    else
                    {
                        if (File.Exists(backup)) throw new IOException("已有备份，请先还原。");
                        var stamp = change.Version + "\n" + Fingerprint(current) + "\n" + Fingerprint(change.Translated) + "\n";
                        WriteReceipt(receipt, stamp);
                        ReplaceFile(path, change.Translated, backup);
                        result.FilesChanged++;
                    }
                }
                catch (Exception e)
                {
                    result.Messages.Add($"{change.RelativePath}：{e.Message}");
                }
            }

            if (result.FilesChanged > 0) AssetDatabase.Refresh();
            var action = restore ? "还原" : "应用";
            result.Report = $"已{action} {result.FilesChanged} 个文件" +
                (result.Messages.Count > 0 ? $"，{result.Messages.Count} 条提示" : "");
            Log(result.Report);
            foreach (var message in result.Messages) Log(message);
            return result;
        }

        static void ValidateBackup(string package, string version, string path, byte[] backup, byte[] current)
        {
            var receipt = path + BackupSuffix + ReceiptSuffix;
            CheckPath(receipt);
            if (File.Exists(receipt))
            {
                var stamp = File.ReadAllLines(receipt);
                var currentHash = Fingerprint(current);
                if (stamp.Length != 3 || stamp[0] != version ||
                    stamp[1] != Fingerprint(backup) ||
                    (stamp[1] != currentHash && stamp[2] != currentHash))
                    throw new IOException("当前包、文件或备份与上次应用不符，保留现有文件。");
                return;
            }

            // Legacy releases stored only the original bytes. Verify their exact output before undoing.
            var original = ReadText(backup, out _);
            if (package == ModularAvatar && string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                var patched = original;
                foreach (var (en, zh) in MenuItems)
                    patched = patched.Replace("\"" + en + "\"", "\"" + zh + "\"");
                if (patched != original && (current.SequenceEqual(backup) ||
                    current.SequenceEqual(new UTF8Encoding(false).GetBytes(patched)))) return;
            }
            else if (string.Equals(Path.GetFileName(path), "zh-Hans.json", StringComparison.Ordinal) &&
                string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "Localization", StringComparison.Ordinal))
            {
                var english = Path.Combine(Path.GetDirectoryName(path), "en-US.json");
                CheckPath(english);
                if (File.Exists(english))
                {
                    var patched = AddMissingTranslations(original, File.ReadAllText(english), true);
                    if (patched.count > 0 && (current.SequenceEqual(backup) ||
                        current.SequenceEqual(EncodeText(patched.text, new UTF8Encoding(true))))) return;
                }
            }
            throw new IOException("无法验证旧备份与当前文件的对应关系，保留现有文件。");
        }

        static string Fingerprint(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        static void ReplaceFile(string path, byte[] content, string backup)
        {
            var temp = path + ".ygt-tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temp, content);
                File.Replace(temp, path, backup);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        static void WriteReceipt(string path, string stamp)
        {
            var bytes = new UTF8Encoding(false).GetBytes(stamp);
            if (File.Exists(path)) ReplaceFile(path, bytes, null);
            else
            {
                // CreateNew prevents overwriting a file that appeared after preview.
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    stream.Write(bytes, 0, bytes.Length);
            }
        }

        static string ReadText(byte[] bytes, out Encoding encoding)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
            {
                var text = reader.ReadToEnd();
                encoding = reader.CurrentEncoding;
                return text;
            }
        }

        static byte[] EncodeText(string text, Encoding encoding)
        {
            return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        }

        static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        static bool TryGetEmbeddedRoot(PackageInfo package, out string root)
        {
            root = null;
            if (package.source != PackageSource.Embedded || string.IsNullOrEmpty(package.resolvedPath) ||
                string.IsNullOrEmpty(package.name) || package.name != Path.GetFileName(package.name))
                return false;

            var expected = Path.GetFullPath(Path.Combine(PackagesRoot, package.name));
            var actual = Path.GetFullPath(package.resolvedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(expected, actual, PathComparison) || !Directory.Exists(expected))
                return false;
            root = expected;
            return true;
        }

        static string ReadPackageVersion(string root, string name)
        {
            var manifest = Path.Combine(root, "package.json");
            CheckPath(manifest);
            var package = JsonUtility.FromJson<PackageManifest>(File.ReadAllText(manifest));
            if (package == null || package.name != name || string.IsNullOrWhiteSpace(package.version))
                throw new IOException("package.json 的包 ID 或版本无法验证。");
            return package.version;
        }

        static void CheckPath(string path)
        {
            var root = Path.GetFullPath(PackagesRoot);
            var candidate = Path.GetFullPath(path);
            if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, PathComparison) &&
                !string.Equals(root, candidate, PathComparison))
                throw new IOException("目标在项目的内嵌包目录之外。");

            while (true)
            {
                try
                {
                    if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("目标经过符号链接或目录联接。");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (string.Equals(root, candidate, PathComparison)) return;
                candidate = Path.GetDirectoryName(candidate);
            }
        }

        static IEnumerable<string> FindFiles(string root, string pattern, List<string> messages)
        {
            if (!Directory.Exists(root)) yield break;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                string[] files;
                try
                {
                    CheckPath(directory);
                    files = Directory.GetFiles(directory, pattern);
                    foreach (var subdirectory in Directory.GetDirectories(directory)) pending.Push(subdirectory);
                }
                catch (Exception e)
                {
                    messages.Add($"{directory}：{e.Message}");
                    continue;
                }

                foreach (var file in files)
                {
                    // Some .NET wildcard implementations match longer filename extensions.
                    if (pattern == "*" + BackupSuffix && !file.EndsWith(BackupSuffix, StringComparison.Ordinal)) continue;
                    if (pattern == "*.cs" && !file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    yield return file;
                }
            }
        }

        internal static void Log(string line)
        {
            try
            {
                var dir = DataDir;
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "catalog-patch.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "\t" + line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { /* 日志失败不影响主流程 */ }
        }

        /// <summary>MA 的词典是平铺的 string->string JSON，这里只做这一种，够用且不会误伤嵌套结构。</summary>
        internal static class FlatJson
        {
            internal static Dictionary<string, string> Parse(string text)
            {
                if (string.IsNullOrEmpty(text)) return null;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                int i = 0;
                if (text.Length > 0 && text[0] == '\uFEFF') i = 1;
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != '{') return null;
                i++;
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == '}')
                    return IsEnd(text, i + 1) ? map : null;
                while (i < text.Length)
                {
                    SkipWs(text, ref i);
                    if (i >= text.Length || text[i] != '"') return null;
                    var key = ReadString(text, ref i);
                    if (key == null) return null;
                    SkipWs(text, ref i);
                    if (i >= text.Length || text[i] != ':') return null;
                    i++;
                    SkipWs(text, ref i);
                    if (i >= text.Length || text[i] != '"') return null;
                    var val = ReadString(text, ref i);
                    if (val == null) return null;
                    map[key] = val;
                    SkipWs(text, ref i);
                    if (i < text.Length && text[i] == ',') { i++; continue; }
                    if (i < text.Length && text[i] == '}') return IsEnd(text, i + 1) ? map : null;
                    return null;
                }
                return null;
            }

            static bool IsEnd(string text, int index)
            {
                SkipWs(text, ref index);
                return index == text.Length;
            }

            internal static string Serialize(Dictionary<string, string> map, bool legacy = false)
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                int n = 0;
                foreach (var kv in map)
                {
                    sb.Append("  \"").Append(Escape(kv.Key, legacy)).Append("\": \"").Append(Escape(kv.Value, legacy)).Append('"');
                    sb.Append(++n < map.Count ? ",\n" : "\n");
                }
                sb.Append('}');
                return sb.ToString();
            }

            static void SkipWs(string s, ref int i)
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
            }

            static string ReadString(string s, ref int i)
            {
                i++; // 跳过起始引号
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ') return null;
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) break;
                    var e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'u':
                            if (i + 4 <= s.Length &&
                                ushort.TryParse(s.Substring(i, 4), System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            else return null;
                            break;
                        default: return null;
                    }
                }
                return null;
            }

            internal static string Escape(string s, bool legacy = false)
            {
                var sb = new StringBuilder(s.Length + 8);
                foreach (var c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ' && !legacy) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
                return sb.ToString();
            }
        }
    }
}
