using System;
using System.IO;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace YGTextTranslator
{
    internal static class TranslatorInstallation
    {
        static readonly System.Reflection.Assembly Assembly = typeof(TranslatorInstallation).Assembly;

        internal static string FindRoot()
        {
            var package = PackageInfo.FindForAssembly(Assembly);
            if (!string.IsNullOrEmpty(package?.resolvedPath)) return package.resolvedPath;

            var definition = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(Assembly.GetName().Name);
            if (string.IsNullOrEmpty(definition)) return null;
            var editorDirectory = Path.GetDirectoryName(Path.GetFullPath(definition));
            return Path.GetDirectoryName(editorDirectory);
        }

        internal static string ReadVersion()
        {
            var package = PackageInfo.FindForAssembly(Assembly);
            if (!string.IsNullOrEmpty(package?.version)) return package.version;

            try
            {
                var root = FindRoot();
                if (root == null) return null;
                var manifest = Path.Combine(root, "package.json");
                return File.Exists(manifest) ? JsonUtility.FromJson<Manifest>(File.ReadAllText(manifest))?.version : null;
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
        }

        [Serializable]
        sealed class Manifest
        {
            public string version = null;
        }
    }
}
