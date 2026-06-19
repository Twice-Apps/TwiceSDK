using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using TwiceSDK;
using TwiceSDK.VersionCheck;

namespace TwiceSDK.Analytics
{
    /// <summary>
    /// Drop-in, dependency-free analytics client for the Twice backend.
    /// All public methods are main-thread safe, non-blocking, and never throw into game code.
    /// </summary>
    public static class TwiceAnalytics
    {
        /// <summary>Optional runtime overrides for <see cref="Init"/>. Any null field falls back to the settings asset.</summary>
        public class Options
        {
            public string endpointBaseUrl;
            public bool? autoTrackSessions;
            public int? flushIntervalSeconds;
            public int? maxBatchSize;
            public bool? debugLogging;
            /// <summary>Force sandbox (true) / production (false) tagging. Null = use the settings/Auto rule.</summary>
            public bool? sandbox;
        }

        // ---- Public API -----------------------------------------------------

        /// <summary>Manually initialise the SDK with a project API key (overrides the settings asset key).</summary>
        public static void Init(string apiKey, Options options = null)
        {
            Guard(() =>
            {
                TwiceAnalyticsRunner.EnsureExists();
                TwiceAnalyticsRunner.Instance.Configure(apiKey, options);
            });
        }

        /// <summary>Enable/disable collection (GDPR/KVKK). When false, queued events are cleared and nothing is sent.</summary>
        public static void SetConsent(bool granted) => Guard(() => TwiceAnalyticsRunner.Instance?.SetConsent(granted));

        /// <summary>Force sandbox (true) or production (false) tagging at runtime (overrides settings/Auto).</summary>
        public static void SetSandbox(bool sandbox) => Guard(() => TwiceAnalyticsRunner.Instance?.SetSandbox(sandbox));

        /// <summary>Attach a property that is merged into every subsequent event's params.</summary>
        public static void SetUserProperty(string key, object value) =>
            Guard(() => TwiceAnalyticsRunner.Instance?.SetUserProperty(key, value));

        /// <summary>Log an arbitrary event with optional flat params (number/string/bool).</summary>
        public static void LogEvent(string name, IDictionary<string, object> parameters = null) =>
            Guard(() => TwiceAnalyticsRunner.Instance?.Enqueue(name, parameters));

        // ---- Preset helpers -------------------------------------------------

        public static void LevelStarted(string level, IDictionary<string, object> extra = null) =>
            LogEvent("level_started", With(extra, "level", level));

        public static void LevelCompleted(string level, int score = 0, float duration = 0f, IDictionary<string, object> extra = null) =>
            LogEvent("level_completed", With(extra, ("level", level), ("score", score), ("duration", duration)));

        public static void LevelFailed(string level, string reason = null, IDictionary<string, object> extra = null) =>
            LogEvent("level_failed", reason == null
                ? With(extra, "level", level)
                : With(extra, ("level", level), ("reason", reason)));

        public static void TutorialCompleted(string step = null, IDictionary<string, object> extra = null) =>
            LogEvent("tutorial_completed", step == null ? extra : With(extra, "step", step));

        public static void ScreenView(string screen) => LogEvent("screen_view", With(null, "screen", screen));

        public static void Purchase(string productId, double price, string currency, IDictionary<string, object> extra = null) =>
            LogEvent("purchase", With(extra, ("product_id", productId), ("price", price), ("currency", currency)));

        public static void AdWatched(string placement, IDictionary<string, object> extra = null) =>
            LogEvent("ad_watched", With(extra, "placement", placement));

