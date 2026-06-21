using System.Collections;
using UnityEngine;
using Firebase;
using Firebase.Messaging;
using TwiceSDK;

namespace TwiceSDK.Push
{
    /// <summary>
    /// Android player-push integration: Firebase Cloud Messaging. Gets the FCM registration
    /// token and hands it to <see cref="TwicePushCore"/>. Compiled only when the TWICE_FCM
    /// scripting define is set (auto-managed by Editor/TwicePushDefineSync.cs when Firebase
    /// Messaging is imported and push is enabled). Auto-runs at boot on Android.
    /// </summary>
    internal class TwicePushAndroid : MonoBehaviour
    {
        static TwicePushAndroid _instance;
        volatile string _pendingToken; // set on a Firebase background thread, consumed in Update

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBootstrap()
        {
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s == null || !s.enablePushNotifications) { return; }
            if (Application.platform != RuntimePlatform.Android) { return; }
            if (_instance != null) { return; }
            var go = new GameObject("[TwicePush.Android]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            _instance = go.AddComponent<TwicePushAndroid>();
            _instance.StartCoroutine(_instance.CoBegin(s));
        }

        IEnumerator CoBegin(TwiceSettings s)
        {
            bool requireBootstrap = (s.initialization == InitializationMode.RequireBootstrap);
            float t = 0f;
            while (requireBootstrap && !Twice.IsInitialized && t < 30f) { t += Time.unscaledDeltaTime; yield return null; }

            bool log = s.debugLogging;
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
            {
                if (task.IsFaulted || task.IsCanceled || task.Result != DependencyStatus.Available)
                {
                    if (log) { Debug.LogWarning("[TwicePush.Android] Firebase unavailable."); }
                    return;
                }
                FirebaseMessaging.TokenReceived += OnTokenReceived;
                FirebaseMessaging.GetTokenAsync().ContinueWith(tok =>
                {
                    if (!tok.IsFaulted && !tok.IsCanceled && !string.IsNullOrEmpty(tok.Result)) { _pendingToken = tok.Result; }
                });
            });
        }

        void OnTokenReceived(object sender, TokenReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Token)) { _pendingToken = e.Token; }
        }

        void Update()
        {
            var tok = _pendingToken;
            if (tok == null) { return; }
            _pendingToken = null;
            TwicePushCore.Register(tok, "android");
        }
    }
}
