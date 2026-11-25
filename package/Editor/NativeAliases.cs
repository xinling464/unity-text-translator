using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YGTextTranslator
{
    internal enum NativeAliasSurface { Hierarchy, Project, BlendShape }

    [InitializeOnLoad]
    internal static class NativeAliases
    {
        const int CacheLimit = 8192;
        static readonly string PrefPrefix = TranslatorPreferences.Prefix + TranslatorPreferences.ProjectId + ".nativeAlias.";
        static readonly bool[] Enabled =
        {
            ReadPreference(NativeAliasSurface.Hierarchy),
            ReadPreference(NativeAliasSurface.Project),
            ReadPreference(NativeAliasSurface.BlendShape)
        };
        static readonly Dictionary<int, string> HierarchyKeys = new Dictionary<int, string>();
        static readonly Dictionary<int, string> AssetKeys = new Dictionary<int, string>();
        static readonly Dictionary<int, string> MeshKeys = new Dictionary<int, string>();
        static readonly Dictionary<(NativeAliasSurface, string), string> Automatic = new Dictionary<(NativeAliasSurface, string), string>();
        static Dictionary<string, string> _overrides;
        static bool _loadFailed;
        static int _hierarchyOverrides;
        static int _assetOverrides;
        static int _shapeOverrides;

        internal static event Action Changed;
        static string StorePath => Path.Combine(TranslatorPreferences.ProjectDataDir, "display-aliases.json");

        static NativeAliases()
        {
            EditorApplication.hierarchyChanged += HierarchyKeys.Clear;
            EditorApplication.projectChanged += InvalidateAssetKeys;
            Undo.undoRedoPerformed += HierarchyKeys.Clear;
        }

        internal static bool IsPreferenceEnabled(NativeAliasSurface surface) => Enabled[(int)surface];

        internal static bool IsEnabled(NativeAliasSurface surface) =>
            TranslatorPreferences.RuntimeEnabled && !TranslatorPreferences.ShowOriginal && IsPreferenceEnabled(surface);

        internal static void SetEnabled(NativeAliasSurface surface, bool enabled)
        {
            if (IsPreferenceEnabled(surface) == enabled) return;
            EditorPrefs.SetBool(PrefPrefix + surface, enabled);
            Enabled[(int)surface] = enabled;
        }

        internal static string ResolveHierarchy(int instanceId, string original, bool useDisplaySettings = true)
        {
            if (!CanResolve(NativeAliasSurface.Hierarchy, original, useDisplaySettings)) return original;
            EnsureLoaded();
            var key = _hierarchyOverrides == 0 ? null :
                KeyForInstance(HierarchyKeys, instanceId, obj => HierarchyKey(obj as GameObject));
            return Read(key) ?? ResolveAutomatic(NativeAliasSurface.Hierarchy, original);
        }

        internal static string ResolveAsset(int instanceId, string original, bool useDisplaySettings = true) =>
            ResolveAsset(instanceId, null, original, useDisplaySettings);

        internal static string ResolveAsset(int instanceId, string guid, string original, bool useDisplaySettings = true)
        {
            if (!CanResolve(NativeAliasSurface.Project, original, useDisplaySettings)) return original;
            EnsureLoaded();
            string key = null;
            if (_assetOverrides > 0)
            {
                key = KeyForInstance(AssetKeys, instanceId, AssetKey);
                if (key == null && !string.IsNullOrEmpty(guid)) key = "a:" + guid;
            }
            return Read(key) ?? ResolveAutomatic(NativeAliasSurface.Project, original);
        }

        internal static string ResolveAssetPath(string guid, string original, bool useDisplaySettings = true)
        {
            if (!CanResolve(NativeAliasSurface.Project, original, useDisplaySettings)) return original;
            EnsureLoaded();
            return Read(string.IsNullOrEmpty(guid) ? null : "a:" + guid)
                ?? ResolveAutomatic(NativeAliasSurface.Project, original);
        }

        internal static string ResolveBlendShape(Object[] targets, string original)
        {
            if (!CanResolve(NativeAliasSurface.BlendShape, original, true) || targets == null || targets.Length == 0)
                return original;

            string display = null;
            foreach (var target in targets)
            {
                if (!(target is SkinnedMeshRenderer renderer) || renderer.sharedMesh == null) return original;
                var alias = ResolveBlendShape(renderer.sharedMesh, original);
                if (display != null && !string.Equals(display, alias, StringComparison.Ordinal)) return original;
                display = alias;
            }
            return display ?? original;
        }

        internal static string ResolveBlendShape(Mesh mesh, string original, bool useDisplaySettings = true)
        {
            if (!CanResolve(NativeAliasSurface.BlendShape, original, useDisplaySettings)) return original;
            EnsureLoaded();
            var prefix = mesh != null && _shapeOverrides > 0 ?
                KeyForInstance(MeshKeys, mesh.GetInstanceID(), obj => ShapePrefix(obj as Mesh)) : null;
            return Read(prefix == null ? null : prefix + original)
                ?? ResolveAutomatic(NativeAliasSurface.BlendShape, original);
        }

        internal static string GetHierarchyAlias(GameObject obj) => ReadLoaded(HierarchyKey(obj));
        internal static string GetAssetAlias(Object obj) => ReadLoaded(AssetKey(obj));
        internal static string GetBlendShapeAlias(Mesh mesh, string original) => ReadLoaded(ShapeKey(mesh, original));
        internal static bool SetHierarchy(GameObject obj, string alias) => Save(HierarchyKey(obj), alias);
        internal static bool SetAsset(Object obj, string alias) => Save(AssetKey(obj), alias);
        internal static bool SetBlendShape(Mesh mesh, string original, string alias) => Save(ShapeKey(mesh, original), alias);

        internal static string GetAssetDisplayName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return AssetDatabase.IsValidFolder(path) ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        }

        static bool ReadPreference(NativeAliasSurface surface) => EditorPrefs.GetBool(PrefPrefix + surface, true);

        static bool CanResolve(NativeAliasSurface surface, string original, bool useDisplaySettings) =>
            !string.IsNullOrWhiteSpace(original) && (!useDisplaySettings || IsEnabled(surface));

        static string ResolveAutomatic(NativeAliasSurface surface, string original)
        {
            var key = (surface, original);
            if (Automatic.TryGetValue(key, out var alias)) return alias;
            alias = NativeAliasTerms.Translate(surface, original);
            if (Automatic.Count >= CacheLimit) Automatic.Clear();
            Automatic.Add(key, alias);
            return alias;
        }

        static string KeyForInstance(Dictionary<int, string> cache, int id, Func<Object, string> identify)
        {
            if (id == 0) return null;
            if (cache.TryGetValue(id, out var key)) return key;
            key = identify(EditorUtility.InstanceIDToObject(id));
            if (cache.Count >= CacheLimit) cache.Clear();
            cache[id] = key;
            return key;
        }

        static string HierarchyKey(GameObject obj)
        {
            if (obj == null || !obj.scene.IsValid() || string.IsNullOrEmpty(obj.scene.path)) return null;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(obj);
            return id.identifierType == 2 && id.targetObjectId != 0 ? "h:" + id : null;
        }

        static string AssetKey(Object obj)
        {
            if (obj == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
                return null;
            return AssetDatabase.IsMainAsset(obj) ? "a:" + guid : "a:" + guid + ":" + localId;
        }

        static string ShapePrefix(Mesh mesh)
        {
            if (mesh == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId))
                return null;
            return "b:" + guid + ":" + localId + ":";
        }

        static string ShapeKey(Mesh mesh, string original)
        {
            var prefix = ShapePrefix(mesh);
            return prefix == null || string.IsNullOrEmpty(original) ? null : prefix + original;
        }

        static string Read(string key) =>
            key != null && _overrides.TryGetValue(key, out var alias) ? alias : null;

        static string ReadLoaded(string key)
        {
            EnsureLoaded();
            return Read(key);
        }

        static void InvalidateAssetKeys()
        {
            AssetKeys.Clear();
            MeshKeys.Clear();
        }

        static void EnsureLoaded()
        {
            if (_overrides != null) return;
            _overrides = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(StorePath))
                {
                    var store = JsonUtility.FromJson<AliasStore>(File.ReadAllText(StorePath));
                    if (store?.entries == null) throw new FormatException("别名文件缺少 entries。");
                    foreach (var entry in store.entries)
                        if (entry != null && !string.IsNullOrEmpty(entry.key) && !string.IsNullOrWhiteSpace(entry.alias))
                            _overrides[entry.key] = entry.alias;
                }
                CountOverrides();
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                Debug.LogWarning("[YGTextTranslator] 无法加载显示别名：" + ex.Message);
            }
        }

        static bool Save(string key, string alias)
        {
            if (key == null) return false;
            EnsureLoaded();
            if (_loadFailed) throw new IOException("别名文件读取失败，请先修复 display-aliases.json。");
            var value = alias?.Trim();
            if (string.IsNullOrEmpty(value)) value = null;
            if (string.Equals(Read(key), value, StringComparison.Ordinal)) return true;
            if (value != null && (value.Length > 256 || value.Any(char.IsControl)))
                throw new ArgumentException("显示别名最长 256 个字符，并且需要写在一行。");

            var updated = new Dictionary<string, string>(_overrides, StringComparer.Ordinal);
            if (value == null) updated.Remove(key);
            else updated[key] = value;
            var file = new AliasStore
            {
                entries = updated.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new AliasEntry { key = item.Key, alias = item.Value }).ToArray()
            };
            TranslatorPreferences.WriteAtomic(StorePath, JsonUtility.ToJson(file, true));
            _overrides = updated;
            CountOverrides();
            HierarchyKeys.Clear();
            InvalidateAssetKeys();
            Changed?.Invoke();
            return true;
        }

        static void CountOverrides()
        {
            _hierarchyOverrides = 0;
            _assetOverrides = 0;
            _shapeOverrides = 0;
            foreach (var key in _overrides.Keys)
            {
                if (key.StartsWith("h:", StringComparison.Ordinal)) _hierarchyOverrides++;
                else if (key.StartsWith("a:", StringComparison.Ordinal)) _assetOverrides++;
                else if (key.StartsWith("b:", StringComparison.Ordinal)) _shapeOverrides++;
            }
        }

        [Serializable]
        sealed class AliasStore { public AliasEntry[] entries; }

        [Serializable]
        sealed class AliasEntry { public string key; public string alias; }
    }
}
