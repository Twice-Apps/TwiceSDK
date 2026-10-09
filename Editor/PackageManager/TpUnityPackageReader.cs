using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// Lists what a .unitypackage contains without importing it. The format is a
    /// gzip'd tar of "&lt;guid&gt;/pathname" (asset path, text), "&lt;guid&gt;/asset" (file
    /// body; absent for folders) and "&lt;guid&gt;/asset.meta". Knowing the guids lets
    /// the installer track exactly what a package put into the project — paths can
    /// move, guids don't — and remove it again.
    /// </summary>
    public static class TpUnityPackageReader
    {
        public sealed class Entry
        {
            public string Guid;
            public string Path;
            public bool IsFolder;   // no "asset" member
        }

        public static List<Entry> Read(string file)
        {
            var byGuid = new Dictionary<string, Entry>();
            var hasAsset = new HashSet<string>();
            using (var fs = File.OpenRead(file))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                var header = new byte[512];
                string longName = null;
                while (true)
                {
                    if (!ReadFull(gz, header, 512)) break;
                    if (IsZero(header)) break;
                    string name = Str(header, 0, 100);
                    string prefix = Str(header, 345, 155);
                    if (prefix.Length > 0 && header[257] == 'u') name = prefix + "/" + name;
                    long size = Octal(header, 124, 12);
                    char type = (char)header[156];
                    if (longName != null) { name = longName; longName = null; }

                    byte[] body = null;
                    bool wantBody = type == 'L' || type == 'x' || name.EndsWith("/pathname", StringComparison.Ordinal);
                    if (wantBody && size < 1 << 20)
                    {
                        body = new byte[size];
                        if (!ReadFull(gz, body, (int)size)) break;
                        Skip(gz, Pad(size));
                    }
                    else Skip(gz, size + Pad(size));

                    if (type == 'L' && body != null) { longName = Encoding.UTF8.GetString(body).TrimEnd('\0'); continue; }
                    if (type == 'x' && body != null) { longName = PaxPath(body); continue; }

                    name = name.TrimStart('.', '/');
                    int slash = name.IndexOf('/');
                    if (slash <= 0) continue;
                    string guid = name.Substring(0, slash);
                    string member = name.Substring(slash + 1);
                    if (member == "pathname" && body != null)
                    {
                        string path = Encoding.UTF8.GetString(body);
                        int nl = path.IndexOf('\n');
                        if (nl >= 0) path = path.Substring(0, nl);
                        path = path.Trim().Replace('\\', '/');
                        byGuid[guid] = new Entry { Guid = guid, Path = path };
                    }
                    else if (member == "asset") hasAsset.Add(guid);
                }
            }
            var list = new List<Entry>();
            foreach (var kv in byGuid)
            {
                kv.Value.IsFolder = !hasAsset.Contains(kv.Key);
                list.Add(kv.Value);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return list;
        }

        /// <summary>
        /// C# namespaces the package declares (from its .cs files). Sent with every upload so
        /// "twice packages detect" can map a missing `using X;` in a project to this package.
        /// </summary>
        public static List<string> Namespaces(string file)
        {
            var cs = new HashSet<string>();
            foreach (var e in Read(file)) if (!e.IsFolder && e.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) cs.Add(e.Guid);
            var set = new SortedSet<string>(StringComparer.Ordinal);
            if (cs.Count == 0) return new List<string>();
            var rx = new System.Text.RegularExpressions.Regex(@"^\s*namespace\s+([A-Za-z_][\w.]*)", System.Text.RegularExpressions.RegexOptions.Multiline);
            using (var fs = File.OpenRead(file))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (ReadFull(gz, header, 512) && !IsZero(header))
                {
                    string name = Str(header, 0, 100).TrimStart('.', '/');
                    long size = Octal(header, 124, 12);
                    int slash = name.IndexOf('/');
                    bool want = slash > 0 && name.Substring(slash + 1) == "asset" && cs.Contains(name.Substring(0, slash)) && size < 4 << 20;
                    if (want)
                    {
                        var body = new byte[size];
                        if (!ReadFull(gz, body, (int)size)) break;
                        Skip(gz, Pad(size));
                        foreach (System.Text.RegularExpressions.Match m in rx.Matches(Encoding.UTF8.GetString(body)))
                            if (set.Count < 300) set.Add(m.Groups[1].Value);
                    }
                    else Skip(gz, size + Pad(size));
                }
            }
            return new List<string>(set);
        }

        static string PaxPath(byte[] body)
        {
            // records: "<len> key=value\n"
            string s = Encoding.UTF8.GetString(body);
            foreach (var line in s.Split('\n'))
            {
                int sp = line.IndexOf(' ');
                if (sp < 0) continue;
                string kv = line.Substring(sp + 1);
                if (kv.StartsWith("path=", StringComparison.Ordinal)) return kv.Substring(5);
            }
            return null;
        }

        static bool ReadFull(Stream s, byte[] buf, int n)
        {
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) return false;
                off += r;
            }
            return true;
        }

        static readonly byte[] SkipBuf = new byte[81920];

        static void Skip(Stream s, long n)
        {
            while (n > 0)
            {
                int r = s.Read(SkipBuf, 0, (int)Math.Min(SkipBuf.Length, n));
                if (r <= 0) return;
                n -= r;
            }
        }

        static long Pad(long size) { return (512 - size % 512) % 512; }

        static bool IsZero(byte[] b)
        {
            for (int i = 0; i < b.Length; i++) if (b[i] != 0) return false;
            return true;
        }

        static string Str(byte[] b, int off, int len)
        {
            int end = off;
            while (end < off + len && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, off, end - off);
        }

        static long Octal(byte[] b, int off, int len)
        {
            // GNU base-256 for files > 8 GB: high bit set on the first byte.
            if ((b[off] & 0x80) != 0)
            {
                long v = 0;
                for (int i = off + 1; i < off + len; i++) v = (v << 8) | b[i];
                return v;
            }
            long r = 0;
            for (int i = off; i < off + len; i++)
            {
                byte c = b[i];
                if (c == 0 || c == ' ') { if (r > 0) break; continue; }
                if (c < '0' || c > '7') break;
                r = r * 8 + (c - '0');
            }
            return r;
        }
    }
}
