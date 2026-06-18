using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TwiceSDK;

namespace TwiceSDK.RemoteConfig
{
    /// <summary>
    /// Remote Config client for the Twice backend (PlayFab Title-Data style:
    /// a per-game typed key-value store). Pulls <c>GET {base}/sdk/config</c>,
    /// caches the result (offline + instant next launch) and exposes typed getters.
    /// Dependency-free, WebGL-safe, never throws into game code.
    ///
    /// Usage:
    /// <code>
    /// bool ads   = TwiceRemoteConfig.GetBool("ads_enabled", true);
    /// int coins  = TwiceRemoteConfig.GetInt("coins_per_level", 50);
    /// var s      = TwiceRemoteConfig.GetJson&lt;GameSettings&gt;("GameSettings");
    /// TwiceRemoteConfig.OnUpdated += () => ApplyConfig();
    /// </code>
    /// </summary>
    public static class TwiceRemoteConfig
    {
        /// <summary>Config version returned by the backend (bumps on every change). 0 if never loaded.</summary>
        public static int Version => TwiceRemoteConfigRunner.Instance != null ? TwiceRemoteConfigRunner.Instance.Version : 0;

        /// <summary>True once a config (live or cached) has been loaded.</summary>
        public static bool IsReady => TwiceRemoteConfigRunner.Instance != null && TwiceRemoteConfigRunner.Instance.Loaded;

        /// <summary>Fired after a successful fetch that changed the config. Subscribe to re-apply values.</summary>
        public static event Action OnUpdated
        {
            add { TwiceRemoteConfigRunner.EnsureExists(); TwiceRemoteConfigRunner.Instance.Updated += value; }
            remove { if (TwiceRemoteConfigRunner.Instance != null) TwiceRemoteConfigRunner.Instance.Updated -= value; }
        }

        /// <summary>Optional manual init (skip if a TwiceSettings asset is in Resources).</summary>
        public static void Init(string apiKey = null, string endpointBaseUrl = null) => Guard(() =>
        {
            TwiceRemoteConfigRunner.EnsureExists();
            TwiceRemoteConfigRunner.Instance.Configure(apiKey, endpointBaseUrl);
        });

        /// <summary>Fetch the latest config from the backend. <paramref name="onComplete"/>(true) on success.</summary>
        public static void Fetch(Action<bool> onComplete = null) => Guard(() =>
        {
            TwiceRemoteConfigRunner.EnsureExists();
            TwiceRemoteConfigRunner.Instance.Fetch(onComplete);
        });

        public static bool HasKey(string key) => Inst()?.Has(key) ?? false;
        public static string[] Keys => Inst()?.Keys ?? Array.Empty<string>();

        public static string GetString(string key, string def = "") => Inst()?.GetString(key, def) ?? def;
        public static bool GetBool(string key, bool def = false) => Inst()?.GetBool(key, def) ?? def;
        public static int GetInt(string key, int def = 0) => Inst()?.GetInt(key, def) ?? def;
        public static long GetLong(string key, long def = 0L) => Inst()?.GetLong(key, def) ?? def;
        public static float GetFloat(string key, float def = 0f) => Inst()?.GetFloat(key, def) ?? def;
        public static double GetDouble(string key, double def = 0d) => Inst()?.GetDouble(key, def) ?? def;

        /// <summary>Raw JSON text of a key's value (object/array/scalar), or null if absent.</summary>
        public static string GetRawJson(string key) => Inst()?.GetRaw(key);

        /// <summary>Deserialize a json-typed key into <typeparamref name="T"/> via Unity's JsonUtility (use a [Serializable] class).</summary>
        public static T GetJson<T>(string key)
        {
            string raw = GetRawJson(key);
            if (string.IsNullOrEmpty(raw)) return default;
            try { return JsonUtility.FromJson<T>(raw); }
            catch (Exception e) { Debug.LogWarning("[TwiceRemoteConfig] GetJson<" + typeof(T).Name + ">(\"" + key + "\") failed: " + e.Message); return default; }
        }

