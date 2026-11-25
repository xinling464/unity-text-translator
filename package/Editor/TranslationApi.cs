using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YGTextTranslator
{
    internal static class TranslationApi
    {
        internal enum Backend { MyMemory, Youdao, Deepl, Baidu, OpenAICompat }

        const int QueueLimit = 4000;
        const string Pref = TranslatorPreferences.Prefix + "online.";
        static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        static readonly object WorkerGate = new object();
        static readonly Queue<Request> Requests = new Queue<Request>();
        static readonly Queue<Result> Completed = new Queue<Result>();
        static readonly HashSet<string> Scheduled = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, DateTime> Cooldowns = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        static CancellationTokenSource _generation = new CancellationTokenSource();
        static bool _workerRunning;
        static bool _loaded;
        static bool _dirty;
        static DateTime _providerCooldown;
        static string EnabledKey => Pref + TranslatorPreferences.ProjectId + ".enabled";
        static bool _enabled = EditorPrefs.GetBool(EnabledKey, false);
        static Backend _backend = ReadBackend();
        static Configuration _configuration;

        internal static string LastError { get; private set; } = "";
        internal static int CachedCount { get { EnsureLoaded(); return Cache.Count; } }
        internal static int FailedCount => Cooldowns.Count + (_providerCooldown > DateTime.UtcNow ? 1 : 0);
        internal static int QueuedCount { get { lock (WorkerGate) return Scheduled.Count; } }

        internal static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                EditorPrefs.SetBool(EnabledKey, value);
                _enabled = value;
                ReloadConfiguration();
            }
        }

        internal static Backend CurrentBackend
        {
            get => _backend;
            set
            {
                if (_backend == value) return;
                EditorPrefs.SetInt(Pref + "backend", (int)value);
                _backend = value;
                ReloadConfiguration();
            }
        }

        internal static string ApiKey { get => Get("key"); set => Set("key", value); }
        internal static string ApiSecret { get => Get("secret"); set => Set("secret", value); }
        internal static string ApiEndpoint { get => Get("endpoint"); set => Set("endpoint", value); }
        internal static string ApiModel { get => Get("model", "gpt-4o-mini"); set => Set("model", value); }

        static string CachePath => Path.Combine(TranslatorPreferences.ProjectDataDir, "online-cache.json");

        internal static string Lookup(string source, string language, string packageId = null)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Length > 900) return null;
            EnsureLoaded();
            var configuration = _configuration ?? (_configuration = ReadConfiguration());
            var originalKey = language + "|" + source;
            var context = configuration.Backend == Backend.OpenAICompat ? packageId ?? "" : "";
            var key = configuration.CacheNamespace + "|" + context + "|" + originalKey;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            if (!_enabled || !TranslatorPreferences.RuntimeEnabled || DateTime.UtcNow < _providerCooldown)
                return null;
            if (Cooldowns.TryGetValue(key, out var until))
            {
                if (DateTime.UtcNow < until) return null;
                Cooldowns.Remove(key);
            }

            lock (WorkerGate)
            {
                if (Scheduled.Count >= QueueLimit || !Scheduled.Add(key)) return null;
                Requests.Enqueue(new Request(key, source, language, context, configuration, _generation.Token));
                if (!_workerRunning)
                {
                    _workerRunning = true;
                    Task.Run(WorkAsync);
                }
            }
            return null;
        }

        internal static int Drain()
        {
            List<Result> done;
            lock (WorkerGate)
            {
                if (Completed.Count == 0) return 0;
                done = new List<Result>(Completed.Count);
                while (Completed.Count > 0)
                {
                    var result = Completed.Dequeue();
                    Scheduled.Remove(result.Request.Key);
                    done.Add(result);
                }
            }

            foreach (var result in done)
            {
                if (result.Error == null)
                {
                    Cache[result.Request.Key] = result.Translation;
                    _dirty = true;
                    LastError = "";
                    continue;
                }

                LastError = result.Error.Message;
                if (result.Error.ProviderWide)
                {
                    _providerCooldown = DateTime.UtcNow + result.Error.RetryDelay;
                    CancelQueued();
                }
                else
                {
                    if (Cooldowns.Count >= QueueLimit) Cooldowns.Clear();
                    Cooldowns[result.Request.Key] = DateTime.UtcNow + result.Error.RetryDelay;
                }
            }
            return done.Count(result => result.Error == null);
        }

        internal static void RetryFailures()
        {
            Cooldowns.Clear();
            _providerCooldown = default;
            LastError = "";
            OnlineTranslator.RequestRefresh();
        }

        internal static void CancelQueued()
        {
            lock (WorkerGate)
            {
                _generation.Cancel();
                _generation.Dispose();
                _generation = new CancellationTokenSource();
                Requests.Clear();
                Completed.Clear();
                Scheduled.Clear();
            }
        }

        internal static void ClearCache()
        {
            EnsureLoaded();
            CancelQueued();
            Cache.Clear();
            _dirty = true;
            Flush();
            RetryFailures();
        }

        internal static void Flush()
        {
            if (!_dirty) return;
            try
            {
                var records = Cache.Select(pair => new CacheRecord { key = pair.Key, value = pair.Value }).ToArray();
                TranslatorPreferences.WriteAtomic(CachePath, JsonUtility.ToJson(new CacheFile { entries = records }));
                _dirty = false;
            }
            catch (Exception ex)
            {
                CatalogPatcher.Log("CACHE_SAVE_ERR " + ex.GetType().Name);
            }
        }

        internal static void SaveConfiguration(string key, string secret, string endpoint, string model)
        {
            var provider = Pref + _backend + ".";
            EditorPrefs.SetString(provider + "key", key ?? "");
            EditorPrefs.SetString(provider + "secret", secret ?? "");
            EditorPrefs.SetString(provider + "endpoint", endpoint ?? "");
            EditorPrefs.SetString(provider + "model", model ?? "");
            ReloadConfiguration();
        }

        internal static async Task<string> TestConnectionAsync()
        {
            var config = ReadConfiguration();
            try
            {
                var result = await TranslateAsync("Enable the preview.", "en", "", config, CancellationToken.None).ConfigureAwait(false);
                return "连接成功 · " + result;
            }
            catch (TranslationFailure failure) { return failure.Message; }
            catch (OperationCanceledException) { return "请求超时，请检查网络后重试。"; }
            catch (HttpRequestException) { return "无法连接翻译服务，请检查网络和接口地址。"; }
            catch (Exception) { return "翻译服务返回了无法读取的响应。"; }
        }

        static async Task WorkAsync()
        {
            var nextRequest = DateTime.UtcNow;
            while (true)
            {
                Request request;
                lock (WorkerGate)
                {
                    if (Requests.Count == 0)
                    {
                        _workerRunning = false;
                        return;
                    }
                    request = Requests.Dequeue();
                }

                TranslationFailure failure = null;
                string translated = null;
                try
                {
                    var remaining = nextRequest - DateTime.UtcNow;
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining, request.Token).ConfigureAwait(false);
                    var spacing = request.Configuration.Backend == Backend.Baidu ? 1050 : 450;
                    nextRequest = DateTime.UtcNow.AddMilliseconds(spacing);
                    translated = await TranslateAsync(request.Source, request.Language, request.PackageId, request.Configuration, request.Token)
                        .ConfigureAwait(false);
                }
                catch (TranslationFailure error) { failure = error; }
                catch (OperationCanceledException) { failure = new TranslationFailure("翻译请求超时。", true, 20); }
                catch (HttpRequestException) { failure = new TranslationFailure("无法连接翻译服务。", true, 30); }
                catch (Exception) { failure = new TranslationFailure("翻译服务返回了无法读取的响应。"); }

                lock (WorkerGate)
                    if (!request.Token.IsCancellationRequested)
                        Completed.Enqueue(new Result(request, translated, failure));
            }
        }

        static async Task<string> TranslateAsync(string source, string language, string packageId, Configuration config, CancellationToken token)
        {
            var protectedSource = TextPipeline.ProtectMarkers(source, out var markers);
            string received;
            switch (config.Backend)
            {
                case Backend.Deepl: received = await Deepl(protectedSource, language, config, token).ConfigureAwait(false); break;
                case Backend.Baidu: received = await Baidu(protectedSource, language, config, token).ConfigureAwait(false); break;
                case Backend.Youdao: received = await Youdao(protectedSource, language, token).ConfigureAwait(false); break;
                case Backend.OpenAICompat: received = await OpenAi(protectedSource, packageId, config, token).ConfigureAwait(false); break;
                default: received = await MyMemory(protectedSource, language, token).ConfigureAwait(false); break;
            }

            var result = TextPipeline.RestoreMarkers(source, received?.Trim(), markers);
            if (string.IsNullOrWhiteSpace(result) || string.Equals(source, result, StringComparison.OrdinalIgnoreCase))
                throw new TranslationFailure("未获得可用译文，已保留原文。");
            return result;
        }

        static async Task<string> MyMemory(string text, string language, CancellationToken token)
        {
            var segments = SplitUtf8(text, 500);
            var translated = new StringBuilder();
            for (var index = 0; index < segments.Count; index++)
            {
                if (index > 0) await Task.Delay(450, token).ConfigureAwait(false);
                var segment = segments[index];
                using (var request = new HttpRequestMessage(HttpMethod.Get,
                    "https://api.mymemory.translated.net/get?q=" + Escape(segment.Trim()) + "&langpair=" + Escape(language + "|zh-CN")))
                {
                    var response = await Send<MyMemoryResponse>(request, token).ConfigureAwait(false);
                    if (response.quotaFinished || response.responseStatus == 429)
                        throw new TranslationFailure("MyMemory 暂时达到翻译额度。", true, 300);
                    var value = response.responseData?.translatedText;
                    if (response.responseStatus != 200 || string.IsNullOrWhiteSpace(value))
                        throw new TranslationFailure("MyMemory 没有返回可用译文。");
                    translated.Append(System.Net.WebUtility.HtmlDecode(value));
                    var trimmed = segment.TrimEnd();
                    translated.Append(segment, trimmed.Length, segment.Length - trimmed.Length);
                }
            }
            return translated.ToString();
        }

        static async Task<string> Youdao(string text, string language, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get,
                "https://aidemo.youdao.com/trans?q=" + Escape(text) + "&from=" + Escape(language) + "&to=zh-CHS"))
            {
                var response = await Send<YoudaoResponse>(request, token).ConfigureAwait(false);
                return response.translation?.FirstOrDefault();
            }
        }

        static async Task<string> Deepl(string text, string language, Configuration config, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(config.Key))
                throw new TranslationFailure("请填写 DeepL AuthKey。", true, 300);

            var host = config.Key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase) ? "api-free.deepl.com" : "api.deepl.com";
            using (var request = JsonRequest("https://" + host + "/v2/translate", new DeeplRequest
            {
                text = new[] { text },
                source_lang = language.ToUpperInvariant(),
                target_lang = "ZH-HANS"
            }))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", config.Key);
                var response = await Send<DeeplResponse>(request, token).ConfigureAwait(false);
                return response.translations?.FirstOrDefault()?.text;
            }
        }

        static async Task<string> Baidu(string text, string language, Configuration config, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(config.Key) || string.IsNullOrWhiteSpace(config.Secret))
                throw new TranslationFailure("请填写百度翻译 AppID 和密钥。", true, 300);

            var salt = Guid.NewGuid().ToString("N");
            string sign;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                sign = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(config.Key + text + salt + config.Secret)))
                    .Replace("-", "").ToLowerInvariant();

            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://fanyi-api.baidu.com/api/trans/vip/translate"))
            {
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["q"] = text, ["from"] = language == "ja" ? "jp" : language, ["to"] = "zh",
                    ["appid"] = config.Key, ["salt"] = salt, ["sign"] = sign
                });
                var response = await Send<BaiduResponse>(request, token).ConfigureAwait(false);
                switch (response.error_code)
                {
                    case "54001":
                    case "52003": throw new TranslationFailure("百度翻译鉴权失败，请检查配置。", true, 300);
                    case "54003":
                    case "54004": throw new TranslationFailure("百度翻译达到请求频率或额度限制。", true, 60);
                    default: return response.trans_result?.FirstOrDefault()?.dst;
                }
            }
        }

        static async Task<string> OpenAi(string text, string packageId, Configuration config, CancellationToken token)
        {
            if (!Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var endpoint)
                || !string.IsNullOrEmpty(endpoint.UserInfo)
                || (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)))
                throw new TranslationFailure("请填写 HTTPS 接口地址或本机 HTTP 地址。", true, 300);
            if (string.IsNullOrWhiteSpace(config.Model))
                throw new TranslationFailure("请填写兼容接口的模型名称。", true, 300);

            using (var request = JsonRequest(endpoint.AbsoluteUri, new ChatRequest
            {
                model = config.Model,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = "将 Unity 编辑器标签翻译成简体中文，只输出译文。保留专名、换行以及形如 [[YG0]] 的标记。包：" + packageId },
                    new ChatMessage { role = "user", content = text }
                }
            }))
            {
                if (!string.IsNullOrWhiteSpace(config.Key))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Key);
                var response = await Send<ChatResponse>(request, token).ConfigureAwait(false);
                return response.choices?.FirstOrDefault()?.message?.content;
            }
        }

        static HttpRequestMessage JsonRequest(string url, object data)
        {
            return new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonUtility.ToJson(data), Encoding.UTF8, "application/json")
            };
        }

        static async Task<T> Send<T>(HttpRequestMessage request, CancellationToken token) where T : class
        {
            using (var response = await Http.SendAsync(request, token).ConfigureAwait(false))
            {
                var status = (int)response.StatusCode;
                if (status == 401 || status == 403)
                    throw new TranslationFailure("翻译服务拒绝鉴权，请检查密钥或权限。", true, 300);
                if (status == 429 || status == 456)
                    throw new TranslationFailure("翻译服务达到请求频率或额度限制。", true, 60);
                if (status >= 500)
                    throw new TranslationFailure("翻译服务暂时不可用。", true, 30);
                if (!response.IsSuccessStatusCode)
                    throw new TranslationFailure("翻译服务拒绝请求（HTTP " + status + "）。");
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonUtility.FromJson<T>(body) ?? throw new TranslationFailure("翻译服务返回了空响应。");
            }
        }

        static List<string> SplitUtf8(string text, int limit)
        {
            var chunks = new List<string>();
            var start = 0;
            while (start < text.Length)
            {
                var index = start;
                var boundary = start;
                var used = 0;
                while (index < text.Length)
                {
                    var size = char.IsHighSurrogate(text[index]) && index + 1 < text.Length
                        && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
                    var character = text[index];
                    var bytes = size == 2 ? 4 : character <= 0x7f ? 1 : character <= 0x7ff ? 2 : 3;
                    if (used + bytes > limit) break;
                    used += bytes;
                    if (char.IsWhiteSpace(text[index])) boundary = index + size;
                    index += size;
                }
                var end = index < text.Length && boundary > start ? boundary : index;
                if (end < text.Length)
                {
                    var tokenStart = text.LastIndexOf("[[YG", end - 1, StringComparison.Ordinal);
                    if (tokenStart > start && text.IndexOf("]]", tokenStart, StringComparison.Ordinal) >= end - 1)
                        end = tokenStart;
                }
                chunks.Add(text.Substring(start, end - start));
                start = end;
            }
            return chunks;
        }

        static Backend ReadBackend()
        {
            var value = EditorPrefs.GetInt(Pref + "backend", (int)Backend.MyMemory);
            return Enum.IsDefined(typeof(Backend), value) ? (Backend)value : Backend.MyMemory;
        }

        static string Get(string key, string fallback = "") => EditorPrefs.GetString(Pref + _backend + "." + key, fallback);

        static void Set(string key, string value)
        {
            value = value ?? "";
            if (Get(key, key == "model" ? "gpt-4o-mini" : "") == value) return;
            EditorPrefs.SetString(Pref + _backend + "." + key, value);
            ReloadConfiguration();
        }

        static Configuration ReadConfiguration() => new Configuration(_backend, ApiKey.Trim(), ApiSecret.Trim(), ApiEndpoint.Trim(), ApiModel.Trim());

        static void ReloadConfiguration()
        {
            _configuration = ReadConfiguration();
            CancelQueued();
            RetryFailures();
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(CachePath))
                {
                    var file = JsonUtility.FromJson<CacheFile>(File.ReadAllText(CachePath));
                    if (file?.entries == null) return;
                    foreach (var entry in file.entries)
                        if (entry != null && !string.IsNullOrEmpty(entry.key) && !string.IsNullOrEmpty(entry.value))
                            Cache[entry.key] = entry.value;
                    return;
                }

                var legacy = Path.Combine(CatalogPatcher.DataDir, "cache.json");
                if (!File.Exists(legacy)) return;
                var entries = CatalogPatcher.FlatJson.Parse(File.ReadAllText(legacy));
                if (entries == null) return;
                foreach (var entry in entries)
                    if (!string.IsNullOrWhiteSpace(entry.Value))
                        Cache["MyMemory||" + entry.Key] = entry.Value;
                _dirty = Cache.Count > 0;
            }
            catch (Exception ex)
            {
                CatalogPatcher.Log("CACHE_LOAD_ERR " + ex.GetType().Name);
            }
        }

        static string Escape(string text) => Uri.EscapeDataString(text);

        sealed class Configuration
        {
            internal readonly Backend Backend;
            internal readonly string Key, Secret, Endpoint, Model, CacheNamespace;

            internal Configuration(Backend backend, string key, string secret, string endpoint, string model)
            {
                Backend = backend;
                Key = key;
                Secret = secret;
                Endpoint = endpoint;
                Model = model;
                CacheNamespace = backend == Backend.OpenAICompat
                    ? backend + ":" + TranslatorPreferences.Fingerprint(endpoint + "\n" + model)
                    : backend.ToString();
            }
        }

        sealed class Request
        {
            internal readonly string Key, Source, Language, PackageId;
            internal readonly Configuration Configuration;
            internal readonly CancellationToken Token;

            internal Request(string key, string source, string language, string packageId, Configuration configuration, CancellationToken token)
            {
                Key = key;
                Source = source;
                Language = language;
                PackageId = packageId;
                Configuration = configuration;
                Token = token;
            }
        }

        sealed class Result
        {
            internal readonly Request Request;
            internal readonly string Translation;
            internal readonly TranslationFailure Error;

            internal Result(Request request, string translation, TranslationFailure error)
            {
                Request = request;
                Translation = translation;
                Error = error;
            }
        }

        sealed class TranslationFailure : Exception
        {
            internal readonly bool ProviderWide;
            internal readonly TimeSpan RetryDelay;
            internal TranslationFailure(string message, bool providerWide = false, int seconds = 60) : base(message)
            {
                ProviderWide = providerWide;
                RetryDelay = TimeSpan.FromSeconds(seconds);
            }
        }

#pragma warning disable 0649 // Provider response and cache fields are populated by JsonUtility.
        [Serializable] sealed class CacheFile { public CacheRecord[] entries; }
        [Serializable] sealed class CacheRecord { public string key, value; }
        [Serializable] sealed class MyMemoryResponse { public bool quotaFinished; public int responseStatus; public MyMemoryText responseData; }
        [Serializable] sealed class MyMemoryText { public string translatedText; }
        [Serializable] sealed class YoudaoResponse { public string[] translation; }
        [Serializable] sealed class DeeplRequest { public string[] text; public string source_lang, target_lang; }
        [Serializable] sealed class DeeplResponse { public DeeplText[] translations; }
        [Serializable] sealed class DeeplText { public string text; }
        [Serializable] sealed class BaiduResponse { public string error_code; public BaiduText[] trans_result; }
        [Serializable] sealed class BaiduText { public string dst; }
        [Serializable] sealed class ChatRequest { public string model; public ChatMessage[] messages; }
        [Serializable] sealed class ChatResponse { public ChatChoice[] choices; }
        [Serializable] sealed class ChatChoice { public ChatMessage message; }
        [Serializable] sealed class ChatMessage { public string role, content; }
#pragma warning restore 0649
    }
}
