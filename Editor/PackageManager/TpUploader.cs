using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// Publishes a package or a new version: optional export from project folders,
    /// sha256, chunked upload (8 MB parts, each retried), server-side join + verify,
    /// then the preview image.
    /// </summary>
    public static class TpUploader
    {
        public sealed class Request
        {
            public bool IsNewPackage;
            public string Slug;
            public string Name;
            public string Version;
            public string Category;
            public string Description;
            public string Publisher;
            public string[] Tags = new string[0];
            public string AssetStoreUrl;
            public string Unity;
            public string Changelog;
            public string[] Dependencies = new string[0];
            public string[] UpmDependencies = new string[0];

            public string SourceFile;           // an existing .unitypackage, or…
            public List<string> ExportPaths;    // …project paths exported now
            public bool IncludeDependencies;

            public string ImagePath;            // optional png/jpg
        }

        public static bool Running { get; private set; }

        /// <summary>Returns null on success, else an error message.</summary>
        public static async Task<string> Upload(Request r)
        {
            if (Running) return "Başka bir yükleme sürüyor.";
            Running = true;
            string exported = null;
            var cancel = new TpCancel();
            try
            {
                string file = r.SourceFile;
                if (r.ExportPaths != null && r.ExportPaths.Count > 0)
                {
                    EditorUtility.DisplayProgressBar("Twice Packages", "Paket dışa aktarılıyor…", 0f);
                    Directory.CreateDirectory("Temp/TwicePackages");
                    exported = Path.GetFullPath("Temp/TwicePackages/" + r.Slug + "-" + r.Version + ".unitypackage");
                    if (File.Exists(exported)) File.Delete(exported);
                    var opts = ExportPackageOptions.Recurse;
                    if (r.IncludeDependencies) opts |= ExportPackageOptions.IncludeDependencies;
                    AssetDatabase.ExportPackage(r.ExportPaths.ToArray(), exported, opts);
                    if (!File.Exists(exported)) return "Dışa aktarma başarısız.";
                    file = exported;
                }
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return ".unitypackage dosyası yok.";

                long size = new FileInfo(file).Length;
                EditorUtility.DisplayProgressBar("Twice Packages", "sha256 hesaplanıyor… (" + TpFormat.Size(size) + ")", 0f);
                string sha = await TpCatalog.Sha256Async(file);
                string[] namespaces;
                try { namespaces = (await Task.Run(() => TpUnityPackageReader.Namespaces(file))).ToArray(); }
                catch (Exception) { namespaces = new string[0]; }

                var begin = await TpHub.PostJson<TpUploadBeginResponse>("upload_begin", new TpUploadBeginRequest
                {
                    slug = r.Slug,
                    name = r.Name,
                    version = r.Version,
                    size = size,
                    sha256 = sha,
                    unity = r.Unity,
                    changelog = r.Changelog,
                    dependencies = r.Dependencies,
                    upmDependencies = r.UpmDependencies,
                    namespaces = namespaces,
                    category = r.Category,
                    description = r.Description,
                    publisher = r.Publisher,
                    tags = r.Tags,
                    assetStoreUrl = r.AssetStoreUrl
                });
                if (!begin.Ok) return begin.Error;
                var b = begin.Data;
                string uid = b.uploadId;
                long chunk = b.chunkSize > 0 ? b.chunkSize : 8L * 1024 * 1024;

                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buf = new byte[chunk];
                    long sent = 0;
                    for (int i = 0; i < b.chunks; i++)
                    {
                        int len = (int)Math.Min(chunk, size - (long)i * chunk);
                        byte[] data = len == buf.Length ? buf : new byte[len];
                        fs.Seek((long)i * chunk, SeekOrigin.Begin);
                        int read = 0;
                        while (read < len)
                        {
                            int n = fs.Read(data, read, len - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        string err = null;
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            long baseSent = sent;
                            var res = await TpHub.PostBytes<TpApiBase>("upload_chunk", "upload=" + uid + "&index=" + i, data, pr =>
                            {
                                float total = (baseSent + pr * len) / (float)size;
                                if (EditorUtility.DisplayCancelableProgressBar("Twice Packages — yükleniyor",
                                        r.Slug + " " + r.Version + " · " + TpFormat.Size((long)(total * size)) + " / " + TpFormat.Size(size), total))
                                    cancel.Requested = true;
                            }, cancel);
                            if (cancel.Requested) { await Abort(uid); return "İptal edildi."; }
                            err = res.Ok ? null : res.Error;
                            if (err == null) break;
                            TpLog.Warn("Parça " + i + " tekrar deneniyor: " + err);
                            await Task.Delay(1000 * (attempt + 1));
                        }
                        if (err != null) { await Abort(uid); return "Parça " + i + " yüklenemedi: " + err; }
                        sent += len;
                    }
                }

                EditorUtility.DisplayProgressBar("Twice Packages", "Sunucu dosyayı doğruluyor…", 1f);
                var fin = await TpHub.PostJson<TpPackageResponse>("upload_finish", new TpUploadIdRequest { uploadId = uid });
                if (!fin.Ok) return fin.Error;
                var pkg = fin.Data.package;

                if (!string.IsNullOrEmpty(r.ImagePath))
                {
                    EditorUtility.DisplayProgressBar("Twice Packages", "Görsel yükleniyor…", 1f);
                    var img = await SetImage(pkg.slug, r.ImagePath);
                    if (img.Key != null) pkg = img.Key;
                    else TpLog.Warn("Paket yüklendi ama görsel yüklenemedi: " + img.Value);
                }

                TpCatalog.Upsert(pkg);
                TpLog.Note("Yüklendi: " + pkg.slug + " " + r.Version + " (" + TpFormat.Size(size) + ")");
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                Running = false;
                EditorUtility.ClearProgressBar();
                if (exported != null) { try { File.Delete(exported); } catch (Exception) { } }
            }
        }

        static async Task Abort(string uid)
        {
            await TpHub.PostJson<TpApiBase>("upload_abort", new TpUploadIdRequest { uploadId = uid });
        }

        /// <summary>Downscales to 640 px max and uploads. Returns (package, null) or (null, error).</summary>
        public static async Task<KeyValuePair<TpPackage, string>> SetImage(string slug, string imagePath)
        {
            byte[] bytes;
            string mime;
            string err = PrepareImage(imagePath, out bytes, out mime);
            if (err != null) return new KeyValuePair<TpPackage, string>(null, err);
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("slug", slug),
                new MultipartFormFileSection("image", bytes, mime == "image/png" ? "image.png" : "image.jpg", mime)
            };
            var res = await TpHub.PostForm<TpPackageResponse>("set_image", form);
            if (!res.Ok) return new KeyValuePair<TpPackage, string>(null, res.Error);
            return new KeyValuePair<TpPackage, string>(res.Data.package, null);
        }

        public static string PrepareImage(string path, out byte[] bytes, out string mime)
        {
            bytes = null;
            mime = "image/png";
            if (!File.Exists(path)) return "Görsel bulunamadı: " + path;
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!src.LoadImage(File.ReadAllBytes(path))) return "Görsel okunamadı (PNG/JPG olmalı).";
                const int max = 640;
                Texture2D outTex = src;
                if (src.width > max || src.height > max)
                {
                    float k = Mathf.Min(max / (float)src.width, max / (float)src.height);
                    int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));
                    var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var prev = RenderTexture.active;
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    outTex.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }
                bytes = outTex.EncodeToPNG();
                if (bytes.Length > 1024 * 1024)
                {
                    bytes = outTex.EncodeToJPG(85);
                    mime = "image/jpeg";
                }
                if (outTex != src) UnityEngine.Object.DestroyImmediate(outTex);
                return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(src);
            }
        }
    }
}