        static TwiceRemoteConfigRunner Inst() => TwiceRemoteConfigRunner.Instance;
        static void Guard(Action a) { try { a(); } catch (Exception e) { Debug.LogWarning("[TwiceRemoteConfig] swallowed: " + e); } }
    }

    /// <summary>Internal engine: persistent singleton owning the cached config and the fetch coroutine.</summary>
    internal class TwiceRemoteConfigRunner : MonoBehaviour
    {
        internal static TwiceRemoteConfigRunner Instance { get; private set; }

        const string CacheKey = "twice_rc_config";   // raw config object json
        const string VersionKey = "twice_rc_version"; // cached version

        string _apiKey;
        string _endpointBaseUrl = "https://www.twiceapps.co/api/v1";
        bool _debug;
        bool _autoFetch = true;
        bool _fetching;
        int _version;
        bool _loaded;
        readonly Dictionary<string, string> _raw = new Dictionary<string, string>(); // key -> raw JSON value text

        internal int Version => _version;
        internal bool Loaded => _loaded;
        internal event Action Updated;

        // ---- bootstrap ------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoBootstrap()
        {
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s == null) return; // no asset → wait for a manual TwiceRemoteConfig.Init()/Fetch()
            EnsureExists();
            Instance.ConfigureFromSettings(s);
            if (Instance._autoFetch && !string.IsNullOrEmpty(Instance._apiKey))
                Instance.Fetch(null);
        }

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("[TwiceRemoteConfig]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            Instance = go.AddComponent<TwiceRemoteConfigRunner>();
            Instance.LoadCache();
        }

        void LoadCache()
        {
            _version = PlayerPrefs.GetInt(VersionKey, 0);
            string raw = PlayerPrefs.GetString(CacheKey, "");
            if (!string.IsNullOrEmpty(raw))
            {
                ParseObjectRaw(raw, _raw);
                _loaded = true;
            }
        }

        internal void ConfigureFromSettings(TwiceSettings s)
        {
            if (!string.IsNullOrEmpty(s.endpointBaseUrl)) _endpointBaseUrl = s.endpointBaseUrl.Trim();
            if (!string.IsNullOrEmpty(s.apiKey)) _apiKey = s.apiKey.Trim();
            _debug = s.debugLogging;
            _autoFetch = s.autoFetchRemoteConfig;
        }

        internal void Configure(string apiKey, string baseUrl)
        {
            if (!string.IsNullOrEmpty(apiKey)) _apiKey = apiKey.Trim();
            if (!string.IsNullOrEmpty(baseUrl)) _endpointBaseUrl = baseUrl.Trim();
        }

        // ---- fetch ----------------------------------------------------------

        internal void Fetch(Action<bool> cb)
        {
            if (_fetching) { cb?.Invoke(false); return; }
            if (string.IsNullOrEmpty(_apiKey))
            {
                Debug.LogWarning("[TwiceRemoteConfig] No API key set. Paste your X-App-Key into TwiceSettings or call TwiceRemoteConfig.Init(apiKey).");
                cb?.Invoke(false);
                return;
            }
            if (!isActiveAndEnabled) { cb?.Invoke(false); return; }
            StartCoroutine(FetchRoutine(cb));
        }

