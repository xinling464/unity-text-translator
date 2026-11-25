using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YGTextTranslator
{
    internal static class TranslatorPreferences
    {
        internal const string Prefix = "YGTextTranslator.";
        internal static readonly string ProjectId = Fingerprint(Path.GetFullPath(Application.dataPath));
        internal static string ProjectDataDir => Path.Combine(CatalogPatcher.DataDir, "projects", ProjectId);
        static readonly string ProjectKey = Prefix + ProjectId + ".";
        static readonly HashSet<string> DisabledPackages = new HashSet<string>(
            EditorPrefs.GetString(ProjectKey + "disabledPackages", "").Split('\n'),
            StringComparer.OrdinalIgnoreCase);
        static bool _runtimeEnabled = EditorPrefs.GetBool(ProjectKey + "runtime", true);
        static bool _showOriginal = SessionState.GetBool(ProjectKey + "original", false);

        internal static bool RuntimeEnabled
        {
            get => _runtimeEnabled;
            set
            {
                if (_runtimeEnabled == value) return;
                EditorPrefs.SetBool(ProjectKey + "runtime", value);
                _runtimeEnabled = value;
            }
        }

        internal static bool ShowOriginal
        {
            get => _showOriginal;
            set
            {
                if (_showOriginal == value) return;
                SessionState.SetBool(ProjectKey + "original", value);
                _showOriginal = value;
            }
        }

        internal static bool IsPackageEnabled(string packageId)
        {
            return string.IsNullOrEmpty(packageId) || !DisabledPackages.Contains(packageId);
        }

        internal static void SetPackageEnabled(string packageId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(packageId) || packageId.IndexOf('\n') >= 0) return;
            if (enabled ? DisabledPackages.Remove(packageId) : DisabledPackages.Add(packageId))
                EditorPrefs.SetString(ProjectKey + "disabledPackages", string.Join("\n", DisabledPackages));
        }

        internal static string Fingerprint(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }

        internal static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
