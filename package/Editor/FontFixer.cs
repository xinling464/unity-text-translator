using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace YGTextTranslator
{
    /// <summary>
    /// 把插件自绘 UI 的字体换成带中文字形的系统字体，并缓存克隆后的 GUIStyle。
    /// 只在第一次使用时做一次字体发现；Clone 之后走 (源样式, 字号) 缓存，避免每帧 new。
    /// </summary>
    internal static class FontFixer
    {
        static readonly string[] PreferredFonts =
        {
            "Microsoft YaHei",
            "Microsoft YaHei UI",
            "微软雅黑",
            "SimHei",
            "SimSun",
            "PingFang SC",
            "Heiti SC",
            "Noto Sans CJK SC",
            "Source Han Sans SC"
        };

        const int DefaultSize = 12;
        const int MaxSourceStyles = 256;

        static readonly Dictionary<int, Font> FontsBySize = new Dictionary<int, Font>();
        static readonly Dictionary<GUIStyle, Dictionary<int, GUIStyle>> ClonesByStyle =
            new Dictionary<GUIStyle, Dictionary<int, GUIStyle>>(StyleRefComparer.Instance);

        static bool _resolved;
        static bool _hasChineseFont;
        static string _fontName;

        public static bool HasChineseFont
        {
            get
            {
                EnsureResolved();
                return _hasChineseFont;
            }
        }

        internal static string ResolvedFontName
        {
            get
            {
                EnsureResolved();
                return _fontName;
            }
        }

        internal static void Warmup()
        {
            EnsureResolved();
        }

        internal static GUIStyle ForTranslation(GUIStyle style, string text)
        {
            if (style == null || !HasChineseFont) return style;

            var current = style.font ?? (Event.current != null ? GUI.skin.font : null);
            foreach (var character in text)
            {
                if (character < '\u3400' || character > '\u9fff') continue;
                if (current == null || !current.HasCharacter(character))
                {
                    var size = style.fontSize > 0 ? style.fontSize : current?.fontSize ?? DefaultSize;
                    return Clone(style, size);
                }
            }
            return style;
        }

        public static GUIStyle Clone(GUIStyle src, int size)
        {
            var effectiveSize = size > 0 ? size : DefaultSize;
            EnsureResolved();

            if (src == null)
            {
                return new GUIStyle
                {
                    font = GetFont(effectiveSize),
                    fontSize = effectiveSize
                };
            }

            Dictionary<int, GUIStyle> bySize;
            if (!ClonesByStyle.TryGetValue(src, out bySize))
            {
                if (ClonesByStyle.Count >= MaxSourceStyles) ClonesByStyle.Clear();
                bySize = new Dictionary<int, GUIStyle>();
                ClonesByStyle[src] = bySize;
            }

            GUIStyle clone;
            if (bySize.TryGetValue(effectiveSize, out clone) && clone != null) return clone;

            clone = new GUIStyle(src) { font = GetFont(effectiveSize) };
            if (clone.fontSize <= 0) clone.fontSize = effectiveSize;
            bySize[effectiveSize] = clone;
            return clone;
        }

        internal static Font GetFont(int size)
        {
            EnsureResolved();
            var effectiveSize = size > 0 ? size : DefaultSize;

            Font font;
            if (FontsBySize.TryGetValue(effectiveSize, out font) && font != null) return font;

            font = TryCreateFont(_fontName, effectiveSize);
            if (font == null && !string.IsNullOrEmpty(_fontName)) font = TryCreateFont("Arial", effectiveSize);
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch { font = null; }
            }

            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch { font = null; }
            }

            if (font == null)
            {
                try { font = GUI.skin != null ? GUI.skin.font : null; }
                catch { font = null; }
            }

            FontsBySize[effectiveSize] = font;
            return font;
        }

        static Font TryCreateFont(string name, int size)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try { return Font.CreateDynamicFontFromOSFont(name, size); }
            catch { return null; }
        }

        static void EnsureResolved()
        {
            if (_resolved) return;
            _resolved = true;

            string[] installed = null;
            try { installed = Font.GetOSInstalledFontNames(); }
            catch { installed = null; }

            if (installed != null && installed.Length > 0)
            {
                for (var c = 0; c < PreferredFonts.Length; c++)
                {
                    var candidate = PreferredFonts[c];
                    for (var i = 0; i < installed.Length; i++)
                    {
                        if (string.Equals(installed[i], candidate, StringComparison.OrdinalIgnoreCase))
                        {
                            _fontName = installed[i];
                            _hasChineseFont = true;
                            return;
                        }
                    }
                }

                // 系统可能只报告本地化名称；退一步做一次真实创建探测。
                for (var c = 0; c < PreferredFonts.Length; c++)
                {
                    var probe = TryCreateFont(PreferredFonts[c], DefaultSize);
                    if (SupportsChinese(probe))
                    {
                        _fontName = PreferredFonts[c];
                        _hasChineseFont = true;
                        return;
                    }
                }

                _fontName = null;
                _hasChineseFont = false;
                return;
            }

            // 拿不到安装列表时（极少数 batchmode / 无字体服务环境）做创建探测。
            for (var c = 0; c < PreferredFonts.Length; c++)
            {
                var probe = TryCreateFont(PreferredFonts[c], DefaultSize);
                if (SupportsChinese(probe))
                {
                    _fontName = PreferredFonts[c];
                    _hasChineseFont = true;
                    return;
                }
            }

            _fontName = null;
            _hasChineseFont = false;
        }

        static bool SupportsChinese(Font font)
        {
            if (font == null) return false;
            var supported = font.HasCharacter('中');
            UnityEngine.Object.DestroyImmediate(font);
            return supported;
        }

        sealed class StyleRefComparer : IEqualityComparer<GUIStyle>
        {
            public static readonly StyleRefComparer Instance = new StyleRefComparer();

            public bool Equals(GUIStyle x, GUIStyle y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(GUIStyle obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
