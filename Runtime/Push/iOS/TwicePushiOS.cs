#if UNITY_IOS
using System.Collections;
using UnityEngine;
using Unity.Notifications.iOS;
using TwiceSDK;

namespace TwiceSDK.Push
{
    /// <summary>
    /// iOS player-push integration: requests notification authorization, registers with
    /// APNs, and hands the device token to <see cref="TwicePushCore"/>. No Firebase — uses
    /// Unity's Mobile Notifications package. Auto-runs at boot when push is enabled.
    /// Requires "Enable Push Notifications" + "Remote Notification" in
    /// Project Settings → Mobile Notifications → iOS.
    /// </summary>
    internal class TwicePushiOS : MonoBehaviour
    {
        static TwicePushiOS _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBootstrap()
        {
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s == null || !s.enablePushNotifications) { return; }
            if (Application.platform != RuntimePlatform.IPhonePlayer) { return; }
            if (_instance != null) { return; }
            var go = new GameObject("[TwicePush.iOS]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            _instance = go.AddComponent<TwicePushiOS>();
            _instance.StartCoroutine(_instance.CoRegister(s));
        }

        IEnumerator CoRegister(TwiceSettings s)
        {
            // In RequireBootstrap mode, wait until Twice.Initialize() has run.
            bool requireBootstrap = (s.initialization == InitializationMode.RequireBootstrap);
            float t = 0f;
            while (requireBootstrap && !Twice.IsInitialized && t < 30f) { t += Time.unscaledDeltaTime; yield return null; }

            using (var req = new AuthorizationRequest(
                AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound, true))
            {
                while (!req.IsFinished) { yield return null; }

                // The APNs device token often arrives a moment AFTER authorization finishes —
                // poll DeviceToken for a few seconds before giving up.
                string token = req.DeviceToken;
                float w = 0f;
                while (string.IsNullOrEmpty(token) && w < 10f)
                {
                    w += Time.unscaledDeltaTime;
                    yield return null;
                    token = req.DeviceToken;
                }

                if (!string.IsNullOrEmpty(token))
                {
                    Debug.Log("[TwicePush.iOS] APNs token acquired (" + token.Length + " chars) — registering.");
                    TwicePushCore.Register(token, "ios");
                }
                else
                {
                    Debug.LogWarning("[TwicePush.iOS] No APNs device token after wait. granted=" + req.Granted + " error=" + req.Error);
                }
            }
        }
    }
}
#endif
