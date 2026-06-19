using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using TwiceSDK;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// Editor convenience: the <c>TwiceSDK</c> bootstrap prefab (root <c>TwiceBootstrap</c> + the
    /// child <c>VersionChecker</c> update-prompt Canvas) must be EDITABLE per game — its UI design
    /// changes from project to project. So it does not live in the immutable package folder; it ships
    /// hidden under <c>Samples~/Bootstrap</c> and is copied once into the project at
    /// <c>Assets/TwiceSDK/Prefabs/TwiceSDK.prefab</c> on first load (automatically, on git pull).
    ///
    /// The .meta is copied too, so the prefab keeps its GUID — existing scene references to it
    /// resolve to the new editable copy. If you already have one, it is left untouched (your edits
    /// are preserved); delete it to get a fresh copy.
    /// </summary>
    [InitializeOnLoad]
    internal static class TwicePrefabBootstrap
    {
        const string DestDir  = "Assets/TwiceSDK/Prefabs";
        const string DestPath = "Assets/TwiceSDK/Prefabs/TwiceSDK.prefab";
        const string SampleRel = "Samples~/Bootstrap/TwiceSDK.prefab";

        static TwicePrefabBootstrap()
        {
            EditorApplication.delayCall += EnsurePrefab; // defer until the asset DB is ready
        }

        static void EnsurePrefab()
        {
            if (File.Exists(DestPath)) return; // already installed (and editable) → never overwrite

            // Resolve the package's physical folder (works for git, local file: and cached installs).
            var pi = PackageInfo.FindForAssembly(typeof(TwiceBootstrap).Assembly);
            if (pi == null) return;
            string src = Path.Combine(pi.resolvedPath, SampleRel);
            if (!File.Exists(src)) return;

            Directory.CreateDirectory(DestDir);
            File.Copy(src, DestPath, false);
            if (File.Exists(src + ".meta")) File.Copy(src + ".meta", DestPath + ".meta", false); // keep GUID

            AssetDatabase.Refresh();
            Debug.Log("[TwiceSDK] Bootstrap prefab kuruldu → " + DestPath +
                      "\nDüzenlenebilir: VersionChecker tasarımını burada değiştir. Prefab'i ilk/preloader sahnene sürükle.");
        }
    }
}
