using UnityEngine;
using TwiceSDK.Analytics;

namespace TwiceSDK.Players
{
    /// <summary>
    /// Player identity &amp; profile. <see cref="UserId"/> is the stable per-device id the SDK uses to
    /// attribute everything (analytics, leaderboards, purchases). <see cref="DisplayName"/> is a
    /// friendly label shown on leaderboards and in the admin panel.
    ///
    /// This is the public home for player concerns — <see cref="TwiceSDK.Analytics.TwiceAnalytics"/>
    /// stays analytics-only. Under the hood the identity stamps every event and the display name
    /// rides the analytics event envelope (no extra endpoint), so this facade delegates to the
    /// analytics engine.
    /// </summary>
    public static class TwicePlayers
    {
        /// <summary>The persistent anonymous user id (same id sent with every event). "" before init.</summary>
        public static string UserId
        {
            get { var r = TwiceAnalyticsRunner.Instance; return r != null ? r.UserId : ""; }
        }

        /// <summary>The current display name, or "" if none set.</summary>
        public static string DisplayName
        {
            get { var r = TwiceAnalyticsRunner.Instance; return r != null ? r.DisplayName : ""; }
        }

        /// <summary>
        /// Set this player's display name (leaderboards, admin panel). Persisted locally and sent
        /// with the next event batch. Pass null/empty to clear it.
        /// </summary>
        public static void SetDisplayName(string name)
        {
            var r = TwiceAnalyticsRunner.Instance;
            if (r != null) r.SetDisplayName(name);
            else Debug.LogWarning("[TwicePlayers] SetDisplayName called before the SDK initialized — ignored. Call after Twice.Initialize().");
        }
    }
}
