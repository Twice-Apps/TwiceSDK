using UnityEngine;

namespace Twice.Analytics
{
    /// <summary>
    /// Project-level configuration for the Twice SDK (analytics + remote config).
    /// Create one via <c>Assets → Create → Twice → Analytics Settings</c> and place it in a
    /// <c>Resources</c> folder named exactly "TwiceAnalyticsSettings" so it is auto-loaded at boot.
    /// </summary>
    /// <summary>How the SDK decides whether events are tagged sandbox or production.</summary>
    public enum EnvironmentMode
    {
        /// <summary>Editor &amp; Development builds (or the TWICE_SANDBOX define) → sandbox; otherwise production.</summary>
        Auto = 0,
        /// <summary>Always tag as production.</summary>
        Production = 1,
        /// <summary>Always tag as sandbox (e.g. a dedicated TestFlight/internal build).</summary>
        Sandbox = 2,
    }

    [CreateAssetMenu(fileName = "TwiceAnalyticsSettings", menuName = "Twice/Analytics Settings", order = 0)]
    public class TwiceAnalyticsSettings : ScriptableObject
    {
        /// <summary>Resources path (without extension) the bootstrap looks for.</summary>
        public const string ResourceName = "TwiceAnalyticsSettings";

        [Header("Project")]
        [Tooltip("X-App-Key for this game. Copy it from Twice admin → Oyunlar → your game → 'API anahtarı (X-App-Key)'. One game = one key.")]
        public string apiKey = "";

        [Tooltip("Base URL of the Twice API. Default points at production.")]
        public string endpointBaseUrl = "https://www.twiceapps.co/api/v1";

        [Header("Analytics")]
        [Tooltip("Automatically track session_start / session_end events.")]
        public bool autoTrackSessions = true;

        [Tooltip("How often (seconds) the queued events are flushed to the backend.")]
        public int flushIntervalSeconds = 15;

        [Tooltip("Maximum number of events sent in a single batch request.")]
        public int maxBatchSize = 20;

        [Header("Remote Config")]
        [Tooltip("Fetch remote config automatically at boot. You can also call TwiceRemoteConfig.Fetch() manually any time.")]
        public bool autoFetchRemoteConfig = true;

        [Header("Environment")]
        [Tooltip("Auto: Editor & Development builds (or TWICE_SANDBOX define) → sandbox, else production. " +
                 "Sandbox/Production force the tag (e.g. set Sandbox in a TestFlight/internal build).")]
        public EnvironmentMode environment = EnvironmentMode.Auto;

        [Header("Debug")]
        [Tooltip("Log SDK activity to the Unity console. Turn off for production builds.")]
        public bool debugLogging = false;
    }
}
