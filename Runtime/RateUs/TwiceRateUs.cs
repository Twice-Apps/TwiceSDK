using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TwiceSDK.RateUs
{
    /// <summary>
    /// Native "Rate Us". Call <see cref="Show"/> from your game (e.g. after a win).
    /// Two modes (TwiceSettings → Rate Us):
    ///   • Direct   → show the native in-app review right away.
    ///   • AskFirst → show the "do you like the game?" gate first (a prefab under the TwiceSDK
    ///                object); only "yes" goes to the native review, "no" just closes (so unhappy
    ///                users never see the store).
    /// Native review: iOS = SKStoreReviewController, Android = Play In-App Review
    /// (falls back to the Play Store page if the Play Review library isn't present).
    /// No backend/panel involvement.
    /// </summary>
    public static class TwiceRateUs
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _twiceRequestReview();
#endif

        /// <summary>Run the rate-us flow per the settings asset (Direct or AskFirst gate).</summary>
        public static void Show()
        {
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s == null || !s.enableRateUs) { return; }

            if (s.rateUsMode == RateUsMode.AskFirst)
            {
                TwiceRateUsPanel.Present(); // instantiates the gate prefab; falls back to native review if missing
            }
            else
            {
                RequestNativeReview();
            }
        }

        /// <summary>True if <paramref name="level"/> is one of the levels configured in
        /// TwiceSettings → Rate Us → Levels (and the module is enabled).</summary>
        public static bool IsRateLevel(int level)
        {
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            return s != null && s.enableRateUs && s.rateUsLevels != null && s.rateUsLevels.Contains(level);
        }

        /// <summary>Run the rate-us flow only if <paramref name="level"/> is in the configured Levels list.
        /// Call this right after a level completes — no need to load settings yourself.</summary>
        public static void ShowAtLevel(int level)
        {
            if (IsRateLevel(level)) { Show(); }
        }

        /// <summary>Open the native in-app review directly (skips the gate).</summary>
        public static void RequestNativeReview()
        {
#if UNITY_EDITOR
            Debug.Log("[TwiceRateUs] (Editor) native review would appear on a device here.");
#elif UNITY_IOS
            try { _twiceRequestReview(); }
            catch (System.Exception e) { Debug.LogWarning("[TwiceRateUs] iOS review failed: " + e); }
#elif UNITY_ANDROID
            TwiceRateUsAndroid.Request();
#else
            Debug.Log("[TwiceRateUs] native review is not supported on this platform.");
#endif
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>Android native review via Play In-App Review (runtime JNI; no compile-time dependency).
    /// The <c>com.google.android.play:review</c> library (2.x) uses the <c>com.google.android.gms.tasks</c>
    /// task API. Falls back to opening the Play Store page if the library is missing or the flow fails.</summary>
    internal static class TwiceRateUsAndroid
    {
        internal static void Request()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    var factory = new AndroidJavaClass("com.google.android.play.core.review.ReviewManagerFactory");
                    var manager = factory.CallStatic<AndroidJavaObject>("create", activity);
                    var requestTask = manager.Call<AndroidJavaObject>("requestReviewFlow");
                    requestTask.Call<AndroidJavaObject>("addOnCompleteListener", new RequestListener(manager, activity));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TwiceRateUs] Play In-App Review unavailable, opening store page. " + e.Message);
                OpenStorePage();
            }
        }

        internal static void OpenStorePage()
        {
            string pkg = Application.identifier;
            try { Application.OpenURL("market://details?id=" + pkg); }
            catch { Application.OpenURL("https://play.google.com/store/apps/details?id=" + pkg); }
        }

        class RequestListener : AndroidJavaProxy
        {
            readonly AndroidJavaObject _manager, _activity;
            public RequestListener(AndroidJavaObject manager, AndroidJavaObject activity)
                : base("com.google.android.gms.tasks.OnCompleteListener") { _manager = manager; _activity = activity; }

            // Called by Play Review when requestReviewFlow finishes.
            public void onComplete(AndroidJavaObject task)
            {
                try
                {
                    if (!task.Call<bool>("isSuccessful")) { OpenStorePage(); return; }
                    var reviewInfo = task.Call<AndroidJavaObject>("getResult");
                    _manager.Call<AndroidJavaObject>("launchReviewFlow", _activity, reviewInfo);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[TwiceRateUs] launchReviewFlow failed, opening store page. " + e.Message);
                    OpenStorePage();
                }
            }
        }
    }
#endif
}
