using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using TwiceSDK;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// Auto-installs the package's EDITABLE content into the project at <c>Assets/TwiceSDK/</c> on
    /// load — no Package Manager "Import Sample" step. This covers the TwiceSDK bootstrap prefab
    /// (with the per-game VersionChecker prompt design) and the Examples / QuickStart demos.
    ///
    /// The content ships hidden in the package under <c>Distributables~</c> (a "~" folder is excluded
    /// from the immutable asset pipeline) and is copied out once per package version. Copy is
    /// non-destructive: existing files are never overwritten (your edits — e.g. the VersionChecker
    /// design — are preserved), and metas are copied so GUIDs and scene references stay stable.
    /// A marker file (<c>Assets/TwiceSDK/.twiceversion</c>) skips re-copying on every reload; a
    /// package upgrade re-runs the copy to add any new files.
    /// </summary>
    [InitializeOnLoad]
    internal static class TwiceContentInstaller
    {
        const string DestRoot   = "Assets/TwiceSDK";
        const string SourceRel  = "Distributables~";
        const string MarkerPath = "Assets/TwiceSDK/.twiceversion";

        static TwiceContentInstaller()
        {
            EditorApplication.delayCall += Install; // defer until the asset DB is ready
        }

        static void Install()
        {
            var pi = PackageInfo.FindForAssembly(typeof(TwiceBootstrap).Assembly);
            if (pi == null) return;

            string version = pi.version ?? "";
            if (File.Exists(MarkerPath) && File.ReadAllText(MarkerPath).Trim() == version)
                return; // already installed for this package version

            string srcRoot = Path.Combine(pi.resolvedPath, SourceRel);
            if (!Directory.Exists(srcRoot)) return;

            int copied = 0;
            foreach (string src in Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories))
            {
                string rel = src.Substring(srcRoot.Length).TrimStart('/', '\\');
                string dest = Path.Combine(DestRoot, rel);
                if (File.Exists(dest)) continue; // never overwrite — preserve project edits
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(src, dest);
                copied++;
            }

            Directory.CreateDirectory(DestRoot);
            File.WriteAllText(MarkerPath, version);

            if (copied > 0)
            {
                AssetDatabase.Refresh();
                Debug.Log("[TwiceSDK] İçerik kuruldu → " + DestRoot + " (" + copied + " dosya). " +
                          "Bootstrap prefab: Assets/TwiceSDK/Prefabs/TwiceSDK.prefab — düzenlenebilir, prefab'i ilk sahnene sürükle.");
            }
        }
    }
}
