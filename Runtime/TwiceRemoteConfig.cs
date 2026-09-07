using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TwiceSDK;
using TwiceSDK.Analytics;

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

        // ---- A/B experiments --------------------------------------------------
        // The backend layers the running experiment's variant values on top of the base config
        // for players it can identify (X-User-Id). The assignment is cached with the config, the
        // ab_group / experiment_id / variant_id user properties are stamped on every analytics
        // event, and an `experiment_assigned` event is logged whenever the assignment changes.

        /// <summary>The A/B assignment from the last config fetch (cached across launches), or null when not in an experiment.</summary>
        public static ExperimentAssignment Experiment => Inst()?.Experiment;

        /// <summary>Experiment id the player is enrolled in, "" if none.</summary>
        public static string ExperimentId => Inst()?.Experiment?.id ?? "";

        /// <summary>Variant key ("A", "B", …) the player is enrolled in, "" if none.</summary>
        public static string Variant => Inst()?.Experiment?.variant ?? "";

        /// <summary>Fired when the assignment changes: enrolled, moved to another variant, or the experiment ended (null).</summary>
        public static event Action<ExperimentAssignment> OnExperimentChanged
        {
            add { TwiceRemoteConfigRunner.EnsureExists(); TwiceRemoteConfigRunner.Instance.ExperimentChanged += value; }
            remove { if (TwiceRemoteConfigRunner.Instance != null) TwiceRemoteConfigRunner.Instance.ExperimentChanged -= value; }
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
        string _endpointBaseUrl = "https://api.twiceapps.co/v1";
        bool _debug;
        bool _fetching;
        int _version;
        bool _loaded;
        JObject _config; // the "config" object from the backend, or null
        ExperimentAssignment _experiment; // A/B assignment from the last fetch (persisted), or null

        internal int Version => _version;
        internal bool Loaded => _loaded;
        internal ExperimentAssignment Experiment => _experiment;
        internal event Action Updated;
        internal event Action<ExperimentAssignment> ExperimentChanged;

        // ---- bootstrap ------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoBootstrap()
        {
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s == null) return;                                   // no asset → wait for a manual TwiceRemoteConfig.Init()/Fetch()
            if (s.initialization == InitializationMode.RequireBootstrap) return; // gated: only Twice.Initialize() may start it
            if (!s.enableRemoteConfig) return;                       // module disabled
            EnsureExists();
            Instance.ConfigureFromSettings(s);
            // Boot fetch is deferred to Twice.Initialize() step 3 (ordered sequence). The cached
            // config is already loaded in EnsureExists, so values are available immediately offline.
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
            _experiment = TwiceExperimentState.Load(); // survives offline launches, like the config itself
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
                // Identify the player so the backend can layer the running A/B experiment's variant
                // on top of the base config. First-open lets "new users only" experiments tell who
                // installed after they started. Without analytics (no user id) the plain base
                // config comes back, exactly as before.
                string uid = TwiceAnalyticsRunner.Instance != null ? TwiceAnalyticsRunner.Instance.UserId : "";
                if (!string.IsNullOrEmpty(uid))
                {
                    req.SetRequestHeader("X-User-Id", uid);
                    req.SetRequestHeader("X-First-Open", TwiceExperimentState.FirstOpenUnix.ToString());
                }
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
            // body: {"ok":true,"version":N,"config":{...},"experiment":{id,variant,rev,source}|null}
            // The "experiment" key is only present when we identified ourselves (X-User-Id).
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

            var xTok = root["experiment"];
            if (xTok != null)
            {
                ExperimentAssignment next = null;
                if (xTok.Type == JTokenType.Object)
                {
                    try { next = xTok.ToObject<ExperimentAssignment>(); } catch { next = null; }
                    if (next != null && (string.IsNullOrEmpty(next.id) || string.IsNullOrEmpty(next.variant))) next = null;
                }
                ApplyExperiment(next);
            }

            Log("updated to v" + version + " (" + _config.Count + " keys).");
            try { Updated?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[TwiceRemoteConfig] OnUpdated handler threw: " + e); }
        }

        /// <summary>
        /// Persist the assignment, stamp the analytics user properties and, when the bucket actually
        /// changed, log <c>experiment_assigned</c> so the panel can join the player to the variant.
        /// </summary>
        void ApplyExperiment(ExperimentAssignment next)
        {
            var prev = _experiment;
            bool changed = (prev == null) != (next == null) || (next != null && !next.SameAs(prev));
            _experiment = next;
            TwiceExperimentState.Save(next);
            TwiceExperimentState.ApplyUserProps(next);
            if (!changed) return;

            if (next != null)
            {
                var p = new Dictionary<string, object>
                {
                    { "experiment_id", next.id },
                    { "variant", next.variant },
                    { "prev_variant", (prev != null && prev.id == next.id) ? prev.variant : "" },
                    { "source", next.source ?? "" },
                };
                TwiceAnalytics.LogEvent("experiment_assigned", p);
                Log("experiment " + next.id + " → variant " + next.variant + " (" + next.source + ")");
            }
            else
            {
                Log("experiment ended / not enrolled — base config.");
            }
            try { ExperimentChanged?.Invoke(next); }
            catch (Exception e) { Debug.LogWarning("[TwiceRemoteConfig] OnExperimentChanged handler threw: " + e); }
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
