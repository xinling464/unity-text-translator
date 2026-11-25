using System;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace YGTextTranslator
{
    /// <summary>
    /// 在线翻译链路自检：真的发一次请求，等译文回落，验证「未命中 → 入队 → 后台翻 → 缓存命中」全通。
    /// 只在 batchmode / 手动调用时跑，日常编辑器不会触发。
    /// </summary>
    internal static class OnlineSelfTest
    {
        const string Sample = "Enable this feature to merge the outfit.";
        const string SampleJa = "影の強さ";

        public static void Run()
        {
            Environment.Exit(RunInner());
        }

        static int RunInner()
        {
            Debug.Log("[YGOnline] START backend=" + TranslationApi.CurrentBackend);
            TranslationApi.Enabled = true;

            var first = TranslationApi.Lookup(Sample, "en");
            Debug.Log("[YGOnline] first lookup => " + (first ?? "<null, queued>"));

            var deadline = DateTime.UtcNow.AddSeconds(25);
            string en = null;
            string ja = null;
            TranslationApi.Lookup(SampleJa, "ja");

            while (DateTime.UtcNow < deadline)
            {
                TranslationApi.Drain();
                if (en == null) en = TranslationApi.Lookup(Sample, "en");
                if (ja == null) ja = TranslationApi.Lookup(SampleJa, "ja");
                if (en != null && ja != null) break;
                Thread.Sleep(250);
            }

            Debug.Log("[YGOnline] en => " + (en ?? "<FAILED>"));
            Debug.Log("[YGOnline] ja => " + (ja ?? "<FAILED>"));
            Debug.Log("[YGOnline] cached=" + TranslationApi.CachedCount + " failed=" + TranslationApi.FailedCount);

            if (en == null) { Debug.Log("[YGOnline] RESULT FAIL_EN"); return 1; }
            if (ja == null) { Debug.Log("[YGOnline] RESULT FAIL_JA"); return 1; }

            Debug.Log("[YGOnline] RESULT ALL_PASS");
            return 0;
        }
    }
}
