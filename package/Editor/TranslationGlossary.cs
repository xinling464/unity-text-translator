using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YGTextTranslator
{
    internal static class TranslationGlossary
    {
        [Serializable]
        internal sealed class Entry
        {
            public string packageId;
            public string source;
            public string translation;
            public bool ignored;
        }

        [Serializable]
        sealed class EntryFile
        {
            public Entry[] entries;
        }

        const int PendingLimit = 1500;
        static string GlossaryPath => Path.Combine(CatalogPatcher.DataDir, "glossary.json");
        static string PendingPath => Path.Combine(TranslatorPreferences.ProjectDataDir, "pending.json");
        static Dictionary<(string, string), Entry> _entries;
        static Dictionary<(string, string), Entry> _pending;
        static bool _pendingChanged;

        internal static bool TryGet(string packageId, string source, out Entry entry)
        {
            EnsureLoaded();
            var package = NormalizePackage(packageId);
            return _entries.TryGetValue((package, source), out entry)
                || (package.Length != 0 && _entries.TryGetValue(("", source), out entry));
        }

        internal static void Note(string packageId, string source, string translation)
        {
            if (source.Length > 900) return;
            EnsureLoaded();
            var package = NormalizePackage(packageId);
            var key = (package, source);
            if (_pending.TryGetValue(key, out var entry))
            {
                if (translation != null && entry.translation != translation)
                {
                    entry.translation = translation;
                    _pendingChanged = true;
                }
                return;
            }

            if (_pending.Count >= PendingLimit) return;
            _pending.Add(key, new Entry { packageId = package, source = source, translation = translation ?? "" });
            _pendingChanged = true;
        }

        internal static Entry[] Entries()
        {
            EnsureLoaded();
            return Snapshot(_entries.Values);
        }

        internal static Entry[] Pending()
        {
            EnsureLoaded();
            return Snapshot(_pending.Values.Where(entry => !TryGet(entry.packageId, entry.source, out _)));
        }

        internal static void Set(string packageId, string source, string translation, bool ignored = false)
        {
            var entry = Validate(new Entry { packageId = packageId, source = source, translation = translation, ignored = ignored });
            EnsureLoaded();
            var copy = new Dictionary<(string, string), Entry>(_entries);
            var key = (entry.packageId, entry.source);
            copy[key] = entry;
            Save(GlossaryPath, copy.Values);
            _entries = copy;
            _pendingChanged |= _pending.Remove(key);
        }

        internal static void Remove(string packageId, string source)
        {
            if (source == null) return;
            EnsureLoaded();
            var copy = new Dictionary<(string, string), Entry>(_entries);
            if (!copy.Remove((NormalizePackage(packageId), source.Trim()))) return;
            Save(GlossaryPath, copy.Values);
            _entries = copy;
        }

        internal static int Import(string path)
        {
            var incoming = Read(path).Select(Validate).ToArray();
            EnsureLoaded();
            var copy = new Dictionary<(string, string), Entry>(_entries);
            foreach (var entry in incoming) copy[(entry.packageId, entry.source)] = entry;
            Save(GlossaryPath, copy.Values);
            _entries = copy;
            foreach (var entry in incoming) _pendingChanged |= _pending.Remove((entry.packageId, entry.source));
            return incoming.Length;
        }

        internal static void Export(string path)
        {
            EnsureLoaded();
            Save(path, _entries.Values);
        }

        internal static void Flush()
        {
            if (!_pendingChanged) return;
            try
            {
                Save(PendingPath, _pending.Values);
                _pendingChanged = false;
            }
            catch (Exception ex)
            {
                CatalogPatcher.Log("PENDING_SAVE_ERR " + ex.GetType().Name);
            }
        }

        static void EnsureLoaded()
        {
            if (_entries != null) return;
            _entries = Load(GlossaryPath);
            _pending = Load(PendingPath);
        }

        static Dictionary<(string, string), Entry> Load(string path)
        {
            var result = new Dictionary<(string, string), Entry>();
            try
            {
                if (!File.Exists(path)) return result;
                foreach (var entry in Read(path))
                {
                    if (entry == null || string.IsNullOrEmpty(entry.source)) continue;
                    entry.packageId = NormalizePackage(entry.packageId);
                    entry.source = entry.source.Trim();
                    result[(entry.packageId, entry.source)] = entry;
                }
            }
            catch (Exception ex)
            {
                CatalogPatcher.Log("GLOSSARY_LOAD_ERR " + ex.GetType().Name);
            }
            return result;
        }

        static Entry[] Read(string path)
        {
            var file = JsonUtility.FromJson<EntryFile>(File.ReadAllText(path));
            if (file?.entries == null) throw new FormatException("词典文件需要包含 entries 数组。");
            return file.entries;
        }

        static void Save(string path, IEnumerable<Entry> entries)
        {
            TranslatorPreferences.WriteAtomic(path, JsonUtility.ToJson(new EntryFile { entries = Snapshot(entries) }, true));
        }

        static Entry[] Snapshot(IEnumerable<Entry> entries)
        {
            return entries.OrderBy(entry => entry.packageId, StringComparer.Ordinal)
                .ThenBy(entry => entry.source, StringComparer.Ordinal)
                .Select(entry => new Entry
                {
                    packageId = entry.packageId,
                    source = entry.source,
                    translation = entry.translation,
                    ignored = entry.ignored
                }).ToArray();
        }

        static string NormalizePackage(string packageId) => (packageId ?? "").Trim().ToLowerInvariant();

        static Entry Validate(Entry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.source))
                throw new ArgumentException("请输入原文。");
            if (!entry.ignored && (string.IsNullOrWhiteSpace(entry.translation)
                || !TextPipeline.PreservesMarkers(entry.source, entry.translation)))
                throw new ArgumentException("译文不能为空，且需保留原文的占位符和富文本标签。");
            entry.packageId = NormalizePackage(entry.packageId);
            entry.source = entry.source.Trim();
            entry.translation = entry.ignored ? "" : entry.translation;
            return entry;
        }
    }
}
