using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using CompilationPipeline = UnityEditor.Compilation.CompilationPipeline;
using Debug = UnityEngine.Debug;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace YGTextTranslator
{
    /// <summary>Translates draw arguments while preserving Unity's control and layout code.</summary>
    internal static class HookHost
    {
        const int RecentCapacity = 10;
        // Important 4：Inspector 一帧有几十到上百条不同文本，窗口太小会把老文本挤出后重新计一次。
        // 4096 条的字符串内存开销可以忽略，换得 CaptureCount 真正接近「唯一文本数」。
        const int DedupWindow = 4096;
        const int ProbeLogQueueMaxChars = 256 * 1024;
        const long ProbeLogMaxBytes = 8L * 1024 * 1024;
        const int ErrorTripThreshold = 3;

        const string KeyStr0 = nameof(LayoutContent);
        const string KeyStr1 = nameof(GuiContent);
        const string KeyContent1 = nameof(LabelField);
        static readonly object Gate = new object();
        static readonly HashSet<string> PatchedKeys = new HashSet<string>();
        static readonly List<string> Recent = new List<string>(RecentCapacity);
        static readonly Queue<string> DedupOrder = new Queue<string>(DedupWindow);
        static readonly HashSet<string> DedupSet = new HashSet<string>();
        // Important 1：熔断按 prefix 分桶。全局单计数会被其它 prefix 的成功调用清零，
        // 真实 Inspector 里 Layout/Repaint 交错，>=3 永远到不了。
        static readonly Dictionary<string, int> ErrorBuckets = new Dictionary<string, int>();

        static int _captureCount;
        static int _hitCount;
        static int _enteredCount;
        static int _totalErrors;
        static bool _logDrainHooked;
        static string _probeLogPath;
        static long _probeLogBytes;
        static bool _probeLogTruncated;
        static bool _probeLogWarned;
        static bool _suppressCapture;
        // Minor 6：熔断排队的标记。避免同一时刻排多次 EditorApplication.delayCall。
        static bool _tripScheduled;

        const int ReplacementCacheCapacity = 1024;
        static readonly Dictionary<ContentKey, GUIContent> ReplacementCache = new Dictionary<ContentKey, GUIContent>();

        struct ContentKey : IEquatable<ContentKey>
        {
            public string Text;
            public Texture Image;
            public string Tooltip;
            public int StyleHash;

            public ContentKey(string text, Texture image, string tooltip, int styleHash)
            {
                Text = text;
                Image = image;
                Tooltip = tooltip;
                StyleHash = styleHash;
            }

            public bool Equals(ContentKey other)
            {
                return StyleHash == other.StyleHash
                    && ReferenceEquals(Image, other.Image)
                    && string.Equals(Text, other.Text, StringComparison.Ordinal)
                    && string.Equals(Tooltip, other.Tooltip, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ContentKey && Equals((ContentKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Text != null ? Text.GetHashCode() : 0;
                    hash = (hash * 397) ^ (Image != null ? Image.GetHashCode() : 0);
                    hash = (hash * 397) ^ (Tooltip != null ? Tooltip.GetHashCode() : 0);
                    hash = (hash * 397) ^ StyleHash;
                    return hash;
                }
            }
        }

        /// <summary>是否已挂上 patch（Enable 成功且尚未 Disable）。</summary>
        public static bool Enabled { get; private set; }

        /// <summary>探针模式：开启时被截获的文本追加写 probe.log。</summary>
        public static bool ProbeMode { get; set; }

        /// <summary>成功挂上的 patch 条数（按 (original,prefix) 去重）。</summary>
        public static int PatchedCount { get { lock (Gate) return PatchedKeys.Count; } }

        /// <summary>
        /// 去重后的截获文本条数（同一文本在 4096 条先进先出窗口内只计一次）。
        /// 超长列表滚动时仍可能缓慢虚涨：淘汰的文本会被重新计一次；T4 替换阶段按文本 hash 缓存译文，不依赖本计数。
        /// </summary>
        public static int CaptureCount { get { lock (Gate) return _captureCount; } }

        /// <summary>所有命中次数（含重复，用于观察 Layout/Repaint 双跑）。</summary>
        public static int HitCount { get { lock (Gate) return _hitCount; } }

        /// <summary>累计失败次数（所有 prefix 相加，测试可读）。</summary>
        public static int TotalErrors { get { lock (Gate) return _totalErrors; } }

        /// <summary>各 prefix 失败桶里的最大值（0 表示当前没有失败在累积）。</summary>
        public static int ConsecutiveErrors
        {
            get
            {
                lock (Gate)
                {
                    var max = 0;
                    foreach (var pair in ErrorBuckets)
                        if (pair.Value > max) max = pair.Value;
                    return max;
                }
            }
        }

        /// <summary>
        /// R14：Prefix 被进入的累计次数（在每个 Prefix 的第一行无条件自增，先于 Enabled 判断）。
        /// Disable() 之后再调一次 GUI.Label / GUILayout.Label，这个值必须不增——这才是 detour
        /// 真被摘掉的证据，而不是只看插件自家的 PatchedCount 账本。
        /// </summary>
        public static int EnteredCount { get { return Volatile.Read(ref _enteredCount); } }

        /// <summary>probe.log 的绝对路径（未启用探针时为 null）。</summary>
        public static string ProbeLogPath { get { return _probeLogPath; } }

        /// <summary>抑制捕获（探针窗口绘制自身 UI 时使用，避免自我增殖）。</summary>
        public static bool SuppressCapture
        {
            get { return _suppressCapture; }
            set { _suppressCapture = value; }
        }

        /// <summary>命中一条文本时触发；订阅者异常会被隔离。</summary>
        public static event Action<string> OnText;

        // ---------------------------------------------------------------- Enable / Disable

        /// <summary>初始化 Harmony 并挂上全部目标 patch。重复调用不会重复计数。</summary>
        public static void Enable()
        {
            // 不做 Enabled 早退：去重保证不会重复 patch，失败项可以在这里重试补齐。

            // 词典与字体预热放在 Enable（非 GUI 路径），避免第一次 Prefix 命中时做文件 IO / 字体创建。
            TextPipeline.EnsureLoaded();
            FontFixer.Warmup();

            if (!HarmonyLoader.TryInit(out var error))
            {
                Debug.LogError($"[YGTextTranslator] HookHost.Enable 失败：Harmony 未就绪 {error}");
                return;
            }

            if (ProbeMode) EnsureProbeLog();
            HookLogDrain(); // Important 3：日志排空只在这里接上，Patch 调用路径不碰文件 IO

            var added = 0;
            var failed = 0;
            foreach (var spec in Targets())
            {
                if (spec.Original == null || spec.Prefix == null)
                {
                    failed++;
                    Debug.LogError($"[YGTextTranslator] 目标缺失：{spec.Key}（original={spec.Original}, prefix={spec.Prefix}）");
                    continue;
                }

                lock (Gate)
                {
                    if (PatchedKeys.Contains(spec.Key)) continue;
                    PatchedKeys.Add(spec.Key);
                }

                if (HarmonyLoader.Patch(spec.Original, spec.Prefix))
                {
                    added++;
                }
                else
                {
                    lock (Gate) PatchedKeys.Remove(spec.Key);
                    failed++;
                }
            }

            Enabled = PatchedCount > 0;
            Debug.Log($"[YGTextTranslator] HookHost.Enable 完成：新增 patch {added}，失败 {failed}，PatchedCount={PatchedCount}");
        }

        /// <summary>撤掉本插件所有 patch。幂等；重复调用安全。</summary>
        public static void Disable()
        {
            UnhookLogDrain();
            EditorApplication.delayCall -= RunDeferredTrip;
            _tripScheduled = false;

            if (!Enabled && PatchedCount == 0) return;

            Enabled = false;
            NativeAliasHooks.Disable();
            HarmonyLoader.UnpatchAll();
            UiTextHook.NotifyUnpatched();

            lock (Gate)
            {
                PatchedKeys.Clear();
                ErrorBuckets.Clear();
            }

            FlushProbeLog();
            Debug.Log($"[YGTextTranslator] HookHost.Disable 完成：PatchedCount={PatchedCount}");
        }

        /// <summary>清空统计与最近列表（不清 patch）。</summary>
        public static void ClearStats()
        {
            lock (Gate)
            {
                _captureCount = 0;
                _hitCount = 0;
                Recent.Clear();
                DedupOrder.Clear();
                DedupSet.Clear();
                ErrorBuckets.Clear();
                _totalErrors = 0;
            }
        }

        /// <summary>最近截获文本的快照（旧 -> 新，最多 10 条）。</summary>
        public static string[] RecentSnapshot()
        {
            lock (Gate) return Recent.ToArray();
        }

        /// <summary>删除 probe.log 并重置写入状态（探针自检前调用）。</summary>
        public static void ResetProbeLog()
        {
            try
            {
                var path = ResolveProbeLogPath();
                if (File.Exists(path)) File.Delete(path);
                var header = $"===== 探针会话 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}";
                File.AppendAllText(path, header, Encoding.UTF8);
                _probeLogPath = path;
                // Minor 2：会话头本身也占字节，按真实文件长度起步，否则 8MB 上限会略微超。
                _probeLogBytes = new FileInfo(path).Length;
                _probeLogTruncated = false;
                _probeLogWarned = false;
                lock (Gate)
                {
                    LogBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YGTextTranslator] 重置 probe.log 失败：{ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 测试入口

        /// <summary>
        /// 测试入口（batchmode 用）：走与 Prefix 完全相同的记录逻辑，返回传入的值。
        /// 用于断言「OnText 收到文本」且「ref 值未被修改」。
        /// </summary>
        internal static string __TestFire(string text)
        {
            // T3 回归入口：保持「只记录、不改值」的旧语义，不经过替换路径。
            Interlocked.Increment(ref _enteredCount);
            var value = text;
            try
            {
                if (Enabled && ShouldCapture() && !string.IsNullOrEmpty(value)) Record(value);
                NoteSuccess(KeyStr1);
            }
            catch (Exception ex)
            {
                SafeNoteFailure(KeyStr1, ex);
            }
            return value;
        }

        /// <summary>
        /// 测试入口：指定 prefix 的失败桶连续累加 N 次，用于验证「同一 prefix 连续 3 次异常 → 自动停用」。
        /// prefixKey 用 GUI.Label(Rect,string) 之类真实 key，或直接用 HookHost 内部的 Key 常量名。
        /// </summary>
        internal static void __TestRaiseErrors(string prefixKey, int times)
        {
            for (var i = 0; i < times; i++)
                NoteFailure(prefixKey, new InvalidOperationException($"probe-injected-error-{i + 1}"));
        }

        /// <summary>测试入口：指定 prefix 报一次成功（用于验证「其它 prefix 成功不会清掉本桶」）。</summary>
        internal static void __TestNoteSuccess(string prefixKey)
        {
            NoteSuccess(prefixKey);
        }

        /// <summary>测试入口：走「是否替换 + 译文」决策，但不实际绘制。T4 探针用。</summary>
        internal static string __TestIntercept(string text)
        {
            return Enabled ? TextPipeline.TranslateDisplay(text) : text;
        }

        /// <summary>测试用：暴露内部 Key 常量，避免测试里硬编码字符串。</summary>
        internal static readonly string TestKeyStr1 = KeyStr1;
        internal static readonly string TestKeyContent1 = KeyContent1;

        struct TargetSpec
        {
            public string Key;
            public MethodInfo Original;
            public MethodInfo Prefix;
        }

        static TargetSpec[] Targets()
        {
            var rect = typeof(Rect);
            var content = typeof(GUIContent);
            var style = typeof(GUIStyle);
            var options = typeof(GUILayoutOption[]);
            var boolean = typeof(bool);
            var id = typeof(int);

            return new[]
            {
                Spec(typeof(GUI), "Label", nameof(GuiContent), rect, content, style),
                Spec(typeof(GUI), "Box", nameof(GuiContent), rect, content, style),
                Spec(typeof(GUI), "Button", nameof(GuiButton), rect, id, content, style),
                Spec(typeof(GUI), "Toggle", nameof(GuiToggle), rect, id, boolean, content, style),
                Spec(typeof(GUILayout), "DoLabel", nameof(LayoutContent), content, style, options),
                Spec(typeof(GUILayout), "DoBox", nameof(LayoutContent), content, style, options),
                Spec(typeof(GUILayout), "DoButton", nameof(LayoutContent), content, style, options),
                Spec(typeof(GUILayout), "DoToggle", nameof(LayoutToggle), boolean, content, style, options),
                Spec(typeof(EditorGUI), "LabelFieldInternal", nameof(LabelField), rect, content, content, style),
                Spec(typeof(EditorGUI), "PrefixLabel", nameof(PrefixLabel), rect, id, content, style),
                Spec(typeof(EditorGUI), "ToggleLeftInternal", nameof(ToggleLeft), rect, content, boolean, style),
                Spec(typeof(EditorGUI), "FoldoutInternal", nameof(Foldout), rect, boolean, content, boolean, style),
            };
        }

        static TargetSpec Spec(Type type, string method, string prefixName, params Type[] args)
        {
            var original = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, args, null);
            var prefix = typeof(HookHost).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
            return new TargetSpec { Key = type.Name + "." + method, Original = original, Prefix = prefix };
        }

        static void GuiContent(ref GUIContent __1, ref GUIStyle __2) =>
            TranslateArguments(ref __1, ref __2, KeyStr1, false);

        static void GuiButton(ref GUIContent __2, ref GUIStyle __3) =>
            TranslateArguments(ref __2, ref __3, nameof(GuiButton), false);

        static void GuiToggle(ref GUIContent __3, ref GUIStyle __4) =>
            TranslateArguments(ref __3, ref __4, nameof(GuiToggle), false);

        static void LayoutContent(ref GUIContent __0, ref GUIStyle __1) =>
            TranslateArguments(ref __0, ref __1, KeyStr0);

        static void LayoutToggle(ref GUIContent __1, ref GUIStyle __2) =>
            TranslateArguments(ref __1, ref __2, nameof(LayoutToggle));

        static void LabelField(ref GUIContent __2, ref GUIStyle __3) =>
            TranslateArguments(ref __2, ref __3, KeyContent1);

        static void PrefixLabel(ref GUIContent __2, ref GUIStyle __3) =>
            TranslateArguments(ref __2, ref __3, nameof(PrefixLabel));

        static void ToggleLeft(ref GUIContent __1, ref GUIStyle __3) =>
            TranslateArguments(ref __1, ref __3, nameof(ToggleLeft));

        static void Foldout(ref GUIContent __2, ref GUIStyle __4) =>
            TranslateArguments(ref __2, ref __4, nameof(Foldout));

        static void TranslateArguments(ref GUIContent content, ref GUIStyle style, string key, bool fromLayout = true)
        {
            Interlocked.Increment(ref _enteredCount);
            if (!Enabled || TextPipeline.Suppress || content == null || string.IsNullOrWhiteSpace(content.text))
                return;

            try
            {
                if (!DisplayScope.TryGetImGuiPackage(fromLayout, out var packageId)) return;
                var original = content.text;
                if (ShouldCapture()) Record(original);
                var translation = TextPipeline.TranslateDisplay(original, packageId);
                if (translation != original)
                {
                    style = FontFixer.ForTranslation(style, translation);
                    content = GetReplacementContent(translation, content.image, content.tooltip, style);
                }
                NoteSuccess(key);
            }
            catch (Exception exception)
            {
                SafeNoteFailure(key, exception);
            }
        }

        internal static void ReportTranslationFailure(string key, Exception exception)
        {
            SafeNoteFailure(key, exception);
        }

        static GUIContent GetReplacementContent(string text, Texture image, string tooltip, GUIStyle style)
        {
            var key = new ContentKey(text, image, tooltip, style != null ? style.GetHashCode() : 0);
            lock (Gate)
            {
                if (ReplacementCache.TryGetValue(key, out var cached)) return cached;
                if (ReplacementCache.Count >= ReplacementCacheCapacity) ReplacementCache.Clear();
                var content = new GUIContent(text, image, tooltip);
                ReplacementCache[key] = content;
                return content;
            }
        }

        // ---------------------------------------------------------------- 记录 / 统计 / 熔断

        static bool ShouldCapture()
        {
            if (_suppressCapture) return false;

            var evt = Event.current;
            if (evt == null) return true; // batchmode / 直接调用：无 GUI 事件，探针路径照常记录

            return evt.type == EventType.Repaint || evt.type == EventType.Layout;
        }

        static void Record(string text)
        {
            bool appended;
            lock (Gate)
            {
                _hitCount++;

                if (DedupSet.Contains(text)) return;

                DedupSet.Add(text);
                // Important 4：DedupOrder 是先进先出的滑窗，窗口满了才逐出最老的文本。
                DedupOrder.Enqueue(text);
                while (DedupOrder.Count > DedupWindow)
                    DedupSet.Remove(DedupOrder.Dequeue());

                _captureCount++;
                Recent.Add(text);
                if (Recent.Count > RecentCapacity) Recent.RemoveAt(0);
                appended = true;
            }

            if (appended && ProbeMode) AppendProbeLog(text);

            var handler = OnText;
            if (handler == null) return;

            try
            {
                handler(text);
            }
            catch (Exception ex)
            {
                // 订阅者的异常不得影响 GUI，也不计入 Prefix 熔断。
                Debug.LogWarning($"[YGTextTranslator] OnText 订阅者异常（已隔离）：{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Self-review 加固：熔断记数自身（Debug.LogError / Disable / 文件 flush）绝不能把异常
        /// 抛回 Unity 的 GUI 循环。这一层是 Prefix catch 分支的最后一道隔离。
        /// </summary>
        static void SafeNoteFailure(string prefixKey, Exception ex)
        {
            try
            {
                NoteFailure(prefixKey, ex);
            }
            catch
            {
                // 已经无法可靠记日志了（LogError 自己炸），只能吞掉保住 GUI。
            }
        }

        static void NoteSuccess(string prefixKey)
        {
            lock (Gate)
            {
                // Important 1：只清自己的桶。别的 prefix 成功不能把本 prefix 的失败计数抹平。
                if (!ErrorBuckets.ContainsKey(prefixKey)) return;
                ErrorBuckets.Remove(prefixKey);
            }
        }

        static void NoteFailure(string prefixKey, Exception ex)
        {
            int count;
            lock (Gate)
            {
                _totalErrors++;
                ErrorBuckets.TryGetValue(prefixKey, out count);
                count++;
                ErrorBuckets[prefixKey] = count;
            }

            Debug.LogError($"[YGTextTranslator] HookHost Prefix 异常（{prefixKey} 连续 {count} 次）：{ex}");

            if (count >= ErrorTripThreshold && Enabled)
            {
                Debug.LogError($"[YGTextTranslator] HookHost {prefixKey} 连续 {ErrorTripThreshold} 次 Prefix 异常，自动停用钩子。");
                // Minor 6（re-review）：不在 Prefix 栈上同步 Disable —— 那会在 GUI 回调里做
                // UnpatchAll + 文件 flush。延到下一次编辑器 tick，本帧 GUI 先正常画完。
                ScheduleTrip();
            }
        }

        /// <summary>
        /// Minor 6（re-review）：熔断触发的 Disable 不在这里同步跑（那会在 GUI 回调栈上做
        /// UnpatchAll + 文件 flush），改为排到下一次编辑器 tick。同一时刻只排一次。
        /// </summary>
        static void ScheduleTrip()
        {
            if (_tripScheduled) return;
            _tripScheduled = true;
            EditorApplication.delayCall += RunDeferredTrip;
        }

        static void RunDeferredTrip()
        {
            _tripScheduled = false;
            if (!Enabled) return;
            Disable();
        }

        /// <summary>测试入口：立刻执行排队的熔断（batchmode 下 delayCall 不一定被驱动）。</summary>
        internal static void __DrainDeferredTrip()
        {
            if (_tripScheduled) RunDeferredTrip();
        }

        /// <summary>测试入口：清空熔断桶与排队中的熔断，保证连续自检不被上一轮污染。</summary>
        internal static void __TestResetErrors()
        {
            EditorApplication.delayCall -= RunDeferredTrip;
            _tripScheduled = false;
            lock (Gate)
            {
                ErrorBuckets.Clear();
                _totalErrors = 0;
            }
        }

        // ---------------------------------------------------------------- probe.log

        static string ResolveProbeLogPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "YGUnityTranslator");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "probe.log");
        }

        static void EnsureProbeLog()
        {
            if (_probeLogPath != null) return;
            try
            {
                _probeLogPath = ResolveProbeLogPath();
                _probeLogBytes = File.Exists(_probeLogPath) ? new FileInfo(_probeLogPath).Length : 0;
                File.AppendAllText(_probeLogPath, $"===== 探针会话 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _probeLogPath = null;
                if (!_probeLogWarned)
                {
                    _probeLogWarned = true;
                    Debug.LogWarning($"[YGTextTranslator] probe.log 初始化失败（后续静默）：{ex.Message}");
                }
            }
        }

        // Important 3：patch 调用路径上禁止文件 IO。命中只把行塞进内存队列，
        // 由 Enable() 注册的 EditorApplication.update 统一排空落盘。
        static readonly StringBuilder LogBuffer = new StringBuilder();

        /// <summary>把缓冲里的探针日志落盘（Disable / update 排空时调用）。</summary>
        public static void FlushProbeLog()
        {
            string payload;
            lock (Gate)
            {
                if (LogBuffer.Length == 0) return;
                if (_probeLogPath == null || _probeLogTruncated)
                {
                    LogBuffer.Clear();
                    return;
                }

                payload = LogBuffer.ToString();
                LogBuffer.Clear();
            }

            try
            {
                // Minor 6（re-review）：GetByteCount 也可能抛，和文件写入放在同一个 try 里。
                var bytes = Encoding.UTF8.GetByteCount(payload);
                if (_probeLogBytes + bytes > ProbeLogMaxBytes)
                {
                    _probeLogTruncated = true;
                    File.AppendAllText(_probeLogPath, $"（已达 {ProbeLogMaxBytes / (1024 * 1024)} MB 上限，后续不再写入）{Environment.NewLine}", Encoding.UTF8);
                    Debug.LogWarning("[YGTextTranslator] probe.log 达到大小上限，停止写入。");
                    return;
                }

                File.AppendAllText(_probeLogPath, payload, Encoding.UTF8);
                _probeLogBytes += bytes;
            }
            catch (Exception ex)
            {
                if (!_probeLogWarned)
                {
                    _probeLogWarned = true;
                    Debug.LogWarning($"[YGTextTranslator] probe.log 写入失败（后续静默）：{ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        static void AppendProbeLog(string text)
        {
            lock (Gate)
            {
                if (_probeLogPath == null || _probeLogTruncated) return;
                // 队列积压上限：硬盘卡住时宁可丢新行，也不让内存无限涨。
                if (LogBuffer.Length >= ProbeLogQueueMaxChars) return;

                LogBuffer.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append('\t').Append(text).Append(Environment.NewLine);
            }
        }

        /// <summary>Important 3：日志排空挂在 EditorApplication.update 上，不在 Prefix/Record 路径里做 IO。</summary>
        static void HookLogDrain()
        {
            if (_logDrainHooked) return;
            _logDrainHooked = true;
            EditorApplication.update -= DrainProbeLog;
            EditorApplication.update += DrainProbeLog;
        }

        /// <summary>Disable 时注销定时器（要求：Disable 停掉所有钩子/定时器）。</summary>
        static void UnhookLogDrain()
        {
            if (!_logDrainHooked) return;
            _logDrainHooked = false;
            EditorApplication.update -= DrainProbeLog;
        }

        static void DrainProbeLog()
        {
            FlushProbeLog();
        }
    }
    internal static class DisplayScope
    {
        static readonly Dictionary<Assembly, string> PackageIds = new Dictionary<Assembly, string>();

        internal static string[] ObservedAssetIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in PackageIds.Values)
            {
                if (id != null && id.StartsWith("assets:", StringComparison.Ordinal)) ids.Add(id);
            }
            var result = new string[ids.Count];
            ids.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        internal static string GetPackageId(Type type)
        {
            var assembly = type.Assembly;
            if (PackageIds.TryGetValue(assembly, out var cached)) return cached;

            var name = assembly.GetName().Name;
            string packageId = null;
            if (assembly != typeof(DisplayScope).Assembly && !name.StartsWith("UnityEditor", StringComparison.Ordinal)
                && !name.StartsWith("UnityEngine", StringComparison.Ordinal))
            {
                var package = PackageInfo.FindForAssembly(assembly);
                if (package != null && package.source != PackageSource.BuiltIn)
                    packageId = package.name;
                else if (package == null && IsProjectAssembly(assembly, name))
                    packageId = "assets:" + name;
            }
            PackageIds[assembly] = packageId;
            return packageId;
        }

        static bool IsProjectAssembly(Assembly assembly, string name)
        {
            if (name == "Assembly-CSharp" || name == "Assembly-CSharp-Editor" ||
                name == "Assembly-CSharp-firstpass" || name == "Assembly-CSharp-Editor-firstpass")
                return true;

            var definition = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(name);
            if (!string.IsNullOrEmpty(definition) && definition.StartsWith("Assets/", StringComparison.Ordinal))
                return true;

            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) return false;
            var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(assembly.Location).StartsWith(assets, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGetImGuiPackage(bool fromLayout, out string packageId)
        {
            packageId = null;
            // The caller establishes whether a GUI draw belongs to an editor plugin or to a project item.
            var trace = new StackTrace(1, false);
            for (var i = 0; i < trace.FrameCount; i++)
            {
                var method = trace.GetFrame(i).GetMethod();
                var type = method?.DeclaringType;
                if (type == null) continue;

                if (!fromLayout && (type == typeof(GUILayout) ||
                    (method.Name.Contains("GUILayout") && type.Assembly == typeof(GUI).Assembly)))
                    return false; // GUILayout already translated and measured its arguments.

                if (type == typeof(DevSelfTest)) return true;
                if (method.Name == "RowGUI" || method.Name == "DrawItem" ||
                    type.Name.IndexOf("TreeView", StringComparison.Ordinal) >= 0 ||
                    type.Name == "ProjectBrowser" || type.Name == "SceneHierarchyWindow" ||
                    type.Name == "ObjectSelector")
                    return false;

                while (type.DeclaringType != null && !IsEditorOwner(type))
                    type = type.DeclaringType;

                if (!IsEditorOwner(type)) continue;
                packageId = GetPackageId(type);
                return packageId != null;
            }
            return false;
        }

        static bool IsEditorOwner(Type type)
        {
            return typeof(EditorWindow).IsAssignableFrom(type) || typeof(Editor).IsAssignableFrom(type) ||
                typeof(PropertyDrawer).IsAssignableFrom(type) || typeof(DecoratorDrawer).IsAssignableFrom(type) ||
                typeof(ShaderGUI).IsAssignableFrom(type);
        }
    }
}
