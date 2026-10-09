using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// Finds .unitypackage files on this machine for bulk upload: the Unity Asset Store
    /// download cache (every asset downloaded from My Assets) or any folder.
    ///
    /// Asset Store packages carry their own metadata — a JSON blob in the gzip header's
    /// FEXTRA field (sub-field "A$"): title, version, publisher, category, id. That is
    /// read without decompressing anything, so scanning hundreds of GB is instant.
    /// Plain packages fall back to the file name and the folder layout
    /// (cache layout is &lt;Publisher&gt;/&lt;Category&gt;/&lt;Sub&gt;/&lt;Title&gt;.unitypackage).
    /// </summary>
    public static class TpLocalScan
    {
        public sealed class Item
        {
            public string Path;
            public long Size;
            public string Title;
            public string Version;
            public string Publisher;
            public string Category;     // top level, e.g. "3D"
            public string SubCategory;  // e.g. "Characters/Humanoids"
            public string AssetStoreId;
            public string UnityVersion;
            public bool FromAssetStore;
            public string Slug;
            public bool IsFolder;       // a plain asset folder, packed at upload (TpPacker)
        }

        /// <summary>Each immediate subfolder that holds Unity content is one asset (folder-kept assets).</summary>
        public static List<Item> ScanFolders(string root)
        {
            var list = new List<Item>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;
            foreach (var d in Directory.GetDirectories(root))
            {
                string n = System.IO.Path.GetFileName(d);
                if (n.StartsWith(".") || n.EndsWith("~")) continue;
                if (Directory.GetFiles(d, "*.unitypackage", SearchOption.TopDirectoryOnly).Length > 0) continue;   // packages are scanned as files
                if (!TpPacker.LooksLikeAsset(d)) continue;
                var m = System.Text.RegularExpressions.Regex.Match(n, @"^(.*?)[\s_\-]+v?(\d+(?:\.\d+){1,3})$");
                string title = m.Success ? m.Groups[1].Value : n;
                var it = new Item
                {
                    Path = d.Replace('\\', '/'), Size = TpPacker.FolderSize(d), Title = title.Trim(),
                    Version = m.Success ? m.Groups[2].Value : "1.0.0", IsFolder = true
                };
                it.Slug = TpFormat.Slugify(it.Title);
                if (it.Slug.Length < 2) it.Slug = "pkg-" + it.Slug;
                list.Add(it);
            }
            list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        [Serializable] public class AsLabel { public string label; public string id; }
        [Serializable] public class AsLink { public string id; public string type; }
        [Serializable]
        public class AsMeta
        {
            public AsLink link;
            public string id;
            public string title;
            public string version;
            public string unity_version;
            public AsLabel publisher;
            public AsLabel category;
        }

        /* ---- Asset Store cache location ----------------------------------------- */

        public static string DefaultAssetStoreCache
        {
            get
            {
#if UNITY_EDITOR_OSX
                string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                return System.IO.Path.Combine(home, "Library/Unity/Asset Store-5.x").Replace('\\', '/');
#elif UNITY_EDITOR_LINUX
                string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                return System.IO.Path.Combine(home, ".local/share/unity3d/Asset Store-5.x").Replace('\\', '/');
#else
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity/Asset Store-5.x").Replace('\\', '/');
#endif
            }
        }

        /// <summary>Custom path from Unity ▸ Preferences ▸ Package Manager (ASSETSTORE_CACHE_PATH) wins when set.</summary>
        public static string AssetStoreCache
        {
            get
            {
                string custom = TpSettings.GetString("AssetStoreCache", "");
                if (!string.IsNullOrEmpty(custom)) return custom;
                string env = Environment.GetEnvironmentVariable("ASSETSTORE_CACHE_PATH");
                if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env.Replace('\\', '/');
                return DefaultAssetStoreCache;
            }
        }

        /* ---- scan ----------------------------------------------------------------- */

        public static List<Item> Scan(string root, bool assetStoreLayout)
        {
            var list = new List<Item>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;
            foreach (var f in Directory.GetFiles(root, "*.unitypackage", SearchOption.AllDirectories))
            {
                try { list.Add(Describe(f, root, assetStoreLayout)); }
                catch (Exception e) { TpLog.Warn("Okunamadı: " + f + " — " + e.Message); }
            }
            list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static Item Describe(string file, string root, bool assetStoreLayout)
        {
            var it = new Item { Path = file.Replace('\\', '/'), Size = new FileInfo(file).Length };
            var meta = ReadAssetStoreMeta(file);
            if (meta != null && !string.IsNullOrEmpty(meta.title))
            {
                it.FromAssetStore = true;
                it.Title = meta.title.Trim();
                it.Version = meta.version;
                it.Publisher = meta.publisher != null ? meta.publisher.label : "";
                string cat = meta.category != null ? meta.category.label : "";
                SplitCategory(cat, out it.Category, out it.SubCategory);
                it.AssetStoreId = meta.link != null && !string.IsNullOrEmpty(meta.link.id) ? meta.link.id : meta.id;
                it.UnityVersion = meta.unity_version;
            }
            else
            {
                it.Title = System.IO.Path.GetFileNameWithoutExtension(file);
                if (assetStoreLayout)
                {
                    // <Publisher>/<Category>/<Sub…>/<Title>.unitypackage
                    string rel = it.Path.Substring(Math.Min(it.Path.Length, root.Replace('\\', '/').TrimEnd('/').Length + 1));
                    var parts = rel.Split('/');
                    if (parts.Length >= 3)
                    {
                        it.Publisher = parts[0];
                        it.Category = parts[1];
                        it.SubCategory = parts.Length > 3 ? string.Join("/", parts, 2, parts.Length - 3) : "";
                    }
                }
                else
                {
                    // plain folder: the parent folder name is a decent category guess
                    string parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file));
                    if (!string.Equals(System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(file)).TrimEnd('\\', '/'),
                                       System.IO.Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        it.Category = parent;
                }
            }
            if (!TpSemVer.IsValid(it.Version)) it.Version = NormalizeVersion(it.Version);
            it.Slug = TpFormat.Slugify(it.Title);
            if (it.Slug.Length < 2) it.Slug = "pkg-" + it.Slug;
            return it;
        }

        static void SplitCategory(string label, out string top, out string sub)
        {
            label = (label ?? "").Trim('/');
            int s = label.IndexOf('/');
            top = s > 0 ? label.Substring(0, s) : label;
            sub = s > 0 ? label.Substring(s + 1) : "";
        }

        /// <summary>"1.29p04" → "1.29-p04", "v2.1 (beta)" → "2.1-beta"; nothing usable → "1.0.0".</summary>
        public static string NormalizeVersion(string v)
        {
            var m = System.Text.RegularExpressions.Regex.Match(v ?? "", @"(\d+(\.\d+){0,3})(.*)");
            if (!m.Success) return "1.0.0";
            string rest = System.Text.RegularExpressions.Regex.Replace(m.Groups[3].Value, "[^0-9A-Za-z.]+", ".").Trim('.');
            string r = rest.Length > 0 ? m.Groups[1].Value + "-" + rest : m.Groups[1].Value;
            if (r.Length > 40) r = r.Substring(0, 40).TrimEnd('.', '-');
            return TpSemVer.IsValid(r) ? r : m.Groups[1].Value;
        }

        static AsMeta ReadAssetStoreMeta(string file)
        {
            using (var fs = File.OpenRead(file))
            {
                var h = new byte[12];
                if (fs.Read(h, 0, 12) < 12 || h[0] != 0x1f || h[1] != 0x8b) return null;
                if ((h[3] & 0x04) == 0) return null;               // no FEXTRA
                int xlen = h[10] | (h[11] << 8);
                if (xlen <= 4 || xlen > 1 << 16) return null;
                var extra = new byte[xlen];
                if (fs.Read(extra, 0, xlen) < xlen) return null;
                int i = 0;
                while (i + 4 <= xlen)
                {
                    byte si1 = extra[i], si2 = extra[i + 1];
                    int len = extra[i + 2] | (extra[i + 3] << 8);
                    if (i + 4 + len > xlen) break;
                    if (si1 == 'A' && si2 == '$')
                    {
                        string json = Encoding.UTF8.GetString(extra, i + 4, len);
                        try { return JsonUtility.FromJson<AsMeta>(json); } catch (Exception) { return null; }
                    }
                    i += 4 + len;
                }
            }
            return null;
        }
    }
}
