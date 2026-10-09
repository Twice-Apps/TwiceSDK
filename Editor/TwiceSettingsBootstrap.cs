using System.IO;
using UnityEditor;
using UnityEngine;
using TwiceSDK;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// Editor convenience: when the package is imported into a project that has no
    /// <see cref="TwiceSettings"/> yet, auto-create an empty one at
    /// <c>Assets/Resources/TwiceSettings.asset</c> so the SDK auto-initialises at boot.
    /// The asset is created with NO API key — you paste your X-App-Key in the Inspector
    /// (the key belongs to the game, never to the shared package).
    /// </summary>
    [InitializeOnLoad]
    internal static class TwiceSettingsBootstrap
    {
        const string ResourcesFolder = "Assets/Resources";
        const string AssetPath = "Assets/Resources/TwiceSettings.asset";

        static TwiceSettingsBootstrap()
        {
            // Defer until the asset database is ready (avoid running mid-import).
            EditorApplication.delayCall += EnsureSettingsAsset;
        }

        static void EnsureSettingsAsset()
        {
            Find(true);
        }

        /// <summary>The project's settings asset (any Resources folder); creates the default one when asked.</summary>
        static TwiceSettings Find(bool create)
        {
            // Already have one anywhere in a Resources folder? Then use it.
            var existing = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (existing != null) return existing;
            if (File.Exists(AssetPath)) return AssetDatabase.LoadAssetAtPath<TwiceSettings>(AssetPath);
            if (!create) return null;

            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var settings = ScriptableObject.CreateInstance<TwiceSettings>(); // empty apiKey by default
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[TwiceSDK] Created " + AssetPath +
                      " — paste your X-App-Key into it (Inspector). Twice admin → Oyunlar → your game → API anahtarı.");
            return settings;
        }

        /// <summary>Twice ▸ Twice SDK Settings — selects (creating if missing) the settings asset and shows it in the Inspector.</summary>
        [MenuItem("Twice/Twice SDK Settings", false, 1)]
        static void OpenSettings()
        {
            var s = Find(true);
            if (s == null) { Debug.LogWarning("[TwiceSDK] TwiceSettings could not be created at " + AssetPath); return; }
            Selection.activeObject = s;
            EditorGUIUtility.PingObject(s);
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        }
    }
}
