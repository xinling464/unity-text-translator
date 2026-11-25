using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace YGTextTranslator
{
    [InitializeOnLoad]
    internal static class PackageScanner
    {
        internal const string SnapshotName = "packages.txt";

        static string SnapshotPath => Path.Combine(CatalogPatcher.ProjectRoot, "Library", "YGUnityTranslator", SnapshotName);

        internal static List<string> LastNewPackages { get; private set; } = new List<string>();

        static PackageScanner()
        {
            ScheduleScan();
            UnityEditor.PackageManager.Events.registeredPackages += _ => ScheduleScan();
        }

        static void ScheduleScan()
        {
            EditorApplication.delayCall -= UpdateInventory;
            EditorApplication.delayCall += UpdateInventory;
        }

        static void UpdateInventory() => Scan();

        internal static List<string> Scan()
        {
            var changed = new List<string>();
            try
            {
                var snapshot = SnapshotPath;
                bool firstScan = !File.Exists(snapshot);
                var oldText = firstScan ? "" : File.ReadAllText(snapshot);
                var previous = ReadSnapshot(oldText);
                var current = PackageInfo.GetAllRegisteredPackages()
                    .Where(package => IsThirdParty(package.name))
                    .OrderBy(package => package.name, StringComparer.Ordinal)
                    .Select(package => new KeyValuePair<string, string>(package.name, package.version))
                    .ToArray();

                if (!firstScan)
                {
                    foreach (var package in current)
                    {
                        if (!previous.TryGetValue(package.Key, out var oldVersion))
                            changed.Add($"{package.Key} {package.Value}（新安装）");
                        else if (oldVersion != package.Value)
                            changed.Add($"{package.Key} {oldVersion} → {package.Value}");
                    }
                }

                var newText = string.Join("\n", current.Select(package => package.Key + "\t" + package.Value));
                if (firstScan || newText != oldText) WriteSnapshot(snapshot, newText);
                if (changed.Count > 0) LastNewPackages = changed;
            }
            catch (Exception e)
            {
                CatalogPatcher.Log("SNAPSHOT_ERR " + e.GetType().Name + " " + e.Message);
            }
            return changed;
        }

        static bool IsThirdParty(string packageName)
        {
            return !string.IsNullOrEmpty(packageName)
                && packageName != "com.yg.unity-text-translator"
                && !packageName.StartsWith("com.unity.", StringComparison.Ordinal);
        }

        static Dictionary<string, string> ReadSnapshot(string text)
        {
            var versions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in text.Split('\n'))
            {
                int separator = line.IndexOf('\t');
                if (separator > 0)
                    versions[line.Substring(0, separator)] = line.Substring(separator + 1).TrimEnd('\r');
            }
            return versions;
        }

        static void WriteSnapshot(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