        /// <summary>
        /// Impression-level ad revenue from a mediation callback (AppLovin MAX,
        /// AdMob, LevelPlay…). <paramref name="revenue"/> is in USD — those SDKs
        /// already report USD. <paramref name="network"/> is the paying ad network;
        /// <paramref name="placement"/> and <paramref name="adFormat"/>
        /// ("rewarded" / "interstitial" / "banner") are optional but power the
        /// network/placement breakdowns in the dashboard.
        /// </summary>
        public static void AdRevenue(double revenue, string network, string placement = null,
                                     string adFormat = null, IDictionary<string, object> extra = null)
        {
            var p = With(extra, ("revenue", revenue), ("network", string.IsNullOrEmpty(network) ? "unknown" : network));
            if (!string.IsNullOrEmpty(placement)) p["placement"] = placement;
            if (!string.IsNullOrEmpty(adFormat)) p["ad_format"] = adFormat;
            LogEvent("ad_revenue", p);
        }

        public static void RewardClaimed(string reward, IDictionary<string, object> extra = null) =>
            LogEvent("reward_claimed", With(extra, "reward", reward));

        /// <summary>Request an immediate (asynchronous) flush of the queued events.</summary>
        public static void Flush() => Guard(() => TwiceAnalyticsRunner.Instance?.RequestFlush());

        /// <summary>The persistent anonymous user id (the same id sent with every event,
        /// used to attribute leaderboard scores and purchases). "" before initialization.</summary>
        public static string UserId => GetDebugInfo().userId ?? "";

        // ---- debug / tooling ------------------------------------------------

        /// <summary>Read-only snapshot of the SDK state, used by the editor debugger window.</summary>
        public struct DebugInfo
        {
            public bool initialized;
            public bool consent;
            public string environment; // "sandbox" | "production"
            public int pending;
            public string userId;
            public string sessionId;
            public string platform;
            public string appVersion;
            public string buildNumber;
            public string endpoint;
            public string apiKeyMasked;
            public string lastStatus;
            public string[] pendingEvents; // "name @unixTs" for the leading queued events
        }

        /// <summary>Returns a live snapshot of the SDK state (safe to call any time).</summary>
        public static DebugInfo GetDebugInfo()
        {
            var r = TwiceAnalyticsRunner.Instance;
            if (r == null) return new DebugInfo { initialized = false, lastStatus = "Not initialized" };
            return r.BuildDebugInfo();
        }

        // ---- helpers --------------------------------------------------------

        static IDictionary<string, object> With(IDictionary<string, object> extra, string key, object value)
        {
            var d = extra != null ? new Dictionary<string, object>(extra) : new Dictionary<string, object>();
            d[key] = value;
            return d;
        }

        static IDictionary<string, object> With(IDictionary<string, object> extra, params (string key, object value)[] pairs)
        {
            var d = extra != null ? new Dictionary<string, object>(extra) : new Dictionary<string, object>();
            foreach (var p in pairs) d[p.key] = p.value;
            return d;
        }

