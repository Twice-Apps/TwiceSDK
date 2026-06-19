using System;
using System.Collections;
using UnityEngine;
using TwiceSDK.Analytics;
using TwiceSDK.RemoteConfig;
using TwiceSDK.VersionCheck;

namespace TwiceSDK
{
    /// <summary>
    /// Top-level SDK entry point. <see cref="Initialize"/> runs the modules in order, one after the
    /// previous finishes pulling/pushing its data:
    ///   1) Version check (pull)  → raises <see cref="OnVersionChecked"/> so the app can gate,
    ///   2) Analytics (flush the queued session_start),
    ///   3) Remote config (pull).
    /// Called by the TwiceSDK bootstrap object; idempotent. Analytics + remote config still
    /// configure themselves from the settings asset, but their boot-time network calls are deferred
    /// to this ordered sequence.
    /// </summary>
    public static class Twice
    {
        public static bool IsInitialized { get; private set; }

        /// <summary>Fired once the version check completes (step 1), before analytics/remote config.</summary>
        public static event Action<UpdateStatus> OnVersionChecked;

        /// <summary>Fired as each init step completes, with a message ("Version checked",
        /// "Analytics initialized", "Remote config initialized"), then "Ready" when the whole
        /// sequence finishes. Handy for a loading-screen status list.</summary>
        public static event Action<string> OnProgress;

        public static void Initialize()
        {
            if (IsInitialized) return;
            IsInitialized = true;
            Debug.Log("[Twice] Initializing…");
            TwiceSequenceRunner.Begin();
        }

        internal static void RaiseVersionChecked(UpdateStatus s)
        {
            try { OnVersionChecked?.Invoke(s); }
            catch (Exception e) { Debug.LogWarning("[Twice] OnVersionChecked handler threw: " + e); }
        }

        internal static void RaiseProgress(string step)
        {
            try { OnProgress?.Invoke(step); }
            catch (Exception e) { Debug.LogWarning("[Twice] OnProgress handler threw: " + e); }
        }
    }

    /// <summary>Hidden host that runs the ordered initialization sequence.</summary>
    internal class TwiceSequenceRunner : MonoBehaviour
    {
        static TwiceSequenceRunner _instance;

        internal static void Begin()
        {
            if (_instance != null) return;
            var go = new GameObject("[Twice]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            _instance = go.AddComponent<TwiceSequenceRunner>();
            _instance.StartCoroutine(_instance.Sequence());
        }

        IEnumerator Sequence()
        {
            // This sequence is the SINGLE init authority in RequireBootstrap mode (where the
            // boot-time AutoBootstraps are gated off), so it CONFIGURES each module here — not
            // just flush/fetch. In Auto mode the modules are already configured; re-configuring
            // is idempotent (Configure only calls Begin() once).
            var settings = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            bool analyticsOn = settings == null || settings.enableAnalytics;
            bool remoteOn     = settings == null || settings.enableRemoteConfig;
            bool versionOn    = settings == null || settings.enableVersionCheck;

            // 1) Version check (pull) — first, so the game can gate before loading anything else.
            if (versionOn)
            {
                Debug.Log("[Twice] 1/3 Version check…");
                bool vcDone = false;
                TwiceVersionChecker.Check(status => { Twice.RaiseVersionChecked(status); vcDone = true; });
                yield return WaitUntil(() => vcDone, 25f);
            }
            else { Debug.Log("[Twice] 1/3 Version check skipped (module disabled)."); }
            Twice.RaiseProgress("Version checked");

            // 2) Analytics — configure from the settings asset, then flush the queued session_start.
            if (analyticsOn)
            {
                Debug.Log("[Twice] 2/3 Analytics…");
                if (settings != null)
                {
                    TwiceAnalyticsRunner.EnsureExists();
                    TwiceAnalyticsRunner.Instance.ConfigureFromSettings(settings);
                }
                TwiceAnalytics.Flush();
                yield return WaitUntil(() => TwiceAnalytics.GetDebugInfo().pending == 0, 10f);
            }
            else { Debug.Log("[Twice] 2/3 Analytics skipped (module disabled)."); }
            Twice.RaiseProgress("Analytics initialized");

            // 3) Remote config (pull) — configure, then fetch (honouring autoFetchRemoteConfig).
            if (remoteOn && (settings == null || settings.autoFetchRemoteConfig))
            {
                Debug.Log("[Twice] 3/3 Remote config…");
                if (settings != null)
                {
                    TwiceRemoteConfigRunner.EnsureExists();
                    TwiceRemoteConfigRunner.Instance.ConfigureFromSettings(settings);
                }
                bool rcDone = false;
                TwiceRemoteConfig.Fetch(ok => rcDone = true);
                yield return WaitUntil(() => rcDone, 25f);
            }
            else { Debug.Log(remoteOn ? "[Twice] 3/3 Remote config skipped (autoFetchRemoteConfig off)." : "[Twice] 3/3 Remote config skipped (module disabled)."); }
            Twice.RaiseProgress("Remote config initialized");

            Twice.RaiseProgress("Ready");
            Debug.Log("[Twice] Initialization sequence complete.");
        }

        // Yields until predicate is true or the timeout elapses (real time, pause-proof).
        static IEnumerator WaitUntil(Func<bool> done, float timeoutSeconds)
        {
            float t = 0f;
            while (!done() && t < timeoutSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