        IEnumerator FetchRoutine(Action<bool> cb)
        {
            _fetching = true;
            string url = _endpointBaseUrl.TrimEnd('/') + "/sdk/config";
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("X-App-Key", _apiKey);
                req.timeout = 20;
                yield return req.SendWebRequest();

                bool ok = req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
                if (ok)
                {
                    try { ApplyResponse(req.downloadHandler.text); }
                    catch (Exception e) { Log("parse failed: " + e.Message); ok = false; }
                }
                else
                {
                    Log("fetch failed: code=" + req.responseCode + " err=" + req.error);
                }
                _fetching = false;
                cb?.Invoke(ok);
            }
        }

        void ApplyResponse(string body)
        {
            // body: {"ok":true,"version":N,"config":{...}}
            var top = new Dictionary<string, string>();
            ParseObjectRaw(body, top);

            int version = _version;
            if (top.TryGetValue("version", out var vRaw))
                int.TryParse(vRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);

            string configRaw = top.TryGetValue("config", out var cRaw) ? cRaw : "{}";

            var map = new Dictionary<string, string>();
            ParseObjectRaw(configRaw, map);

            _raw.Clear();
            foreach (var kv in map) _raw[kv.Key] = kv.Value;
            _version = version;
            _loaded = true;

            PlayerPrefs.SetString(CacheKey, configRaw);
            PlayerPrefs.SetInt(VersionKey, version);
            PlayerPrefs.Save();

            Log("updated to v" + version + " (" + _raw.Count + " keys).");
            try { Updated?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[TwiceRemoteConfig] OnUpdated handler threw: " + e); }
        }

        // ---- typed getters --------------------------------------------------

        internal bool Has(string key) => _raw.ContainsKey(key);

        internal string[] Keys
        {
            get { var arr = new string[_raw.Count]; _raw.Keys.CopyTo(arr, 0); return arr; }
        }

        internal string GetRaw(string key) => _raw.TryGetValue(key, out var v) ? v : null;

        internal string GetString(string key, string def)
        {
            if (!_raw.TryGetValue(key, out var raw)) return def;
            raw = raw.Trim();
            if (raw.Length >= 2 && raw[0] == '"')
            {
                int i = 0;
                return ReadString(raw, ref i);
            }
            return raw; // number/bool stored without quotes
        }

        internal bool GetBool(string key, bool def)
        {
            if (!_raw.TryGetValue(key, out var raw)) return def;
            raw = raw.Trim();
            if (raw == "true") return true;
            if (raw == "false") return false;
            return def;
        }

        internal double GetDouble(string key, double def)
        {
            if (!_raw.TryGetValue(key, out var raw)) return def;
            return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
        }

        internal int GetInt(string key, int def)
        {
            if (!_raw.TryGetValue(key, out var raw)) return def;
            string t = raw.Trim();
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (int)d;
            return def;
        }

        internal long GetLong(string key, long def)
        {
            if (!_raw.TryGetValue(key, out var raw)) return def;
            string t = raw.Trim();
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (long)d;
            return def;
        }

        internal float GetFloat(string key, float def)
        {
            double d = GetDouble(key, double.NaN);
            return double.IsNaN(d) ? def : (float)d;
        }

        // ---- minimal JSON scanner (dependency-free) -------------------------
        // Parses a flat JSON object into key -> raw value text (value left verbatim:
        // strings keep their quotes, objects/arrays keep their braces). Nested
        // structures are preserved as raw text for GetRawJson / GetJson<T>.

        static void ParseObjectRaw(string s, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(s)) return;
            int i = 0;
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '{') return;
            i++; // {
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; break; }
                if (i >= s.Length || s[i] != '"') break;
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') break;
                i++; // :
                SkipWs(s, ref i);
                int start = i;
                SkipValue(s, ref i);
                map[key] = s.Substring(start, i - start).Trim();
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        static void SkipValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return;
            char c = s[i];
            if (c == '"') { SkipString(s, ref i); }
            else if (c == '{' || c == '[') { SkipContainer(s, ref i); }
            else
            {
                while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;
            }
        }

        static void SkipString(string s, ref int i)
        {
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '\\') { if (i < s.Length) i++; }
                else if (c == '"') break;
            }
        }

        static void SkipContainer(string s, ref int i)
        {
            char open = s[i];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"') { SkipString(s, ref i); continue; }
                if (c == open) { depth++; i++; continue; }
                if (c == close) { depth--; i++; if (depth == 0) return; continue; }
                i++;
            }
        }

        static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '\\')
                {
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ---- lifecycle / log ------------------------------------------------

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Log(string msg) { if (_debug) Debug.Log("[TwiceRemoteConfig] " + msg); }
    }
}
