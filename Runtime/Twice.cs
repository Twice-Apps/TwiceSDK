using UnityEngine;

namespace TwiceSDK
{
    /// <summary>
    /// Top-level entry point / facade for the Twice SDK. Optional today — analytics
    /// auto-bootstraps from the <see cref="TwiceSettings"/> asset and the version prompt
    /// self-runs — but this is the single place to wire modules up explicitly as the SDK grows
    /// (kill switch, etc.). Safe to call from anywhere; idempotent.
    /// </summary>
    public static class Twice
    {
        public static bool IsInitialized { get; private set; }

        /// <summary>Explicitly initialize the SDK. Calling more than once is a no-op.</summary>
        public static void Initialize()
        {
            if (IsInitialized) return;
            IsInitialized = true;
            Debug.Log("[Twice] SDK initialized.");
            // Future: central module wiring (analytics, remote config, version check, kill switch).
        }
    }
}
