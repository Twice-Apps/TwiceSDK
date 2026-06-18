using System;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using TwiceSDK;

namespace TwiceSDK.VersionCheck
{
    /// <summary>What the backend decided for the running build's version.</summary>
    public enum UpdateAction { None, Optional, Forced }

    /// <summary>Result of a version check. The prompt UI/text — and the store URL it opens — are
    /// built on the client from these ids (the backend only returns the decision + ids).</summary>
    public struct UpdateStatus
    {
        public UpdateAction Action;
        public string AppId;    // iOS App Store numeric id (for itms-apps://…/id{AppId})
        public string BundleId; // platform bundle id (for market://details?id={BundleId})
        public bool UpdateAvailable => Action != UpdateAction.None;
        public bool IsForced => Action == UpdateAction.Forced;
        public bool IsOptional => Action == UpdateAction.Optional;
    }

    /// <summary>
    /// Update gating client. Asks the backend whether the running (platform, version, build) is
    /// behind the configured "latest" (→ optional) or "minimum" (→ forced) and returns the
    /// decision plus the store URL. Also owns build-number resolution (its core concern), which
    /// <see cref="Analytics.TwiceAnalytics"/> reuses for its event envelope.
    /// All calls are non-blocking and never throw into game code.
    /// </summary>
    public static class TwiceVersionChecker
    {
        // ---- Build number (iOS CFBundleVersion / Android versionCode) -------

        static string _buildNumber;

        /// <summary>
        /// The platform build number — iOS <c>CFBundleVersion</c>, Android <c>versionCode</c> —
        /// which the store increments on every (incl. TestFlight/internal) upload even when the
        /// marketing version is unchanged. In the Editor it reflects the active build target's
        /// setting. "" when unavailable. Cached after first resolve.
        /// </summary>
        public static string BuildNumber
        {
            get { return _buildNumber ?? (_buildNumber = ResolveBuildNumber()); }
        }

        // ---- Check API ------------------------------------------------------

        /// <summary>Check using the API key + endpoint from the TwiceSettings asset.</summary>
        public static void Check(Action<UpdateStatus> onResult)
        {
            string apiKey = null, baseUrl = "https://www.twiceapps.co/api/v1";
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s != null)
            {
                apiKey = s.apiKey;
                if (!string.IsNullOrEmpty(s.endpointBaseUrl)) baseUrl = s.endpointBaseUrl;
            }
            Check(apiKey, baseUrl, onResult);
        }

        /// <summary>Check with an explicit API key + endpoint base URL.</summary>
        public static void Check(string apiKey, string baseUrl, Action<UpdateStatus> onResult)
        {
            try
            {
                if (string.IsNullOrEmpty(apiKey))
                {
                    Debug.LogWarning("[TwiceVersionChecker] No API key — skipping version check.");
                    onResult?.Invoke(default);
                    return;
                }
                TwiceVersionCheckerRunner.EnsureExists();
                TwiceVersionCheckerRunner.Instance.Run(
                    apiKey.Trim(),
                    string.IsNullOrEmpty(baseUrl) ? "https://www.twiceapps.co/api/v1" : baseUrl.Trim().TrimEnd('/'),
                    StorePlatform(), Application.version, BuildNumber, onResult);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceVersionChecker] " + e);
                onResult?.Invoke(default);
            }
        }

        // ---- internals ------------------------------------------------------

        static string StorePlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                default: return Application.platform.ToString(); // backend ignores non-store platforms
            }
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern IntPtr _TwiceBuildNumber();
#endif

        static string ResolveBuildNumber()
        {
#if UNITY_EDITOR
            try
            {
                if (UnityEditor.EditorUserBuildSettings.activeBuildTarget == UnityEditor.BuildTarget.Android)
                    return UnityEditor.PlayerSettings.Android.bundleVersionCode.ToString(CultureInfo.InvariantCulture);
                return UnityEditor.PlayerSettings.iOS.buildNumber ?? "";
            }
            catch { return ""; }
#elif UNITY_ANDROID
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    string pkg = activity.Call<string>("getPackageName");
                    using (var info = pm.Call<AndroidJavaObject>("getPackageInfo", pkg, 0))
                        return info.Get<int>("versionCode").ToString(CultureInfo.InvariantCulture);
                }
            }
            catch { return ""; }
#elif UNITY_IOS
            try
            {
                IntPtr p = _TwiceBuildNumber();
                return p != IntPtr.Zero ? (Marshal.PtrToStringAnsi(p) ?? "") : "";
            }
            catch { return ""; }
#else
            return "";
#endif
        }
    }

    /// <summary>Internal coroutine host for the version-check web request.</summary>
    internal class TwiceVersionCheckerRunner : MonoBehaviour
    {
        internal static TwiceVersionCheckerRunner Instance { get; private set; }

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("[TwiceVersionChecker]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            Instance = go.AddComponent<TwiceVersionCheckerRunner>();
        }

        internal void Run(string apiKey, string baseUrl, string platform, string version, string build, Action<UpdateStatus> cb)
        {
            StartCoroutine(Co(apiKey, baseUrl, platform, version, build, cb));
        }

        IEnumerator Co(string apiKey, string baseUrl, string platform, string version, string build, Action<UpdateStatus> cb)
        {
            string url = baseUrl + "/sdk/version-check?platform=" + UnityWebRequest.EscapeURL(platform)
                       + "&version=" + UnityWebRequest.EscapeURL(version)
                       + "&build=" + UnityWebRequest.EscapeURL(build);

            var status = new UpdateStatus { Action = UpdateAction.None, AppId = "", BundleId = "" };
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("X-App-Key", apiKey);
                req.timeout = 15;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                {
                    try
                    {
                        var j = JObject.Parse(req.downloadHandler.text);
                        string action = (string)j["action"] ?? "none";
                        status.Action = action == "forced" ? UpdateAction.Forced
                                      : action == "optional" ? UpdateAction.Optional
                                      : UpdateAction.None;
                        status.AppId    = (string)j["app_id"] ?? "";
                        status.BundleId = (string)j["bundle_id"] ?? "";
                    }
                    catch (Exception e) { Debug.LogWarning("[TwiceVersionChecker] parse failed: " + e); }
                }
                else
                {
                    Debug.Log("[TwiceVersionChecker] check failed (code=" + req.responseCode + ", err=" + req.error + ").");
                }
            }

            try { cb?.Invoke(status); }
            catch (Exception e) { Debug.LogWarning("[TwiceVersionChecker] callback threw: " + e); }
        }
    }
}
