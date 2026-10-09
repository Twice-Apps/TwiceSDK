using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// Machine-wide preferences (EditorPrefs): hub address, token, cache folder.
    /// The token is never written into the project. When no Twice Packages token is
    /// set, the PlayTwice token of this project is used — both are the same ptk_ token.
    /// </summary>
    public static class TpSettings
    {
        const string P = "TwicePackages.";
        /// <summary>Fixed on purpose: there is one twicehub, and a settable address is a place to point a token at the wrong server.</summary>
        public const string HubBase = "https://hub.twiceapps.co";

        public static string Token
        {
            get { return EditorPrefs.GetString(P + "Token", ""); }
            set { EditorPrefs.SetString(P + "Token", (value ?? "").Trim()); }
        }

        /// <summary>Same key PlayTwiceSettings uses (per machine + project).</summary>
        static string PlayTwiceTokenKey()
        {
            string proj = Directory.GetParent(Application.dataPath).FullName.ToLowerInvariant();
            int h = 23;
            foreach (char c in proj) h = h * 31 + c;
            return "PlayTwice.Token." + (h & 0x7fffffff).ToString("x");
        }

        public static string PlayTwiceToken { get { return EditorPrefs.GetString(PlayTwiceTokenKey(), ""); } }

        /// <summary>
        /// No token, no catalog: this machine must be connected to twicehub first
        /// ("Twicehub ile bağlan" → approve in the panel), or use this project's PlayTwice token.
        /// The token lives only in this machine's EditorPrefs and can be revoked from the panel.
        /// </summary>
        public static string EffectiveToken
        {
            get { return string.IsNullOrEmpty(Token) ? PlayTwiceToken : Token; }
        }

        public static bool UsingPlayTwiceToken { get { return string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(PlayTwiceToken); } }
        public static bool HasToken { get { return !string.IsNullOrEmpty(EffectiveToken); } }

        /// <summary>Shown in the panel's approval screen and device list.</summary>
        public static string MachineLabel
        {
            get
            {
                string u = CloudProjectSettings.userName;
                string who = string.IsNullOrEmpty(u) || u == "anonymous" ? Environment.UserName : u;
                return (who + " @ " + SystemInfo.deviceName).Trim();
            }
        }

        /// <summary>Signed in as (from the last successful catalog/me call).</summary>
        public static string ConnectedAs
        {
            get { return EditorPrefs.GetString(P + "ConnectedAs", ""); }
            set { EditorPrefs.SetString(P + "ConnectedAs", value ?? ""); }
        }

        /// <summary>Show Unity's "Import Unity Package" dialog (pick files) instead of importing everything.</summary>
        public static bool InteractiveImport
        {
            get { return EditorPrefs.GetBool(P + "InteractiveImport", false); }
            set { EditorPrefs.SetBool(P + "InteractiveImport", value); }
        }

        /// <summary>On update, offer to delete files the old version had and the new one does not.</summary>
        public static bool CleanUpdate
        {
            get { return EditorPrefs.GetBool(P + "CleanUpdate", true); }
            set { EditorPrefs.SetBool(P + "CleanUpdate", value); }
        }

        public static bool Verbose
        {
            get { return EditorPrefs.GetBool(P + "Verbose", false); }
            set { EditorPrefs.SetBool(P + "Verbose", value); }
        }

        public static string CustomCacheDir
        {
            get { return EditorPrefs.GetString(P + "CacheDir", ""); }
            set { EditorPrefs.SetString(P + "CacheDir", (value ?? "").Trim()); }
        }

        public static string DefaultCacheDir
        {
            get
            {
#if UNITY_EDITOR_OSX
                string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                return Path.Combine(home, "Library/Caches/TwicePackages").Replace('\\', '/');
#else
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwicePackages").Replace('\\', '/');
#endif
            }
        }

        public static string CacheDir
        {
            get
            {
                string c = CustomCacheDir;
                return string.IsNullOrEmpty(c) ? DefaultCacheDir : c.Replace('\\', '/');
            }
        }

        /* ---- favourites: personal, not shared ------------------------------ */

        public static HashSet<string> Favorites
        {
            get
            {
                var s = new HashSet<string>(StringComparer.Ordinal);
                foreach (var x in EditorPrefs.GetString(P + "Favorites", "").Split('|'))
                    if (x.Length > 0) s.Add(x);
                return s;
            }
        }

        public static void SetFavorite(string slug, bool on)
        {
            var s = Favorites;
            if (on) s.Add(slug); else s.Remove(slug);
            EditorPrefs.SetString(P + "Favorites", string.Join("|", s));
        }

        /* ---- window prefs ---------------------------------------------------- */

        public static int GetInt(string key, int def) { return EditorPrefs.GetInt(P + key, def); }
        public static void SetInt(string key, int v) { EditorPrefs.SetInt(P + key, v); }
        public static string GetString(string key, string def) { return EditorPrefs.GetString(P + key, def); }
        public static void SetString(string key, string v) { EditorPrefs.SetString(P + key, v ?? ""); }
    }

    public static class TpLog
    {
        const string Tag = "[Twice Packages] ";
        public static void Info(string m) { if (TpSettings.Verbose) Debug.Log(Tag + m); }
        public static void Note(string m) { Debug.Log(Tag + m); }
        public static void Warn(string m) { Debug.LogWarning(Tag + m); }
        public static void Error(string m) { Debug.LogError(Tag + m); }
    }

    /// <summary>1.2.3(.4)(-pre)(+build) comparison; a pre-release sorts before its release.</summary>
    public static class TpSemVer
    {
        public static int Compare(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            string aCore, aPre, bCore, bPre;
            Split(a, out aCore, out aPre);
            Split(b, out bCore, out bPre);
            var ap = aCore.Split('.');
            var bp = bCore.Split('.');
            int n = Math.Max(ap.Length, bp.Length);
            for (int i = 0; i < n; i++)
            {
                long x = i < ap.Length ? ParseNum(ap[i]) : 0;
                long y = i < bp.Length ? ParseNum(bp[i]) : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            if (aPre.Length == 0 && bPre.Length == 0) return 0;
            if (aPre.Length == 0) return 1;
            if (bPre.Length == 0) return -1;
            return string.CompareOrdinal(aPre, bPre);
        }

        static void Split(string v, out string core, out string pre)
        {
            int plus = v.IndexOf('+');
            if (plus >= 0) v = v.Substring(0, plus);
            int dash = v.IndexOf('-');
            core = dash >= 0 ? v.Substring(0, dash) : v;
            pre = dash >= 0 ? v.Substring(dash + 1) : "";
        }

        static long ParseNum(string s)
        {
            long r;
            return long.TryParse(s, out r) ? r : 0;
        }

        public static bool IsValid(string v)
        {
            return !string.IsNullOrEmpty(v) && v.Length <= 40 &&
                   System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+(\.\d+){0,3}([\-+][0-9A-Za-z.\-]+)?$");
        }

        /// <summary>1.2.3 → 1.2.4; anything odd → 1.0.0.</summary>
        public static string BumpPatch(string v)
        {
            if (!IsValid(v)) return "1.0.0";
            string core, pre;
            Split(v, out core, out pre);
            var p = new List<string>(core.Split('.'));
            while (p.Count < 3) p.Add("0");
            p[p.Count - 1] = (ParseNum(p[p.Count - 1]) + 1).ToString();
            return string.Join(".", p);
        }
    }

    public static class TpFormat
    {
        public static string Size(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double s = bytes;
            while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
            return (i == 0 ? ((long)s).ToString() : s.ToString("0.#")) + " " + u[i];
        }

        public static string Date(string iso)
        {
            DateTime d;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out d)) return "-";
            return d.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        }

        public static string Slugify(string name)
        {
            string s = (name ?? "").ToLowerInvariant()
                .Replace('ç', 'c').Replace('ğ', 'g').Replace('ı', 'i').Replace('ö', 'o').Replace('ş', 's').Replace('ü', 'u');
            s = System.Text.RegularExpressions.Regex.Replace(s, "[^a-z0-9]+", "-").Trim('-');
            return s.Length > 63 ? s.Substring(0, 63) : s;
        }

        public static string[] SplitList(string csv)
        {
            var list = new List<string>();
            foreach (var x in (csv ?? "").Split(','))
            {
                var t = x.Trim();
                if (t.Length > 0 && !list.Contains(t)) list.Add(t);
            }
            return list.ToArray();
        }
    }
}