        static void Guard(Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogWarning("[TwiceAnalytics] swallowed exception: " + e); }
        }
    }

    /// <summary>Internal engine: persistent singleton owning the queue, lifecycle and network coroutines.</summary>
    internal class TwiceAnalyticsRunner : MonoBehaviour
    {
        internal static TwiceAnalyticsRunner Instance { get; private set; }

        const string UserIdKey = "twice_analytics_user_id";
        const string ConsentKey = "twice_analytics_consent";
        const string QueuePrefsKey = "twice_analytics_queue"; // WebGL fallback
        const string QueueFileName = "twice_analytics_queue.tsv";
        const int MaxQueue = 1000;
        const float MaxBackoffSeconds = 60f;
        const double NewSessionAfterMinutes = 30.0;

        struct QueuedEvent
        {
            public string eventId;    // unique per event (GUID) for backend de-duplication on resend
            public string sessionId;
            public long ts;
            public string name;
            public string paramsJson; // pre-serialized JSON object, "{}" when empty
        }

        // config
        string _apiKey;
        string _endpointBaseUrl = "https://www.twiceapps.co/api/v1";
        bool _autoTrackSessions = true;
        int _flushIntervalSeconds = 15;
        int _maxBatchSize = 20;
        bool _debugLogging;
        EnvironmentMode _environment = EnvironmentMode.Auto;
        bool? _sandboxOverride; // runtime/Init override; null = use _environment rule

        // identity / session
        string _userId;
        string _sessionId;
        string _platform;
        string _appVersion;
        string _buildNumber; // owned by TwiceVersionChecker; cached here for the event envelope
        DateTime _sessionStartUtc;
        DateTime? _backgroundSinceUtc;

        // state
        readonly List<QueuedEvent> _queue = new List<QueuedEvent>();
        readonly object _queueLock = new object();
        readonly Dictionary<string, object> _userProps = new Dictionary<string, object>();
        bool _consent = true;
        bool _configured;
        bool _sending;
        float _currentDelay = 15f;
        string _queueFilePath;
        string _lastStatus = "Idle";

        // ---- bootstrap ------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoBootstrap()
        {
            var settings = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (settings == null) return;                                   // no asset → wait for a manual TwiceAnalytics.Init()
            if (settings.initialization == InitializationMode.RequireBootstrap) return; // gated: only Twice.Initialize() (TwiceBootstrap) may start it
            if (!settings.enableAnalytics) return;                          // module disabled
            EnsureExists();
            Instance.ConfigureFromSettings(settings);
        }

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("[TwiceAnalytics]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            Instance = go.AddComponent<TwiceAnalyticsRunner>();
            Instance.InitIdentity();
        }

        void InitIdentity()
        {
            _userId = PlayerPrefs.GetString(UserIdKey, null);
            if (string.IsNullOrEmpty(_userId))
            {
                _userId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(UserIdKey, _userId);
                PlayerPrefs.Save();
            }
            _consent = PlayerPrefs.GetInt(ConsentKey, 1) == 1;
            _platform = ResolvePlatform();
            _appVersion = Application.version;
            _buildNumber = TwiceVersionChecker.BuildNumber; // platform build no (version checker's concern)
            _queueFilePath = Path.Combine(Application.persistentDataPath, QueueFileName);
        }

        // ---- configuration --------------------------------------------------

        internal void ConfigureFromSettings(TwiceSettings s)
        {
            _endpointBaseUrl = Trim(s.endpointBaseUrl, _endpointBaseUrl);
            _autoTrackSessions = s.autoTrackSessions;
            _flushIntervalSeconds = Mathf.Max(1, s.flushIntervalSeconds);
            _maxBatchSize = Mathf.Clamp(s.maxBatchSize, 1, 200);
            _debugLogging = s.debugLogging;
            _environment = s.environment;
#if UNITY_EDITOR
            // Editor testing: report a real store platform so this build's version reaches the
            // Version Checker (events tagged Editor are otherwise ignored there). Sandbox-only.
            if (s.editorPlatformOverride != EditorPlatform.None)
            {
                _platform = s.editorPlatformOverride.ToString();
                _buildNumber = TwiceVersionChecker.EditorBuildFor(_platform);
            }
#endif
            Configure(s.apiKey, null);
        }

        internal void Configure(string apiKey, TwiceAnalytics.Options o)
        {
            if (!string.IsNullOrEmpty(apiKey)) _apiKey = apiKey.Trim();
            if (o != null)
            {
                if (!string.IsNullOrEmpty(o.endpointBaseUrl)) _endpointBaseUrl = o.endpointBaseUrl.Trim();
                if (o.autoTrackSessions.HasValue) _autoTrackSessions = o.autoTrackSessions.Value;
                if (o.flushIntervalSeconds.HasValue) _flushIntervalSeconds = Mathf.Max(1, o.flushIntervalSeconds.Value);
                if (o.maxBatchSize.HasValue) _maxBatchSize = Mathf.Clamp(o.maxBatchSize.Value, 1, 200);
                if (o.debugLogging.HasValue) _debugLogging = o.debugLogging.Value;
                if (o.sandbox.HasValue) _sandboxOverride = o.sandbox.Value;
            }
            _currentDelay = _flushIntervalSeconds;

            if (string.IsNullOrEmpty(_apiKey))
                Debug.LogWarning("[TwiceAnalytics] No API key set. Paste your X-App-Key into TwiceSettings or call TwiceAnalytics.Init(apiKey).");

            if (!_configured)
            {
                _configured = true;
                Begin();
            }
        }

        void Begin()
        {
            LoadPersistedQueue();
            StartNewSession(logStart: _autoTrackSessions);
            StartCoroutine(FlushLoop());
            // Boot flush is deferred to Twice.Initialize() step 2 so the ordered sequence
            // (version check → analytics → remote config) holds. Without the bootstrap object,
            // the periodic FlushLoop still sends shortly after.
        }

        // ---- session --------------------------------------------------------

        void StartNewSession(bool logStart)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _sessionStartUtc = DateTime.UtcNow;
            if (logStart)
                Enqueue("session_start", new Dictionary<string, object>
                {
                    { "device_model", SystemInfo.deviceModel },
                    { "os", SystemInfo.operatingSystem },
                    { "language", Application.systemLanguage.ToString() },
                    { "screen", Screen.width + "x" + Screen.height },
                });
        }

        void EndSession()
        {
            if (!_autoTrackSessions) return;
            double duration = (DateTime.UtcNow - _sessionStartUtc).TotalSeconds;
            Enqueue("session_end", new Dictionary<string, object> { { "duration", Math.Round(duration, 2) } });
        }

        // ---- public-facing engine ops --------------------------------------

        internal void SetConsent(bool granted)
        {
            _consent = granted;
            PlayerPrefs.SetInt(ConsentKey, granted ? 1 : 0);
            PlayerPrefs.Save();
            if (!granted)
            {
                lock (_queueLock) _queue.Clear();
                PersistQueue();
                Log("Consent revoked — queue cleared, collection paused.");
            }
            else Log("Consent granted.");
        }

        internal void SetUserProperty(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _userProps[key] = value;
        }

        internal void SetSandbox(bool sandbox)
        {
            _sandboxOverride = sandbox;
            Log("Environment set to " + (sandbox ? "sandbox" : "production"));
        }

        /// <summary>Resolves whether events are tagged sandbox. Priority: runtime/Init override → settings mode → Auto rule.</summary>
        bool IsSandbox
        {
            get
            {
                if (_sandboxOverride.HasValue) return _sandboxOverride.Value;
                switch (_environment)
                {
                    case EnvironmentMode.Sandbox: return true;
                    case EnvironmentMode.Production: return false;
                    default: // Auto
#if TWICE_SANDBOX
                        return true;
#else
                        if (Application.isEditor || Debug.isDebugBuild) return true;
                        return IosReceiptIsSandbox(); // TestFlight/sandbox receipt → sandbox (release iOS build)
#endif
                }
            }
        }

        internal void Enqueue(string name, IDictionary<string, object> parameters)
        {
            if (!_consent) return;

            string clean = SanitizeName(name);
            // Merge user properties (event params win on key collision).
            IDictionary<string, object> merged = parameters;
            if (_userProps.Count > 0)
            {
                merged = new Dictionary<string, object>(_userProps);
                if (parameters != null)
                    foreach (var kv in parameters) merged[kv.Key] = kv.Value;
            }
            string json = merged != null && merged.Count > 0 ? JsonConvert.SerializeObject(merged) : "{}";

            var ev = new QueuedEvent
            {
                eventId = Guid.NewGuid().ToString("N"),
                sessionId = _sessionId,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                name = clean,
                paramsJson = json,
            };

            int count;
            bool triggerFlush;
            lock (_queueLock)
            {
                _queue.Add(ev);
                if (_queue.Count > MaxQueue)
                {
                    int drop = _queue.Count - MaxQueue;
                    _queue.RemoveRange(0, drop);
                    Debug.LogWarning($"[TwiceAnalytics] Queue capped at {MaxQueue}; dropped {drop} oldest event(s).");
                }
                count = _queue.Count;
                triggerFlush = count >= _maxBatchSize;
            }
            PersistQueue();
            Log($"Queued '{clean}' ({count} pending).");

            if (triggerFlush) RequestFlush();
        }

        internal void RequestFlush()
        {
            if (!_consent || _sending) return;
            if (!isActiveAndEnabled) return;
            StartCoroutine(DrainQueue());
        }

        // ---- network --------------------------------------------------------

        IEnumerator FlushLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(_currentDelay);
                if (!_consent) { _currentDelay = _flushIntervalSeconds; continue; }
                bool hasWork;
                lock (_queueLock) hasWork = _queue.Count > 0;
                if (hasWork && !_sending)
                    yield return DrainQueue();
                else if (!hasWork)
                    _currentDelay = _flushIntervalSeconds;
            }
        }

        IEnumerator DrainQueue()
        {
            if (_sending) yield break;
            _sending = true;
            try
            {
                while (true)
                {
                    if (!_consent || string.IsNullOrEmpty(_apiKey)) break;

                    List<QueuedEvent> batch = TakeBatch();
                    if (batch.Count == 0) { _currentDelay = _flushIntervalSeconds; break; }

                    bool success = false;
                    bool fatalDrop = false;
                    yield return SendBatch(batch, ok => success = ok, drop => fatalDrop = drop);

                    if (success || fatalDrop)
                    {
                        RemoveSent(batch.Count, batch[0].sessionId);
                        PersistQueue();
                        _currentDelay = _flushIntervalSeconds; // healthy → reset backoff
                    }
                    else
                    {
                        // Network/server failure: keep events, back off, retry later.
                        _currentDelay = Mathf.Min(Mathf.Max(_currentDelay, _flushIntervalSeconds) * 2f, MaxBackoffSeconds);
                        Log($"Flush failed, backing off {_currentDelay:0}s.");
                        break;
                    }
                }
            }
            finally { _sending = false; }
        }

        /// <summary>Takes up to maxBatchSize leading events that share the same session id (preserves order).</summary>
        List<QueuedEvent> TakeBatch()
        {
            var batch = new List<QueuedEvent>();
            lock (_queueLock)
            {
                if (_queue.Count == 0) return batch;
                string sid = _queue[0].sessionId;
                for (int i = 0; i < _queue.Count && batch.Count < _maxBatchSize; i++)
                {
                    if (_queue[i].sessionId != sid) break;
                    batch.Add(_queue[i]);
                }
            }
            return batch;
        }

        void RemoveSent(int n, string sessionId)
        {
            lock (_queueLock)
            {
                int removed = 0;
                while (removed < n && _queue.Count > 0 && _queue[0].sessionId == sessionId)
                {
                    _queue.RemoveAt(0);
                    removed++;
                }
            }
        }

        IEnumerator SendBatch(List<QueuedEvent> batch, Action<bool> onResult, Action<bool> onFatalDrop)
        {
            string url = _endpointBaseUrl.TrimEnd('/') + "/sdk/events";
            byte[] body = Encoding.UTF8.GetBytes(BuildBatchBody(batch));

            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-App-Key", _apiKey);
                req.timeout = 20;

                yield return req.SendWebRequest();

                long code = req.responseCode;
                bool transportOk = req.result == UnityWebRequest.Result.Success;

                if (transportOk && code >= 200 && code < 300)
                {
                    _lastStatus = $"OK {code} — sent {batch.Count} event(s)";
                    Log($"Sent {batch.Count} event(s) → {code}. {req.downloadHandler.text}");
                    onResult(true);
                }
                else if (code == 401)
                {
                    _lastStatus = "401 invalid_app_key (dropped)";
                    Debug.LogError("[TwiceAnalytics] 401 invalid_app_key — check your X-App-Key. These events will be dropped.");
                    onFatalDrop(true); // never going to succeed; drop so the queue does not wedge
                }
                else if (code == 422 || code == 400)
                {
                    _lastStatus = $"{code} rejected (dropped)";
                    Debug.LogWarning($"[TwiceAnalytics] {code} rejected batch (dropping it): {req.downloadHandler.text}");
                    onFatalDrop(true); // malformed/unprocessable — dropping avoids an infinite retry loop
                }
                else
                {
                    _lastStatus = $"Failed (code={code}, {req.error}) — will retry";
                    Log($"Send failed (code={code}, result={req.result}, err={req.error}). Will retry.");
                    onResult(false);
                }
            }
        }

        string BuildBatchBody(List<QueuedEvent> batch)
        {
            var sb = new StringBuilder(256 + batch.Count * 64);
            sb.Append('{');
            sb.Append("\"session_id\":").Append(JsonConvert.ToString(Clamp(batch[0].sessionId, 80))).Append(',');
            sb.Append("\"user_id\":").Append(JsonConvert.ToString(Clamp(_userId, 80))).Append(',');
            sb.Append("\"platform\":").Append(JsonConvert.ToString(_platform)).Append(',');
            sb.Append("\"app_version\":").Append(JsonConvert.ToString(_appVersion)).Append(',');
            if (!string.IsNullOrEmpty(_buildNumber))
                sb.Append("\"build\":").Append(JsonConvert.ToString(_buildNumber)).Append(',');
            sb.Append("\"env\":").Append(JsonConvert.ToString(IsSandbox ? "sandbox" : "production")).Append(',');
            sb.Append("\"events\":[");
            for (int i = 0; i < batch.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var e = batch[i];
                sb.Append("{\"event_id\":").Append(JsonConvert.ToString(e.eventId));
                sb.Append(",\"name\":").Append(JsonConvert.ToString(e.name));
                sb.Append(",\"ts\":").Append(e.ts.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"params\":").Append(string.IsNullOrEmpty(e.paramsJson) ? "{}" : e.paramsJson);
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ---- persistence (offline) -----------------------------------------
        // Format: one event per line, TAB-separated: eventId \t sessionId \t ts \t name \t base64(paramsJson)

        void PersistQueue()
        {
            try
            {
                QueuedEvent[] snapshot;
                lock (_queueLock) snapshot = _queue.ToArray();

                var sb = new StringBuilder(snapshot.Length * 64);
                foreach (var e in snapshot)
                {
                    sb.Append(e.eventId).Append('\t')
                      .Append(e.sessionId).Append('\t')
                      .Append(e.ts.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(e.name).Append('\t')
                      .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(e.paramsJson ?? "{}")))
                      .Append('\n');
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                PlayerPrefs.SetString(QueuePrefsKey, sb.ToString());
                PlayerPrefs.Save();
#else
                if (snapshot.Length == 0)
                {
                    if (File.Exists(_queueFilePath)) File.Delete(_queueFilePath);
                }
                else File.WriteAllText(_queueFilePath, sb.ToString(), Encoding.UTF8);
#endif
            }
            catch (Exception e) { Log("PersistQueue failed: " + e.Message); }
        }

        void LoadPersistedQueue()
        {
            try
            {
                string raw;
#if UNITY_WEBGL && !UNITY_EDITOR
                raw = PlayerPrefs.GetString(QueuePrefsKey, "");
#else
                raw = File.Exists(_queueFilePath) ? File.ReadAllText(_queueFilePath, Encoding.UTF8) : "";
#endif
                if (string.IsNullOrEmpty(raw)) return;

                var lines = raw.Split('\n');
                lock (_queueLock)
                {
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var parts = line.Split('\t');

                        string eventId, sessionId, name, b64;
                        long ts;
                        if (parts.Length == 5)
                        {
                            eventId = parts[0]; sessionId = parts[1]; name = parts[3]; b64 = parts[4];
                            if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ts)) continue;
                        }
                        else if (parts.Length == 4) // legacy (eventId yoktu) → yenisini üret
                        {
                            eventId = Guid.NewGuid().ToString("N"); sessionId = parts[0]; name = parts[2]; b64 = parts[3];
                            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ts)) continue;
                        }
                        else continue;

                        string json;
                        try { json = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
                        catch { continue; }
                        _queue.Add(new QueuedEvent { eventId = eventId, sessionId = sessionId, ts = ts, name = name, paramsJson = json });
                    }
                }
                Log($"Restored {lines.Length} persisted event line(s).");
            }
            catch (Exception e) { Log("LoadPersistedQueue failed: " + e.Message); }
        }

        // ---- Unity lifecycle -----------------------------------------------

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _backgroundSinceUtc = DateTime.UtcNow;
                PersistQueue();
                RequestFlush();
            }
            else if (_backgroundSinceUtc.HasValue)
            {
                double mins = (DateTime.UtcNow - _backgroundSinceUtc.Value).TotalMinutes;
                _backgroundSinceUtc = null;
                if (mins >= NewSessionAfterMinutes)
                {
                    EndSession();
                    StartNewSession(logStart: _autoTrackSessions);
                    Log($"Resumed after {mins:0} min — started a new session.");
                }
            }
        }

        void OnApplicationQuit()
        {
            EndSession();
            PersistQueue();
            RequestFlush();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- debug ----------------------------------------------------------

        internal TwiceAnalytics.DebugInfo BuildDebugInfo()
        {
            string[] pending;
            int count;
            lock (_queueLock)
            {
                count = _queue.Count;
                int n = Mathf.Min(count, 20);
                pending = new string[n];
                for (int i = 0; i < n; i++)
                    pending[i] = _queue[i].name + " @" + _queue[i].ts.ToString(CultureInfo.InvariantCulture);
            }
            return new TwiceAnalytics.DebugInfo
            {
                initialized = _configured,
                consent = _consent,
                environment = IsSandbox ? "sandbox" : "production",
                pending = count,
                userId = _userId,
                sessionId = _sessionId,
                platform = _platform,
                appVersion = _appVersion,
                buildNumber = _buildNumber,
                endpoint = _endpointBaseUrl,
                apiKeyMasked = MaskKey(_apiKey),
                lastStatus = _lastStatus,
                pendingEvents = pending,
            };
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern int _TwiceIsSandboxReceipt();
#endif

        // iOS App Store receipt'i sandbox mı? (TestFlight + Xcode/StoreKit sandbox). Diğer platform/editör → false.
        static bool IosReceiptIsSandbox()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try { return _TwiceIsSandboxReceipt() != 0; }
            catch { return false; }
#else
            return false;
#endif
        }

        static string MaskKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(empty)";
            if (key.Length <= 10) return key.Substring(0, 3) + "…";
            return key.Substring(0, 6) + "…" + key.Substring(key.Length - 4);
        }

        // ---- utils ----------------------------------------------------------

        static string ResolvePlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                case RuntimePlatform.WebGLPlayer: return "WebGL";
                case RuntimePlatform.WindowsPlayer: return "Windows";
                case RuntimePlatform.OSXPlayer: return "macOS";
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.LinuxEditor: return "Editor";
                default: return "Editor";
            }
        }

        static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                          || c == '_' || c == '.' || c == ':' || c == '-';
                sb.Append(ok ? c : '_');
                if (sb.Length >= 64) break;
            }
            return sb.Length == 0 ? "unnamed" : sb.ToString();
        }

        static string Clamp(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

        static string Trim(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value.Trim();

        void Log(string msg)
        {
            if (_debugLogging) Debug.Log("[TwiceAnalytics] " + msg);
        }
    }
}
