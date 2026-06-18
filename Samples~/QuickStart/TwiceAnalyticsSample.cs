using System.Collections.Generic;
using UnityEngine;
using TwiceSDK.Analytics;
using TwiceSDK.RemoteConfig;

namespace TwiceSDK.Sample
{
    /// <summary>
    /// Minimal usage example for analytics + remote config. Drop this on a GameObject,
    /// press Play, and watch the Twice admin dashboard fill up. If you ship a
    /// TwiceSettings asset in Resources, the SDK auto-inits and the explicit
    /// Init() below is optional.
    /// </summary>
    public class TwiceAnalyticsSample : MonoBehaviour
    {
        [Tooltip("Leave empty to use the API key from the TwiceSettings asset.")]
        public string apiKeyOverride = "";

        void Start()
        {
            // Optional manual init (skip if you use the settings asset in Resources).
            if (!string.IsNullOrEmpty(apiKeyOverride))
                TwiceAnalytics.Init(apiKeyOverride, new TwiceAnalytics.Options { debugLogging = true });

            // ---- Analytics ----
            TwiceAnalytics.SetConsent(true);                 // GDPR/KVKK; defaults to granted
            TwiceAnalytics.SetUserProperty("ab_group", "B"); // attached to every subsequent event
            TwiceAnalytics.LevelCompleted("1-3", score: 1200, duration: 42.5f);
            TwiceAnalytics.LogEvent("boss_defeated", new Dictionary<string, object>
            {
                { "boss", "golem" },
                { "tries", 3 },
            });
            TwiceAnalytics.Flush(); // force an immediate send (otherwise flushes on interval/batch)

            // ---- Remote Config ----
            // Auto-fetched at boot (toggle on the settings asset). Re-apply on update:
            TwiceRemoteConfig.OnUpdated += ApplyConfig;
            ApplyConfig(); // apply whatever is cached right now

            // Manual refresh example:
            TwiceRemoteConfig.Fetch(ok =>
                Debug.Log($"[Sample] remote config fetch ok={ok}, v{TwiceRemoteConfig.Version}"));
        }

        void ApplyConfig()
        {
            bool adsOn = TwiceRemoteConfig.GetBool("ads_enabled", true);
            int coins = TwiceRemoteConfig.GetInt("coins_per_level", 50);
            Debug.Log($"[Sample] config v{TwiceRemoteConfig.Version}: ads_enabled={adsOn}, coins_per_level={coins}");
        }

        void OnDestroy()
        {
            TwiceRemoteConfig.OnUpdated -= ApplyConfig;
        }
    }
}
