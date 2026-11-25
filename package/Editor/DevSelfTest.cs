using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YGTextTranslator
{
    /// <summary>
    /// batchmode 自检入口：批处理打不开窗口，用它跑真实的 Harmony 初始化 + patch/撤销并写日志。
    /// 调用：
    ///   Unity -batchmode -nographics -quit -executeMethod YGTextTranslator.DevSelfTest.RunHarmonyProbe
    ///   Unity -batchmode -nographics -quit -executeMethod YGTextTranslator.DevSelfTest.RunHookProbe
    /// </summary>
    public static class DevSelfTest
    {
        // ---------------------------------------------------------------- 任务 2：Harmony 加载探针

        public static void RunHarmonyProbe()
        {
            var ok = false;
            var patched = false;
            try
            {
                ok = HarmonyLoader.TryInit(out var error);
                Debug.Log($"[YGProbe] TryInit={ok} Source={HarmonyLoader.Source} error={error ?? "<null>"}");

                if (ok)
                {
                    var original = typeof(GUI).GetMethod("Label", new[] { typeof(Rect), typeof(string) });
                    var prefix = typeof(DevSelfTest).GetMethod(nameof(LabelPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                    patched = HarmonyLoader.Patch(original, prefix);
                    Debug.Log($"[YGProbe] Patch={patched} original={Describe(original)} prefix={Describe(prefix)}");

                    // patch 生效时这里应打印 LabelPrefix fired。
                    InvokeLabel();
                    HarmonyLoader.UnpatchAll();
                    // 撤销后再调一次，不应再出现 LabelPrefix fired。
                    InvokeLabel();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YGProbe] 探针抛异常：{ex}");
            }
            finally
            {
                Debug.Log($"[YGProbe] RESULT Source={HarmonyLoader.Source} TryInit={ok} Patch={patched}");
                if (Application.isBatchMode)
                {
                    Console.Out.Flush();
                    EditorApplication.Exit(ok && patched ? 0 : 1);
                }
            }
        }

        // ---------------------------------------------------------------- 任务 3：HookHost 探针自检

        /// <summary>
        /// 三项断言：A) PatchedCount>=6；B) Prefix 记录逻辑收到文本且不改 ref；C) Disable 后归零且不再触发。
        /// 额外验证：真实 GUI.Label 会穿过 Prefix、连续 3 次异常自动停用、probe.log 落盘。
        /// </summary>
        public static void RunHookProbe()
        {
            var ok = true;
            var results = new List<string>();

            bool Check(string name, bool pass, string detail)
            {
                var line = $"[YGProbe] {(pass ? "PASS" : "FAIL")} {name} :: {detail}";
                results.Add(line);
                if (pass) Debug.Log(line);
                else
                {
                    ok = false;
                    Debug.LogError(line);
                }
                return pass;
            }

            Action<string> handler = null;
            var received = new List<string>();
            var restoreProbeMode = HookHost.ProbeMode; // Minor 3：finally 里要还原
            try
            {
                HookHost.ProbeMode = true;
                HookHost.ResetProbeLog();
                HookHost.ClearStats();

                handler = text => received.Add(text);
                HookHost.OnText += handler;

                HookHost.Enable();
                Debug.Log($"[YGProbe] ProbeMode={HookHost.ProbeMode} Enabled={HookHost.Enabled} " +
                          $"PatchedCount={HookHost.PatchedCount} log={HookHost.ProbeLogPath ?? "<null>"}");

                // A. patch 数量
                Check("A.PatchedCount>=6", HookHost.PatchedCount >= 6, $"PatchedCount={HookHost.PatchedCount}");

                // B. Prefix 记录逻辑
                const string testText = "YG_PROBE_TEXT_RAW";
                var returned = HookHost.__TestFire(testText);
                Check("B1.OnText 收到文本", received.Contains(testText), $"received={Join(received)}");
                Check("B2.传入 ref 值未被修改", returned == testText, $"input=\"{testText}\" returned=\"{returned}\"");

                // B3. 真实调用 GUI.Label：batchmode 下方法体可能抛「只能在 OnGUI 里调」，但 Prefix 在方法体前执行。
                const string realText = "YG_PROBE_REAL_GUI_LABEL";
                try
                {
                    GUI.Label(new Rect(0f, 0f, 1f, 1f), realText);
                    Debug.Log("[YGProbe] GUI.Label 调用完成（batchmode 下无异常）");
                }
                catch (Exception ex)
                {
                    Debug.Log($"[YGProbe] GUI.Label 抛 {ex.GetType().Name}: {ex.Message}（预期，Prefix 已在方法体前执行）");
                }
                Check("B3.真实 GUI.Label 穿过 Prefix", received.Contains(realText), $"received={Join(received)}");

                // 显式传入样式可在 batchmode 避开 GUI.skin 检查，验证真正的布局拦截点。
                const string layoutText = "YG_PROBE_REAL_GUILAYOUT_LABEL";
                var enteredBeforeLayout = HookHost.EnteredCount;
                try { GUILayout.Label(new GUIContent(layoutText), new GUIStyle()); } catch (Exception) { }
                Check("B4.GUILayout.Label 进入布局拦截",
                    HookHost.EnteredCount > enteredBeforeLayout && received.Contains(layoutText),
                    $"EnteredCount {enteredBeforeLayout}->{HookHost.EnteredCount}；received={Join(received)}");

                // C. Disable 归零 + 幂等 + 真实 detour 已摘除（R14）+ 软闸
                var capBefore = HookHost.CaptureCount;
                HookHost.Disable();
                HookHost.Disable(); // 幂等
                Check("C1.Disable 后 PatchedCount=0", HookHost.PatchedCount == 0, $"PatchedCount={HookHost.PatchedCount}");
                Check("C2.Disable 幂等（Enabled=false）", !HookHost.Enabled, $"Enabled={HookHost.Enabled}");

                // R14：这是唯一能证伪「UnpatchAll 静默失败」的断言——4 个 Prefix 的第一行无条件自增，
                // 只要 detour 还在，即使 Enabled=false 也会 +1。
                var enteredBefore = HookHost.EnteredCount;
                var hitBefore = HookHost.HitCount;
                const string afterText = "YG_PROBE_AFTER_DISABLE";
                try { GUI.Label(new Rect(0f, 0f, 1f, 1f), afterText); } catch (Exception) { }
                try { GUILayout.Label(new GUIContent(afterText), new GUIStyle()); } catch (Exception) { }
                Check("C5.R14 Disable 后 Prefix 不再被进入（detour 真被摘）",
                    HookHost.EnteredCount == enteredBefore && HookHost.HitCount == hitBefore,
                    $"EnteredCount {enteredBefore}->{HookHost.EnteredCount}；HitCount {hitBefore}->{HookHost.HitCount}");

                // 软闸：__TestFire 直接调 Prefix（绕过 Harmony），只验证 Enabled 判断这一层。
                var returned2 = HookHost.__TestFire(afterText);
                Check("C3.Disable 后不再记录",
                    !received.Contains(afterText) && HookHost.CaptureCount == capBefore,
                    $"received={Join(received)} CaptureCount={HookHost.CaptureCount}（Disable 前 {capBefore}）");
                Check("C4.Disable 后 ref 仍未被改", returned2 == afterText, $"returned=\"{returned2}\"");
                // D. 连续 3 次 Prefix 异常自动停用（R9）
                HookHost.Enable();
                Check("D0.重新 Enable 成功", HookHost.PatchedCount >= 6, $"PatchedCount={HookHost.PatchedCount}");
                // D0b. patch 去重（R9）：已 Enabled 时再 Enable 不得重复挂。
                // 方法：真实 GUI.Label 穿过 prefix（它会抛 OnGUI 异常但 prefix 已执行），比对重复 Enable 前后的命中增量。
                // 实测：一次 GUI.Label(Rect,string) 会命中 2 次（string 重载 + 它内部转发的带 style 重载），
                // 所以断言不能写死 1，要比对两次增量是否一致。
                var d1Before = HookHost.HitCount;
                try { GUI.Label(new Rect(0, 0, 1, 1), "YG_PROBE_DEDUP_A"); } catch (Exception) { }
                var d1 = HookHost.HitCount - d1Before;

                var patchedOnce = HookHost.PatchedCount;
                HookHost.Enable();
                var patchedTwice = HookHost.PatchedCount;

                var d2Before = HookHost.HitCount;
                try { GUI.Label(new Rect(0, 0, 1, 1), "YG_PROBE_DEDUP_B"); } catch (Exception) { }
                var d2 = HookHost.HitCount - d2Before;

                Check("D0b.重复 Enable 不重复挂",
                    patchedOnce == patchedTwice && d1 > 0 && d2 == d1,
                    $"PatchedCount {patchedOnce}->{patchedTwice}；HitCount 增量 A={d1} B={d2}（B 必须等于 A，否则就是挂重了）");

                // D5（re-review Minor 4）：R9 的「同一 prefix 成功要给自己的桶清零」必须能被证伪。
                // 旧版 D2 的两次 NoteSuccess 都打在空桶上——把 NoteSuccess 改成空实现，D2/D3/D4 依然全 PASS。
                var keyC = HookHost.TestKeyStr1;
                HookHost.__TestNoteSuccess(keyC);              // 起点：桶干净
                HookHost.__TestRaiseErrors(keyC, 2);           // 桶 = 2
                var cBucket = HookHost.ConsecutiveErrors;
                HookHost.__TestNoteSuccess(keyC);              // 同 prefix 成功 → 只清自己的桶
                var cBucketAfter = HookHost.ConsecutiveErrors;
                HookHost.__TestRaiseErrors(keyC, 2);           // 若没清零就是 4（会熔断），清零了才回到 2
                var cBucketAgain = HookHost.ConsecutiveErrors;
                Check("D5.同 prefix 成功后桶清零（R9 成功清零）",
                    cBucket == 2 && cBucketAfter == 0 && cBucketAgain == 2 && HookHost.Enabled,
                    $"桶 {cBucket}->{cBucketAfter}；再失 2 次后桶={cBucketAgain} Enabled={HookHost.Enabled}");
                HookHost.__TestNoteSuccess(keyC);              // 收尾：清干净，别影响 D2 的基数

                // D2. Important 1：熔断按 prefix 分桶——别的 prefix 成功不得抹平本桶
                var keyA = HookHost.TestKeyStr1;
                var keyB = HookHost.TestKeyContent1;
                HookHost.__TestNoteSuccess(keyA);
                HookHost.__TestNoteSuccess(keyB);
                var errBase = HookHost.TotalErrors;
                HookHost.__TestRaiseErrors(keyA, 1); // A=1
                HookHost.__TestNoteSuccess(keyB); // 别的 prefix 成功（旧全局计数实现会在这里清零）
                HookHost.__TestRaiseErrors(keyB, 1); // B=1
                HookHost.__TestRaiseErrors(keyA, 1); // A=2
                HookHost.__TestRaiseErrors(keyB, 1); // B=2
                Check("D2.交错失败不熔断（分桶）",
                    HookHost.Enabled && HookHost.TotalErrors - errBase == 4 && HookHost.ConsecutiveErrors == 2,
                    $"Enabled={HookHost.Enabled} TotalErrors+={HookHost.TotalErrors - errBase} maxBucket={HookHost.ConsecutiveErrors}");

                // D3：A 桶再挨一次（累计 3）就该熔断——旧全局计数在这里只有 1，不会断。
                HookHost.__TestRaiseErrors(keyA, 1);
                // Minor 6：熔断的 Disable 已延到下一次编辑器 tick；batchmode 下 delayCall 不一定被驱动，
                // 这里显式排空，否则 D3 测的是「有没有排队」而不是「真的停用」。
                HookHost.__DrainDeferredTrip();
                Check("D3.同一 prefix 累计到 3 次自动停用",
                    !HookHost.Enabled && HookHost.PatchedCount == 0,
                    $"Enabled={HookHost.Enabled} PatchedCount={HookHost.PatchedCount}");

                // D4：字面复现 R9——重新 Enable 后同一 prefix 连失 3 次。
                HookHost.Enable();
                HookHost.__TestRaiseErrors(keyA, 3);
                HookHost.__DrainDeferredTrip();
                Check("D4.连失 3 次自动停用",
                    !HookHost.Enabled && HookHost.PatchedCount == 0,
                    $"Enabled={HookHost.Enabled} PatchedCount={HookHost.PatchedCount}");

                HookHost.Disable(); // 已停用：这一步只是让缓冲日志落盘（幂等、静默）
                HookHost.ProbeMode = false;
                HookHost.FlushProbeLog();
                // E. probe.log 落盘
                var logPath = HookHost.ProbeLogPath;
                var logText = logPath != null && File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
                var logBytes = logPath != null && File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
                Check("E1.probe.log 含探针文本", logText.Contains(testText), $"path={logPath} bytes={logBytes}");
            }
            catch (Exception ex)
            {
                ok = false;
                Debug.LogError($"[YGProbe] RunHookProbe 抛异常：{ex}");
            }
            finally
            {
                if (handler != null) HookHost.OnText -= handler;
                try { HookHost.Disable(); }
                catch { /* 收尾失败不影响结果输出 */ }
                HookHost.ProbeMode = restoreProbeMode; // Minor 3

                Debug.Log($"[YGProbe] HOOK_RESULT {(ok ? "ALL_PASS" : "HAS_FAILURE")}");
                foreach (var line in results) Debug.Log($"[YGProbe] SUMMARY {line}");

                if (Application.isBatchMode)
                {
                    Console.Out.Flush();
                    EditorApplication.Exit(ok ? 0 : 1);
                }
            }
        }
        // ---------------------------------------------------------------- 任务 4：文本管线自检

        /// <summary>
        /// R11：batchmode 下无法进入 OnGUI，这里断言纯逻辑面——ShouldTranslate 判定表、
        /// 内置词典命中、未命中回退、中文字体发现与 GUIStyle 克隆。
        /// 用 -executeMethod YGTextTranslator.DevSelfTest.RunPipelineProbe 运行，任一失败 exit 1。
        /// </summary>
        public static void RunPipelineProbe()
        {
            HookHost.__TestResetErrors();
            HookHost.Enable();

            var ok = RunPipelineProbeBody();
            if (Application.isBatchMode)
            {
                Console.Out.Flush();
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        /// <summary>同一进程连跑三次；第 3 次仍 Enabled，才证明绘制异常没有污染熔断桶。</summary>
        public static void RunPipelineProbeThrice()
        {
            HookHost.__TestResetErrors();
            HookHost.Enable();

            var all = true;
            for (var round = 1; round <= 3; round++)
            {
                Debug.Log($"[YGProbe] THRICE round {round} 开始：Enabled={HookHost.Enabled} ConsecutiveErrors={HookHost.ConsecutiveErrors}");
                var bodyPass = RunPipelineProbeBody();
                HookHost.__DrainDeferredTrip();

                var enabled = HookHost.Enabled;
                var pass = bodyPass && enabled;
                if (!pass) all = false;
                Debug.Log($"[YGProbe] {(pass ? "PASS" : "FAIL")} THRICE.{round}.第 {round} 次后仍 Enabled :: Enabled={enabled} BodyPass={bodyPass} ConsecutiveErrors={HookHost.ConsecutiveErrors}");
            }

            Debug.Log($"[YGProbe] PIPELINE_THRICE_RESULT {(all ? "ALL_PASS" : "HAS_FAILURE")}");
            if (Application.isBatchMode)
            {
                Console.Out.Flush();
                EditorApplication.Exit(all ? 0 : 1);
            }
        }

        static bool RunPipelineProbeBody()
        {
            var ok = true;
            var results = new List<string>();

            bool Check(string name, bool pass, string detail)
            {
                var line = $"[YGProbe] {(pass ? "PASS" : "FAIL")} {name} :: {detail}";
                results.Add(line);
                if (pass) Debug.Log(line);
                else
                {
                    ok = false;
                    Debug.LogError(line);
                }
                return pass;
            }

            void ExpectShouldTranslate(string text, bool expected)
            {
                var actual = TextPipeline.ShouldTranslate(text);
                var shown = text == null ? "<null>" : "\"" + text + "\"";
                Check($"ShouldTranslate({shown})=={expected.ToString().ToLowerInvariant()}",
                    actual == expected, $"actual={actual.ToString().ToLowerInvariant()}");
            }

            try
            {
                Check("P0.builtin.json 加载", TextPipeline.EntryCount >= 20 && string.IsNullOrEmpty(TextPipeline.LoadError),
                    $"EntryCount={TextPipeline.EntryCount} LoadError={TextPipeline.LoadError ?? "<null>"}");

                ExpectShouldTranslate("Enable", true);
                ExpectShouldTranslate("影の強さ", true);
                ExpectShouldTranslate("启用", false);
                ExpectShouldTranslate("123", false);
                ExpectShouldTranslate("", false);
                ExpectShouldTranslate(null, false);
                ExpectShouldTranslate("Main Color 颜色", false);

                var zh = TextPipeline.Translate("Enable");
                Check("T1.Translate(\"Enable\")==启用", string.Equals(zh, "启用", StringComparison.Ordinal),
                    $"result=\"{zh ?? "<null>"}\"");

                const string missText = "YG_PIPELINE_UNKNOWN_TOKEN_42";
                var miss = TextPipeline.Translate(missText);
                Check("T2.Translate 未命中原样返回", string.Equals(miss, missText, StringComparison.Ordinal) && !string.IsNullOrEmpty(miss),
                    $"result=\"{miss ?? "<null>"}\"");

                Check("F1.FontFixer.HasChineseFont", FontFixer.HasChineseFont, $"HasChineseFont={FontFixer.HasChineseFont}");

                var probeStyle = new GUIStyle();
                var clone = FontFixer.Clone(probeStyle, 12);
                Check("F2.FontFixer.Clone(style,12).font != null", clone != null && clone.font != null,
                    $"clone={(clone == null ? "<null>" : "ok")} font={(clone == null || clone.font == null ? "<null>" : clone.font.name)}");

                // H：HookHost 接管决策（不实际绘制，batchmode 下只能验证到这里）
                var intercepted = HookHost.__TestIntercept("Enable");
                Check("H1.HookHost 接管 Enable→启用", HookHost.Enabled && string.Equals(intercepted, "启用", StringComparison.Ordinal),
                    $"Enabled={HookHost.Enabled} result=\"{intercepted ?? "<null>"}\"");

                const string interceptMissText = "YG_PIPELINE_UNKNOWN_TOKEN_42";
                var interceptedMiss = HookHost.__TestIntercept(interceptMissText);
                Check("H2.HookHost 未命中原样返回", string.Equals(interceptedMiss, interceptMissText, StringComparison.Ordinal),
                    $"result=\"{interceptedMiss ?? "<null>"}\"");

                var interceptedChinese = HookHost.__TestIntercept("启用");
                Check("H3.HookHost 已是中文原样返回", string.Equals(interceptedChinese, "启用", StringComparison.Ordinal),
                    $"result=\"{interceptedChinese ?? "<null>"}\"");

                var previousSuppress = TextPipeline.Suppress;
                try
                {
                    TextPipeline.Suppress = true;
                    var suppressed = HookHost.__TestIntercept("Enable");
                    Check("H4.R12 Suppress 期间重入不再翻译", string.Equals(suppressed, "Enable", StringComparison.Ordinal),
                        $"result=\"{suppressed ?? "<null>"}\"");
                }
                finally
                {
                    TextPipeline.Suppress = previousSuppress;
                }

                Check("H5.R12 Suppress 已恢复", TextPipeline.Suppress == previousSuppress,
                    $"Suppress={TextPipeline.Suppress}");

                // R12：真实 GUI.Label(Rect,string) 会转发到 GUIStyle 重载。若 Suppress 没盖住自重画，
                // 译文会被再翻一次甚至递归。batchmode 下真实绘制会抛异常，但 Prefix 与命中计数已经发生。
                var hitBefore = TextPipeline.HitCount;
                try
                {
                    GUI.Label(new Rect(0f, 0f, 16f, 16f), "Enable");
                }
                catch (Exception)
                {
                    // batchmode 没有 OnGUI，绘制本身会抛；这里只断言 Prefix 侧的行为。
                }

                var hitDelta = TextPipeline.HitCount - hitBefore;
                Check("H6.R12 一次 GUI.Label 只翻译一次", hitDelta == 1, $"TranslateHitDelta={hitDelta}");
                Check("H7.R12 绘制异常后 Suppress 已复位", !TextPipeline.Suppress, $"Suppress={TextPipeline.Suppress}");

                // H8：未命中会从 string 重载转发到 GUIContent 重载；哨兵保证只记一次 miss。
                const string guiMissText = "A sample label missing from the dictionary.";
                var missBefore = TextPipeline.MissCount;
                try
                {
                    GUI.Label(new Rect(0f, 0f, 16f, 16f), guiMissText);
                }
                catch (Exception)
                {
                }

                var missDelta = TextPipeline.MissCount - missBefore;
                Check("H8.R12 miss 一次 GUI.Label 只记一次", missDelta == 1, $"TranslateMissDelta={missDelta}");

                const string boxMissText = "A sample description missing from the dictionary.";
                var boxMissBefore = TextPipeline.MissCount;
                try
                {
                    GUI.Box(new Rect(0f, 0f, 16f, 16f), boxMissText);
                }
                catch (Exception)
                {
                }

                var boxMissDelta = TextPipeline.MissCount - boxMissBefore;
                Check("H9.R12 Box miss 一次 GUI.Box 只记一次", boxMissDelta == 1, $"TranslateMissDelta={boxMissDelta}");

                HookHost.__DrainDeferredTrip();
                Check("H10.连续绘制后 HookHost 仍 Enabled", HookHost.Enabled,
                    $"Enabled={HookHost.Enabled} ConsecutiveErrors={HookHost.ConsecutiveErrors}");

                TextPipeline.Reload();
                Check("H11.TextPipeline.Reload 后可重新加载", TextPipeline.EntryCount >= 20 && string.IsNullOrEmpty(TextPipeline.LoadError)
                    && string.Equals(TextPipeline.Translate("Enable"), "启用", StringComparison.Ordinal),
                    $"EntryCount={TextPipeline.EntryCount} LoadError={TextPipeline.LoadError ?? "<null>"}");

                var parseOk = TextPipeline.__TestParseBuiltin(
                    "{\"version\":1,\"entries\":[{\"k\":\"A\",\"v\":\"\\u12\"},{\"k\":\"B\",\"v\":\"B\"}]}",
                    "A", out var firstValue, out var parsedCount, out var parseError);
                Check("H12.非法 \\u 转义不打断解析", parseOk && parsedCount == 2
                    && string.Equals(firstValue, "\\u12", StringComparison.Ordinal),
                    $"parsed={parseOk} count={parsedCount} value=\"{firstValue ?? "<null>"}\" error={parseError ?? "<null>"}");
            }
            catch (Exception ex)
            {
                ok = false;
                Debug.LogError($"[YGProbe] RunPipelineProbe 抛异常：{ex}");
            }
            finally
            {
                Debug.Log($"[YGProbe] PIPELINE_RESULT {(ok ? "ALL_PASS" : "HAS_FAILURE")}");
                foreach (var line in results) Debug.Log($"[YGProbe] SUMMARY {line}");
            }

            return ok;
        }

        static string Join(List<string> items)
        {
            return items.Count == 0 ? "<空>" : "[" + string.Join(", ", items) + "]";
        }

        // 调用一次 GUI.Label：batchmode 下它可能因"只能在 OnGUI 里调"抛异常，异常类型本身不重要，
        // 关键是 prefix 有没有被触发。
        static void InvokeLabel()
        {
            try
            {
                GUI.Label(new Rect(0, 0, 1, 1), "probe");
                Debug.Log("[YGProbe] GUI.Label 调用完成（无异常）");
            }
            catch (Exception ex)
            {
                Debug.Log($"[YGProbe] GUI.Label 调用抛出 {ex.GetType().Name}: {ex.Message}");
            }
        }

        static string Describe(MethodInfo method)
        {
            return method == null
                ? "<null>"
                : $"{method.DeclaringType?.FullName}.{method.Name}({method.GetParameters().Length} args)";
        }

        // Harmony prefix 允许完全无参；这里只需要证明 patch 能挂上并被调用。
        static void LabelPrefix()
        {
            Debug.Log("[YGProbe] LabelPrefix fired");
        }
    }
}
