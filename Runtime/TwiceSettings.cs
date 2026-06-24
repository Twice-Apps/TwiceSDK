using System.Collections.Generic;
using UnityEngine;

namespace TwiceSDK
{
    /// <summary>Editor-only: report this store platform instead of "Editor", so version
    /// discovery + checking work while testing in Play mode without a device build.</summary>
    public enum EditorPlatform
    {
        None = 0,
        iOS = 1,
        Android = 2,
    }

    /// <summary>Controls whether the SDK starts itself at boot or waits for a TwiceBootstrap object.</summary>
    public enum InitializationMode
    {
        /// <summary>Default / backward-compatible: the SDK auto-initializes at boot from the
        /// settings asset (no bootstrap object required).</summary>
        Auto = 0,
        /// <summary>The SDK does NOT auto-start. It initializes only when a TwiceBootstrap object
        /// calls <c>Twice.Initialize()</c> — lets you gate init on the prefab's presence.</summary>
        RequireBootstrap = 1,
    }

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

    /// <summary>How the Rate Us module asks for a review.</summary>
    public enum RateUsMode
    {
        /// <summary>Show the native in-app review immediately (iOS SKStoreReviewController / Android Play In-App Review).</summary>
        Direct = 0,
        /// <summary>Ask "do you like the game?" first; only on "yes" go to the native review (no = just close).</summary>
        AskFirst = 1,
    }

    /// <summary>
    /// Project-level configuration for the Twice SDK (analytics + remote config).
    /// Create one via <c>Assets → Create → Twice → SDK Settings</c> and place it in a
    /// <c>Resources</c> folder named exactly "TwiceSettings" so it is auto-loaded at boot.
    /// </summary>
    [CreateAssetMenu(fileName = "TwiceSettings", menuName = "Twice/SDK Settings", order = 0)]
    public class TwiceSettings : ScriptableObject
    {
        /// <summary>Resources path (without extension) the bootstrap looks for.</summary>
        public const string ResourceName = "TwiceSettings";

        [Header("Project")]
        [Tooltip("X-App-Key for this game. Copy it from Twice admin → Oyunlar → your game → 'API anahtarı (X-App-Key)'. One game = one key.")]
        public string apiKey = "";

        [Tooltip("Base URL of the Twice API. Default points at production.")]
        public string endpointBaseUrl = "https://www.twiceapps.co/api/v1";

        [Header("Initialization")]
        [Tooltip("Auto (default): SDK auto-initializes at boot from this asset (current behavior). " +
                 "RequireBootstrap: the SDK does NOT auto-start; it initializes only when a TwiceBootstrap " +
                 "object (the TwiceSDK prefab) calls Twice.Initialize(). Lets you gate init on the prefab's presence.")]
        public InitializationMode initialization = InitializationMode.Auto;

        [Header("Modules")]
        [Tooltip("Analytics: sessions, events, IAP/ad revenue. When off, the analytics runner never starts.")]
        public bool enableAnalytics = true;

        [Tooltip("Remote Config: fetch typed key/values from the backend. When off, no config is loaded/fetched.")]
        public bool enableRemoteConfig = true;

        [Tooltip("Version Checker: ask the backend whether this build needs an update. When off, no check runs.")]
        public bool enableVersionCheck = true;

        [Tooltip("Leaderboards: Submit / GetTop / GetMyRank calls. When off, those calls are no-ops (no network).")]
        public bool enableLeaderboards = true;

        [Tooltip("Push Notifications: capture the device's FCM registration token and register it with the " +
                 "backend so the panel can send player campaigns. Requires Firebase Messaging imported AND the " +
                 "TWICE_FCM scripting define set (otherwise the push code is excluded and nothing happens). When off, no token is captured.")]
        public bool enablePushNotifications = true;

        [Tooltip("Rate Us: native in-app review (iOS SKStoreReviewController / Android Play In-App Review), " +
                 "optionally behind a 'do you like the game?' gate. Call TwiceRateUs.Show() from your game. When off, that call is a no-op.")]
        public bool enableRateUs = true;

        [Header("Rate Us")]
        [Tooltip("Direct: show the native review immediately. AskFirst: ask 'do you like the game?' first — only 'yes' goes to the native review; 'no' just closes. AskFirst penceresinin metinleri prefab'te (Resources/TwiceRateUsPanel).")]
        public RateUsMode rateUsMode = RateUsMode.AskFirst;

        [Tooltip("Rate Us'u göstermek istediğin level numaraları. SDK bunu otomatik kullanmaz; oyun kodundan " +
                 "okuyup uygun level'da TwiceRateUs.Show() çağırırsın (örn. if (settings.rateUsLevels.Contains(level)) TwiceRateUs.Show()).")]
        public List<int> rateUsLevels = new List<int>();

        [Header("Identity")]
        [Tooltip("ON (default): the player id is the stable per-device identifier (iOS = vendor IDFV, " +
                 "not the advertising IDFA) — a reinstall keeps the same player, and the same user is " +
                 "recognised across all of this studio's games on that device. OFF: a random GUID is " +
                 "generated and stored locally (resets on uninstall). Note: a device identifier is " +
                 "personal data under KVKK/GDPR — disclose it in your privacy text.")]
        public bool useDeviceIdentifier = true;

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

        [Tooltip("EDITOR ONLY. While testing in the Editor, report this store platform instead of " +
                 "'Editor' so the build's version reaches the Version Checker and the update prompt " +
                 "can be tested without a device. None = normal Editor behavior. Ignored in real builds.")]
        public EditorPlatform editorPlatformOverride = EditorPlatform.None;

        [Header("Debug")]
        [Tooltip("Log SDK activity to the Unity console. Turn off for production builds.")]
        public bool debugLogging = false;
    }
}
