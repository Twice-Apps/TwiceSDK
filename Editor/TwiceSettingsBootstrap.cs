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
            // Already have one anywhere in a Resources folder? Then do nothing.
            if (Resources.Load<TwiceSettings>(TwiceSettings.ResourceName) != null) return;
            if (File.Exists(AssetPath)) return;

            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var settings = ScriptableObject.CreateInstance<TwiceSettings>(); // empty apiKey by default
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[TwiceSDK] Created " + AssetPath +
                      " — paste your X-App-Key into it (Inspector). Twice admin → Oyunlar → your game → API anahtarı.");
        }
    }
}
