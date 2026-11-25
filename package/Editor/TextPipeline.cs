using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace YGTextTranslator
{
    internal static class TextPipeline
    {
        const int EligibilityCapacity = 4096;
        static readonly Regex Identifier = new Regex(@"^[\p{L}\p{N}]+(?:[_.][\p{L}\p{N}]+)+$|^[a-z][A-Za-z0-9]*[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);
        static readonly Regex PathPattern = new Regex(
            @"(?:\b(?:Assets|Packages|Library|ProjectSettings)[/\\]|[A-Za-z]:[\\/]|\\\\[^\\]|(?:\bhttps?|file|vpm)://|^\s*(?:[/\\~]|\.{1,2}[/\\])|(?:^|\s)[^ ]+\.(?:asset|prefab|unity|mat|shader|controller|anim|cs|dll|json|uxml|uss|fbx|png|texture|zip)(?:$|[\s,;]))",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex FormatMarker = new Regex(
            @"</?[a-z][^<>]*>|\{\{|\}\}|\{[0-9A-Za-z_][^{}\r\n]*\}|%\w|\b(?:Ctrl|Alt|Shift|Cmd)(?:\s*\+\s*(?:Ctrl|Alt|Shift|Cmd))*\s*\+\s*[A-Za-z0-9]+",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Dictionary<string, bool> Eligibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        static Dictionary<string, string> _builtin;
        static string _loadError;
        static int _hitCount;
        static int _missCount;

        internal static bool Suppress { get; set; }
        internal static int HitCount => Volatile.Read(ref _hitCount);
        internal static int MissCount => Volatile.Read(ref _missCount);
        internal static int EntryCount { get { EnsureLoaded(); return _builtin.Count; } }
        internal static string LoadError { get { EnsureLoaded(); return _loadError; } }

        internal static void EnsureLoaded()
        {
            if (_builtin != null) return;
            _builtin = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var root = TranslatorInstallation.FindRoot();
                if (root == null) throw new IOException("package root not found");
                var path = Path.Combine(root, "lexicons", "builtin.json");
                if (!BuiltinJson.TryParseEntries(File.ReadAllText(path, Encoding.UTF8), out var parsed, out _loadError))
                    throw new FormatException(_loadError);
                _builtin = parsed;
            }
            catch (Exception ex)
            {
                _loadError = ex.GetType().Name + ": " + ex.Message;
                Debug.LogWarning("[YGTextTranslator] 内置词典加载失败：" + _loadError);
            }
        }

        internal static bool ShouldTranslate(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 900) return false;
            if (Eligibility.TryGetValue(text, out var result)) return result;
            result = IsDisplayPhrase(FormatMarker.Replace(text, ""));
            if (Eligibility.Count >= EligibilityCapacity) Eligibility.Clear();
            Eligibility[text] = result;
            return result;
        }

        static bool IsDisplayPhrase(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || LooksLikeIdentifier(text)) return false;
            if (DetectLang(text) == "ja") return true;
            var hasLetter = false;
            foreach (var character in text)
            {
                if ((character >= '\u3400' && character <= '\u9fff') || (character >= '\uf900' && character <= '\ufaff'))
                    return false;
                if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z'))
                    hasLetter = true;
            }
            return hasLetter;
        }

        internal static bool LooksLikeIdentifier(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var trimmed = text.Trim();
            return trimmed.EndsWith("(Clone)", StringComparison.Ordinal)
                || Identifier.IsMatch(trimmed)
                || PathPattern.IsMatch(trimmed)
                || (!trimmed.Any(char.IsWhiteSpace) && (trimmed.Contains("/") || trimmed.Contains("\\")));
        }

        internal static string DetectLang(string text)
        {
            if (text != null)
            {
                foreach (var character in text)
                {
                    if ((character >= '\u3041' && character <= '\u30fa') || (character >= '\uff66' && character <= '\uff9f'))
                        return "ja";
                }
            }
            return "en";
        }

        internal static string Translate(string text) => TranslateDisplay(text);

        internal static string TranslateDisplay(string text, string packageId = null)
        {
            if (string.IsNullOrWhiteSpace(text) || Suppress || TranslatorPreferences.ShowOriginal
                || !TranslatorPreferences.IsPackageEnabled(packageId)) return text;

            EnsureLoaded();
            var source = text.Trim();
            if (TranslationGlossary.TryGet(packageId, source, out var entry))
                return entry.ignored ? text : Translated(text, source, entry.translation);

            if (_builtin.TryGetValue(source, out var builtin))
                return Translated(text, source, builtin);

            if (ShouldTranslate(source))
            {
                var online = TranslationApi.Lookup(source, DetectLang(source), packageId);
                TranslationGlossary.Note(packageId, source, online);
                if (online != null) return Translated(text, source, online);
            }

            Interlocked.Increment(ref _missCount);
            return text;
        }

        static string Translated(string original, string source, string result)
        {
            if (string.IsNullOrEmpty(result) || result == source || !PreservesMarkers(source, result)) return original;
            Interlocked.Increment(ref _hitCount);
            if (original.Length == source.Length) return result;
            var start = original.IndexOf(source, StringComparison.Ordinal);
            return original.Substring(0, start) + result + original.Substring(start + source.Length);
        }

        internal static bool PreservesMarkers(string source, string translation)
        {
            var before = FormatMarker.Match(source);
            var after = FormatMarker.Match(translation);
            while (before.Success && after.Success)
            {
                if (!before.Value.Equals(after.Value, StringComparison.Ordinal)) return false;
                before = before.NextMatch();
                after = after.NextMatch();
            }
            return !before.Success && !after.Success;
        }

        internal static string ProtectMarkers(string source, out string[] markers)
        {
            var found = new List<string>();
            var protectedText = FormatMarker.Replace(source, match =>
            {
                var token = "[[YG" + found.Count + "]]";
                found.Add(match.Value);
                return token;
            });
            markers = found.ToArray();
            return protectedText;
        }

        internal static string RestoreMarkers(string source, string translation, string[] markers)
        {
            if (translation == null) return null;
            for (var index = 0; index < markers.Length; index++)
            {
                var token = "[[YG" + index + "]]";
                var start = translation.IndexOf(token, StringComparison.Ordinal);
                if (start < 0 || translation.IndexOf(token, start + token.Length, StringComparison.Ordinal) >= 0) return null;
                translation = translation.Replace(token, markers[index]);
            }
            return PreservesMarkers(source, translation) ? translation : null;
        }

        internal static void Reload()
        {
            _builtin = null;
            Eligibility.Clear();
            EnsureLoaded();
        }

        internal static bool __TestParseBuiltin(string json, string key, out string value, out int count, out string error)
        {
            value = null;
            count = 0;
            if (!BuiltinJson.TryParseEntries(json, out var parsed, out error)) return false;
            count = parsed.Count;
            return parsed.TryGetValue(key, out value);
        }

        static class BuiltinJson
        {
            public static bool TryParseEntries(string json, out Dictionary<string, string> entries, out string error)
            {
                entries = new Dictionary<string, string>(StringComparer.Ordinal);
                error = null;

                try
                {
                    if (!string.IsNullOrEmpty(json) && json[0] == '\uFEFF') json = json.Substring(1);

                    var p = new Parser(json);
                    p.Expect('{');

                    var foundEntries = false;
                    while (true)
                    {
                        p.SkipWhitespace();
                        if (p.TryConsume('}')) break;

                        var name = p.ReadString();
                        p.Expect(':');

                        if (name == "entries")
                        {
                            foundEntries = true;
                            p.Expect('[');

                            while (true)
                            {
                                p.SkipWhitespace();
                                if (p.TryConsume(']')) break;

                                p.Expect('{');
                                string k = null;
                                string v = null;

                                while (true)
                                {
                                    p.SkipWhitespace();
                                    if (p.TryConsume('}')) break;

                                    var prop = p.ReadString();
                                    p.Expect(':');

                                    if (prop == "k") k = p.ReadString();
                                    else if (prop == "v") v = p.ReadString();
                                    else p.SkipValue();

                                    p.SkipWhitespace();
                                    if (p.TryConsume(',')) continue;

                                    p.Expect('}');
                                    break;
                                }

                                if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v)) entries[k] = v;

                                p.SkipWhitespace();
                                if (p.TryConsume(',')) continue;

                                p.Expect(']');
                                break;
                            }
                        }
                        else
                        {
                            p.SkipValue();
                        }

                        p.SkipWhitespace();
                        if (p.TryConsume(',')) continue;

                        p.Expect('}');
                        break;
                    }

                    if (!foundEntries)
                    {
                        error = "missing \"entries\" array";
                        return false;
                    }

                    if (p.Warnings.Count > 0)
                    {
                        foreach (var warning in p.Warnings)
                            Debug.LogWarning("[YGTextTranslator] builtin.json 解析警告：" + warning);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    error = "JSON: " + ex.Message;
                    return false;
                }
            }

            sealed class Parser
            {
                readonly string _s;
                int _i;
                public readonly List<string> Warnings = new List<string>();

                public Parser(string s)
                {
                    _s = s ?? string.Empty;
                }

                public void SkipWhitespace()
                {
                    while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
                }

                public bool TryConsume(char c)
                {
                    SkipWhitespace();
                    if (_i < _s.Length && _s[_i] == c)
                    {
                        _i++;
                        return true;
                    }

                    return false;
                }

                public void Expect(char c)
                {
                    SkipWhitespace();
                    if (_i >= _s.Length || _s[_i] != c)
                        throw new FormatException($"expected '{c}' at {_i}");
                    _i++;
                }

                public string ReadString()
                {
                    SkipWhitespace();
                    if (_i >= _s.Length || _s[_i] != '"')
                        throw new FormatException($"expected '\"' at {_i}");

                    _i++;
                    var sb = new StringBuilder();

                    while (_i < _s.Length)
                    {
                        var c = _s[_i++];
                        if (c == '"') return sb.ToString();
                        if (c != '\\')
                        {
                            sb.Append(c);
                            continue;
                        }

                        if (_i >= _s.Length) throw new FormatException("unterminated escape");
                        var e = _s[_i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                            {
                                var start = _i;
                                var valid = _i + 4 <= _s.Length;
                                var code = 0;
                                if (valid)
                                {
                                    for (var k = 0; k < 4; k++)
                                    {
                                        var digit = HexValue(_s[_i + k]);
                                        if (digit < 0) { valid = false; break; }
                                        code = (code << 4) | digit;
                                    }
                                }

                                if (!valid)
                                {
                                    // 坏词条（如 \u12）只记一条 warning，按字面保留，不打断整本词典。
                                    Warnings.Add($"非法 \\u 转义 at {start - 2}，按字面保留");
                                    sb.Append('\\').Append('u');
                                }
                                else
                                {
                                    sb.Append((char)code);
                                    _i += 4;
                                }
                                break;
                            }
                            default:
                                Warnings.Add($"非法转义 '\\{e}' at {_i - 2}，按字面保留");
                                sb.Append('\\').Append(e);
                                break;
                        }
                    }

                    throw new FormatException("unterminated string");
                }

                static int HexValue(char c)
                {
                    if (c >= '0' && c <= '9') return c - '0';
                    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                    return -1;
                }

                public void SkipValue()
                {
                    SkipWhitespace();
                    if (_i >= _s.Length) throw new FormatException("unexpected end");

                    var c = _s[_i];
                    if (c == '"')
                    {
                        ReadString();
                        return;
                    }

                    if (c == '{')
                    {
                        _i++;
                        while (true)
                        {
                            SkipWhitespace();
                            if (TryConsume('}')) return;
                            ReadString();
                            Expect(':');
                            SkipValue();
                            SkipWhitespace();
                            if (TryConsume(',')) continue;
                            Expect('}');
                            return;
                        }
                    }

                    if (c == '[')
                    {
                        _i++;
                        while (true)
                        {
                            SkipWhitespace();
                            if (TryConsume(']')) return;
                            SkipValue();
                            SkipWhitespace();
                            if (TryConsume(',')) continue;
                            Expect(']');
                            return;
                        }
                    }

                    while (_i < _s.Length)
                    {
                        var d = _s[_i];
                        if (d == ',' || d == '}' || d == ']' || char.IsWhiteSpace(d)) return;
                        _i++;
                    }
                }
            }
        }
    }
}
