using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// Turns a plain asset FOLDER (outside any project, or a project folder) into a
    /// .unitypackage — some assets are kept as folders, not packages. Same format Unity
    /// writes: gzip'd tar of &lt;guid&gt;/pathname, &lt;guid&gt;/asset, &lt;guid&gt;/asset.meta.
    ///
    /// Guids: taken from existing .meta files when present (references between the
    /// asset's files keep working); otherwise derived from slug + relative path, so packing
    /// the same folder again yields the same guids and an update replaces files instead of
    /// duplicating them. A minimal .meta is written; Unity fills importer defaults on import.
    /// </summary>
    public static class TpPacker
    {
        public sealed class Result
        {
            public int Count;
            public int GeneratedMetas;
            public List<string> Namespaces = new List<string>();
            public string RootPath;     // "Assets/<Name>" inside the package
        }

        static readonly Regex GuidRx = new Regex(@"^guid:\s*([0-9a-fA-F]{32})", RegexOptions.Multiline);
        static readonly Regex NsRx = new Regex(@"^\s*namespace\s+([A-Za-z_][\w.]*)", RegexOptions.Multiline);

        /// <summary>
        /// folder: the asset folder. If it is a Unity project (has Assets/ + ProjectSettings/),
        /// its Assets/ content is packed as-is; otherwise everything goes under Assets/&lt;rootName&gt;.
        /// </summary>
        public static Result Pack(string folder, string rootName, string slug, string outFile)
        {
            folder = Path.GetFullPath(folder);
            var res = new Result();
            var items = new List<KeyValuePair<string, string>>();   // (absolute path, package path)
            bool isProject = Directory.Exists(Path.Combine(folder, "Assets")) && Directory.Exists(Path.Combine(folder, "ProjectSettings"));
            if (isProject)
            {
                res.RootPath = "Assets";
                foreach (var e in Children(Path.Combine(folder, "Assets"))) Collect(e, "Assets/" + Path.GetFileName(e), items);
            }
            else
            {
                string name = string.IsNullOrEmpty(rootName) ? Path.GetFileName(folder) : rootName;
                res.RootPath = "Assets/" + Sanitize(name);
                Collect(folder, res.RootPath, items);
            }
            if (items.Count == 0) throw new Exception("Klasör boş: " + folder);

            var ns = new SortedSet<string>(StringComparer.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)));
            using (var fs = File.Create(outFile))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            {
                foreach (var it in items)
                {
                    bool isDir = Directory.Exists(it.Key);
                    string metaPath = it.Key + ".meta";
                    string meta = File.Exists(metaPath) ? File.ReadAllText(metaPath) : null;
                    string guid = null;
                    if (meta != null) { var m = GuidRx.Match(meta); if (m.Success) guid = m.Groups[1].Value.ToLowerInvariant(); }
                    if (guid == null)
                    {
                        guid = StableGuid(slug + "/" + it.Value);
                        meta = "fileFormatVersion: 2\nguid: " + guid + "\n" + (isDir
                            ? "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
                            : "");
                        res.GeneratedMetas++;
                    }
                    WriteEntry(gz, guid + "/pathname", Encoding.UTF8.GetBytes(it.Value));
                    WriteEntry(gz, guid + "/asset.meta", Encoding.UTF8.GetBytes(meta));
                    if (!isDir)
                    {
                        WriteFile(gz, guid + "/asset", it.Key);
                        if (it.Value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && new FileInfo(it.Key).Length < (4 << 20))
                            foreach (Match m in NsRx.Matches(File.ReadAllText(it.Key))) ns.Add(m.Groups[1].Value);
                    }
                    res.Count++;
                }
                gz.Write(new byte[1024], 0, 1024);   // end-of-archive
            }
            res.Namespaces.AddRange(ns);
            return res;
        }

        /// <summary>A folder is an "asset folder" for bulk scans when it holds something Unity imports.</summary>
        public static bool LooksLikeAsset(string dir)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".meta" || ext == ".cs" || ext == ".prefab" || ext == ".unity" || ext == ".mat" || ext == ".fbx" || ext == ".png" || ext == ".shader" || ext == ".asset" || ext == ".wav" || ext == ".ogg")
                        return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        public static long FolderSize(string dir)
        {
            long s = 0;
            try { foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) s += new FileInfo(f).Length; } catch (Exception) { }
            return s;
        }

        static IEnumerable<string> Children(string dir)
        {
            var list = new List<string>();
            foreach (var d in Directory.GetDirectories(dir)) if (!Skip(d)) list.Add(d);
            foreach (var f in Directory.GetFiles(dir)) if (!Skip(f) && !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) list.Add(f);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // Unity ignores these too: hidden files, "~" folders, version control and OS clutter.
        static bool Skip(string p)
        {
            string n = Path.GetFileName(p);
            return n.StartsWith(".") || n.EndsWith("~") || n == "Thumbs.db" || n == "desktop.ini" || n == "__MACOSX";
        }

        static void Collect(string abs, string pkgPath, List<KeyValuePair<string, string>> items)
        {
            items.Add(new KeyValuePair<string, string>(abs, pkgPath));
            if (!Directory.Exists(abs)) return;
            foreach (var c in Children(abs)) Collect(c, pkgPath + "/" + Path.GetFileName(c), items);
        }

        static string Sanitize(string n)
        {
            n = Regex.Replace(n ?? "Asset", @"[\\/:*?""<>|]", "_").Trim();
            return n.Length == 0 ? "Asset" : n;
        }

        static string StableGuid(string s)
        {
            using (var md5 = MD5.Create())
            {
                var h = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(32);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static void WriteEntry(Stream s, string name, byte[] data)
        {
            s.Write(Header(name, data.Length), 0, 512);
            s.Write(data, 0, data.Length);
            Pad(s, data.Length);
        }

        static void WriteFile(Stream s, string name, string file)
        {
            long size = new FileInfo(file).Length;
            s.Write(Header(name, size), 0, 512);
            using (var f = File.OpenRead(file)) f.CopyTo(s, 1 << 20);
            Pad(s, size);
        }

        static void Pad(Stream s, long size)
        {
            int p = (int)((512 - size % 512) % 512);
            if (p > 0) s.Write(new byte[p], 0, p);
        }

        static byte[] Header(string name, long size)
        {
            var h = new byte[512];
            Put(h, 0, name, 100);
            Put(h, 100, "0000644", 8);
            Put(h, 108, "0000000", 8);
            Put(h, 116, "0000000", 8);
            if (size < 077777777777L) Put(h, 124, Convert.ToString(size, 8).PadLeft(11, '0'), 12);
            else { h[124] = 0x80; for (int i = 0; i < 8; i++) h[135 - i] = (byte)(size >> (8 * i)); }   // GNU base-256 (> 8 GB)
            Put(h, 136, Convert.ToString(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 8).PadLeft(11, '0'), 12);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            h[156] = (byte)'0';
            Put(h, 257, "ustar", 6);
            h[263] = (byte)'0'; h[264] = (byte)'0';
            int sum = 0;
            foreach (var b in h) sum += b;
            Put(h, 148, Convert.ToString(sum, 8).PadLeft(6, '0'), 6);
            h[154] = 0;               // checksum field: 6 octal digits, NUL, space
            h[155] = (byte)' ';
            return h;
        }

        static void Put(byte[] h, int off, string s, int len)
        {
            var b = Encoding.UTF8.GetBytes(s);
            Array.Copy(b, 0, h, off, Math.Min(b.Length, len));
        }
    }
}
