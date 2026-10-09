using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// The team catalog plus the machine-wide download cache. The catalog is kept on
    /// disk so the window opens instantly and works offline; a refresh sends the
    /// cached rev as ETag and the hub answers 304 when nothing changed.
    /// Cache layout (TpSettings.CacheDir):
    ///   catalog.json · images/&lt;image file&gt; · files/&lt;slug&gt;/&lt;version&gt;/&lt;slug&gt;-&lt;version&gt;.unitypackage
    /// </summary>
    public static class TpCatalog
    {
        static TpCatalogCache _cache;
        static readonly Dictionary<string, Texture2D> _images = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> _imageLoading = new HashSet<string>();

        public static event Action Changed;

        public static TpCatalogCache Cache
        {
            get
            {
                if (_cache == null) _cache = LoadCache();
                return _cache;
            }
        }

        public static List<TpPackage> Packages { get { return Cache.packages ?? (Cache.packages = new List<TpPackage>()); } }
        public static bool IsSuper { get { return Cache.super; } }

        static readonly string[] DefaultCategories = { "3D", "2D", "Audio", "Tools", "VFX", "Templates", "SDKs", "Other" };

        /// <summary>The hub's fixed category list (server-side canonical); one dropdown everywhere.</summary>
        public static string[] Categories
        {
            get { var c = Cache.categories; return c != null && c.Length > 0 ? c : DefaultCategories; }
        }

        /// <summary>Subcategories of a category (Asset Store tree, server-side canonical).</summary>
        public static string[] Subcategories(string category)
        {
            if (Cache.taxonomy != null)
                foreach (var n in Cache.taxonomy) if (n.name == category) return n.subs ?? new string[0];
            return new string[0];
        }
        public static string User { get { return Cache.user; } }

        public static TpPackage Find(string slug)
        {
            foreach (var p in Packages) if (p.slug == slug) return p;
            return null;
        }

        static string CatalogPath { get { return Path.Combine(TpSettings.CacheDir, "catalog.json"); } }

        static TpCatalogCache LoadCache()
        {
            try
            {
                if (File.Exists(CatalogPath))
                {
                    var c = JsonUtility.FromJson<TpCatalogCache>(File.ReadAllText(CatalogPath));
                    if (c != null && c.hub == TpSettings.HubBase) return c;
                }
            }
            catch (Exception e) { TpLog.Warn("Katalog önbelleği okunamadı: " + e.Message); }
            return new TpCatalogCache { hub = TpSettings.HubBase };
        }

        static void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(TpSettings.CacheDir);
                File.WriteAllText(CatalogPath, JsonUtility.ToJson(_cache, true));
            }
            catch (Exception e) { TpLog.Warn("Katalog önbelleği yazılamadı: " + e.Message); }
        }

        /// <summary>Returns null on success, else an error. force skips the ETag shortcut.</summary>
        public static async Task<string> Refresh(bool force)
        {
            var c = Cache;
            var headers = new Dictionary<string, string>();
            if (!force && c.rev > 0 && c.hub == TpSettings.HubBase && c.packages != null && c.packages.Count > 0)
                headers["If-None-Match"] = "\"tpkg-" + c.rev + "\"";
            var r = await TpHub.Get<TpCatalogResponse>("catalog", null, headers);
            if (!r.Ok) return r.Error;
            if (!r.NotModified)
            {
                _cache = new TpCatalogCache
                {
                    hub = TpSettings.HubBase,
                    rev = r.Data.rev,
                    user = r.Data.user,
                    super = r.Data.super,
                    categories = r.Data.categories ?? new string[0],
                    taxonomy = r.Data.taxonomy ?? new List<TpCategoryNode>(),
                    fetchedAt = DateTime.UtcNow.ToString("o"),
                    packages = r.Data.packages ?? new List<TpPackage>()
                };
                SaveCache();
                TpLog.Info("Katalog yenilendi: " + _cache.packages.Count + " paket, rev " + _cache.rev);
            }
            if (Changed != null) Changed();
            return null;
        }

        /// <summary>Puts one package (fresh from an upload or edit) into the cached catalog.</summary>
        public static void Upsert(TpPackage p)
        {
            if (p == null) return;
            var list = Packages;
            int i = list.FindIndex(x => x.slug == p.slug);
            if (i >= 0) list[i] = p; else list.Add(p);
            Cache.rev = 0;   // force a full fetch next time
            SaveCache();
            if (Changed != null) Changed();
        }

        public static void Remove(string slug)
        {
            Packages.RemoveAll(x => x.slug == slug);
            Cache.rev = 0;
            SaveCache();
            if (Changed != null) Changed();
        }

        /// <summary>Disconnect: the offline catalog copy must not outlive the token.</summary>
        public static void Forget()
        {
            try { if (File.Exists(CatalogPath)) File.Delete(CatalogPath); } catch (Exception) { }
            _cache = new TpCatalogCache { hub = TpSettings.HubBase };
            if (Changed != null) Changed();
        }

        public static void ResetForHubChange()
        {
            _cache = new TpCatalogCache { hub = TpSettings.HubBase };
            if (Changed != null) Changed();
        }

        /* ---- preview images ---------------------------------------------------- */

        /// <summary>Texture for the package image, or null while it loads / when there is none.</summary>
        public static Texture2D Image(TpPackage p)
        {
            if (p == null || string.IsNullOrEmpty(p.image)) return null;
            Texture2D t;
            if (_images.TryGetValue(p.image, out t) && t != null) return t;
            if (_imageLoading.Contains(p.image)) return null;
            _imageLoading.Add(p.image);
            LoadImage(p);
            return null;
        }

        static async void LoadImage(TpPackage p)
        {
            string key = p.image;
            string path = Path.Combine(TpSettings.CacheDir, "images", Path.GetFileName(key));
            try
            {
                byte[] bytes = null;
                if (File.Exists(path)) bytes = File.ReadAllBytes(path);
                else
                {
                    bytes = await TpHub.DownloadBytes("image", "slug=" + Uri.EscapeDataString(p.slug));
                    if (bytes != null && bytes.Length > 0)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, bytes);
                    }
                }
                if (bytes == null || bytes.Length == 0) return;   // stays in _imageLoading: no retry storm
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (tex.LoadImage(bytes)) _images[key] = tex;
                if (Changed != null) Changed();
            }
            catch (Exception e) { TpLog.Info("Görsel yüklenemedi (" + p.slug + "): " + e.Message); }
        }

        /* ---- download cache ------------------------------------------------------ */

        public static string CachedFile(string slug, string version)
        {
            return Path.Combine(TpSettings.CacheDir, "files", slug, version, slug + "-" + version + ".unitypackage").Replace('\\', '/');
        }

        /// <summary>
        /// Makes sure slug@version is in the cache and matches the catalog sha256.
        /// Returns (path, null) or (null, error).
        /// </summary>
        public static async Task<KeyValuePair<string, string>> Ensure(TpPackage p, TpVersion v, Action<float> progress, TpCancel cancel)
        {
            string dest = CachedFile(p.slug, v.version);
            if (File.Exists(dest))
            {
                string have = await Sha256Async(dest);
                if (string.Equals(have, v.sha256, StringComparison.OrdinalIgnoreCase)) return new KeyValuePair<string, string>(dest, null);
                TpLog.Warn(p.slug + " " + v.version + " önbellekte bozuk; yeniden indiriliyor.");
                try { File.Delete(dest); } catch (Exception) { }
            }
            string tmp = dest + ".part";
            string err = await TpHub.DownloadFile("download", "slug=" + Uri.EscapeDataString(p.slug) + "&v=" + Uri.EscapeDataString(v.version), tmp, progress, cancel);
            if (err != null) return new KeyValuePair<string, string>(null, err);
            string sha = await Sha256Async(tmp);
            if (!string.Equals(sha, v.sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tmp); } catch (Exception) { }
                return new KeyValuePair<string, string>(null, "İndirilen dosyanın sha256'sı tutmuyor; bağlantı yarıda kesilmiş olabilir. Tekrar dene.");
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            return new KeyValuePair<string, string>(dest, null);
        }

        public static Task<string> Sha256Async(string path)
        {
            return Task.Run(() => Sha256(path));
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                var h = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(64);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static long CacheSize()
        {
            string d = Path.Combine(TpSettings.CacheDir, "files");
            if (!Directory.Exists(d)) return 0;
            long s = 0;
            foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) s += new FileInfo(f).Length;
            return s;
        }

        public static void ClearFileCache()
        {
            string d = Path.Combine(TpSettings.CacheDir, "files");
            if (Directory.Exists(d)) Directory.Delete(d, true);
            string i = Path.Combine(TpSettings.CacheDir, "images");
            if (Directory.Exists(i)) Directory.Delete(i, true);
            _images.Clear();
            _imageLoading.Clear();
        }
    }
}
