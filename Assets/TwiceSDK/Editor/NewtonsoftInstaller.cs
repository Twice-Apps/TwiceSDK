#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public static class NewtonsoftInstaller
{
    private const string PackageName = "com.unity.nuget.newtonsoft-json";
    private const string PackageVersion = "3.2.1";

    static NewtonsoftInstaller()
    {
        string manifestPath = Path.Combine(Application.dataPath, "../Packages/manifest.json");

        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning("manifest.json bulunamadı.");
            return;
        }

        string manifestText = File.ReadAllText(manifestPath);

        if (manifestText.Contains($"\"{PackageName}\""))
        {
            Debug.Log($"'{PackageName}' zaten manifest.json içinde mevcut.");
            return;
        }

        // "dependencies": { kısmını bul
        int index = manifestText.IndexOf("\"dependencies\":");
        if (index == -1)
        {
            Debug.LogError("manifest.json içinde 'dependencies' bölümü bulunamadı.");
            return;
        }

        int insertIndex = manifestText.IndexOf("{", index) + 1;

        if (insertIndex <= 0)
        {
            Debug.LogError("manifest.json dependencies bölümü düzgün değil.");
            return;
        }

        // Yeni dependency satırı
        string newDependency = $"\n    \"{PackageName}\": \"{PackageVersion}\",";

        manifestText = manifestText.Insert(insertIndex, newDependency);

        File.WriteAllText(manifestPath, manifestText);
        AssetDatabase.Refresh();

        Debug.Log($"✅ '{PackageName}' v{PackageVersion} manifest.json dosyasına eklendi.");
    }
}
#endif
