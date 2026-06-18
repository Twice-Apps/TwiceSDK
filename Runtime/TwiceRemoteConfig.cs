using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TwiceSDK;

namespace TwiceSDK.RemoteConfig
{
    /// <summary>
    /// Remote Config client for the Twice backend (PlayFab Title-Data style:
    /// a per-game typed key-value store). Pulls <c>GET {base}/sdk/config</c>,
    /// caches the result (offline + instant next launch) and exposes typed getters.
    /// JSON parsing is done with Newtonsoft.Json. Never throws into game code.
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

        /// <summary>Fired after a successful fetch. Subscribe to re-apply values.</summary>
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

        public static string GetString(string key, string def = "") => Inst()?.Get(key, def) ?? def;
        public static bool GetBool(string key, bool def = false) => Inst() != null ? Inst().Get(key, def) : def;
        public static int GetInt(string key, int def = 0) => Inst() != null ? Inst().Get(key, def) : def;
        public static long GetLong(string key, long def = 0L) => Inst() != null ? Inst().Get(key, def) : def;
        public static float GetFloat(string key, float def = 0f) => Inst() != null ? Inst().Get(key, def) : def;
        public static double GetDouble(string key, double def = 0d) => Inst() != null ? Inst().Get(key, def) : def;

        /// <summary>Raw JSON text of a key's value (object/array/scalar), or null if absent.</summary>
        public static string GetRawJson(string key) => Inst()?.GetRaw(key);

        /// <summary>Deserialize a json-typed key into <typeparamref name="T"/> via Newtonsoft (POCO / [Serializable]).</summary>
        public static T GetJson<T>(string key) => Inst() != null ? Inst().GetJson<T>(key) : default;

        static TwiceRemoteConfigRunner Inst() => TwiceRemoteConfigRunner.Instance;
        static void Guard(Action a) { try { a(); } catch (Exception e) { Debug.LogWarning("[TwiceRemoteConfig] swallowed: " + e); } }
    }

    /// <summary>Internal engine: persistent singleton owning the cached config and the fetch coroutine.</summary>
    internal class TwiceRemoteConfigRunner : MonoBehaviour
    {
        internal static TwiceRemoteConfigRunner Instance { get; private set; }

        const string CacheKey = "twice_rc_config";    // raw config object json
        const string VersionKey = "twice_rc_version"; // cached version

        string _apiKey;
        string _endpointBaseUrl = "https://www.twiceapps.co/api/v1";
        bool _debug;
        bool _autoFetch = true;
        bool _fetching;
        int _version;
        bool _loaded;
        JObject _config; // the "config" object from the backend, or null

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
            if (string.IsNullOrEmpty(raw)) return;
            try { _config = JObject.Parse(raw); _loaded = true; }
            catch (Exception e) { Log("cache parse failed: " + e.Message); }
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
                    catch (Exception e) { Log("apply failed: " + e.Message); ok = false; }
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
            var root = JObject.Parse(body);

            int version = _version;
            var vTok = root["version"];
            if (vTok != null) { try { version = vTok.ToObject<int>(); } catch { } }

            _config = root["config"] as JObject ?? new JObject();
            _version = version;
            _loaded = true;

            PlayerPrefs.SetString(CacheKey, _config.ToString(Formatting.None));
            PlayerPrefs.SetInt(VersionKey, version);
            PlayerPrefs.Save();

            Log("updated to v" + version + " (" + _config.Count + " keys).");
            try { Updated?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[TwiceRemoteConfig] OnUpdated handler threw: " + e); }
        }

        // ---- typed getters --------------------------------------------------

        internal bool Has(string key) => _config != null && _config[key] != null;

        internal string[] Keys
        {
            get
            {
                if (_config == null) return Array.Empty<string>();
                var list = new List<string>();
                foreach (var p in _config.Properties()) list.Add(p.Name);
                return list.ToArray();
            }
        }

        internal string GetRaw(string key)
        {
            var t = _config?[key];
            return t == null ? null : t.ToString(Formatting.None);
        }

        internal T GetJson<T>(string key)
        {
            var t = _config?[key];
            if (t == null || t.Type == JTokenType.Null) return default;
            try { return t.ToObject<T>(); }
            catch (Exception e) { Log("GetJson<" + typeof(T).Name + ">(\"" + key + "\") failed: " + e.Message); return default; }
        }

        internal T Get<T>(string key, T def)
        {
            var t = _config?[key];
            if (t == null || t.Type == JTokenType.Null) return def;
            try { return t.ToObject<T>(); }
            catch { return def; }
        }

        // ---- log ------------------------------------------------------------

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Log(string msg) { if (_debug) Debug.Log("[TwiceRemoteConfig] " + msg); }
    }
}
