using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TwiceSDK.Players;

namespace TwiceSDK.Push
{
    /// <summary>
    /// Player push facade. The device-token CAPTURE lives in the platform assemblies
    /// (TwiceSDK.Push.iOS = native APNs via Mobile Notifications, TwiceSDK.Push.Android =
    /// Firebase FCM); each hands its token to <see cref="TwicePushCore.Register"/>, which
    /// registers it with the Twice backend. This core lives in the always-present TwiceSDK
    /// assembly so both platform assemblies can call it without a circular reference.
    /// </summary>
    public static class TwicePush
    {
        /// <summary>Unregister this device (call on logout) so it stops receiving pushes.</summary>
        public static void Unregister()
        {
            TwicePushRunner.EnsureExists();
            TwicePushRunner.Instance.Unregister();
        }
    }

    /// <summary>Backend registration for a captured device token. Called by the platform integrations.</summary>
    public static class TwicePushCore
    {
        /// <summary>Register a captured token. platform = "ios" (APNs) or "android" (FCM).</summary>
        public static void Register(string token, string platform)
        {
            if (string.IsNullOrEmpty(token)) { return; }
            TwicePushRunner.EnsureExists();
            TwicePushRunner.Instance.Send(token, platform);
        }
    }

    /// <summary>Hidden coroutine host that POST/DELETEs the token to /sdk/push-token.</summary>
    internal class TwicePushRunner : MonoBehaviour
    {
        internal static TwicePushRunner Instance { get; private set; }
        string _currentToken;
        string _lastSent;

        internal static void EnsureExists()
        {
            if (Instance != null) { return; }
            var go = new GameObject("[TwicePush]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            Instance = go.AddComponent<TwicePushRunner>();
        }

        internal void Send(string token, string platform)
        {
            if (token == _lastSent) { return; }
            _currentToken = token;
            StartCoroutine(CoSend(token, platform));
        }

        IEnumerator CoSend(string token, string platform)
        {
            // Wait briefly for the stable user id so segment targeting works.
            float t = 0f;
            while (string.IsNullOrEmpty(TwicePlayers.UserId) && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }

            string apiKey, baseUrl;
            ResolveConfig(out apiKey, out baseUrl);
            if (string.IsNullOrEmpty(apiKey)) { Debug.LogWarning("[TwicePush] No API key in TwiceSettings — cannot register token."); yield break; }

            // APNs environment: development (Xcode) builds use sandbox; TestFlight/App Store = production.
            string env = (platform == "ios" && Debug.isDebugBuild) ? "sandbox" : "production";

            string json = "{\"token\":\"" + Esc(token) + "\",\"platform\":\"" + Esc(platform)
                        + "\",\"user_id\":\"" + Esc(TwicePlayers.UserId) + "\",\"env\":\"" + env + "\"}";

            using (var req = new UnityWebRequest(baseUrl + "/sdk/push-token", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-App-Key", apiKey);
                req.timeout = 15;
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
                if (ok) { _lastSent = token; Debug.Log("[TwicePush] token registered with backend (" + platform + ")."); }
                else { Debug.LogWarning("[TwicePush] register failed (code=" + req.responseCode + ", err=" + req.error + ", resp=" + (req.downloadHandler != null ? req.downloadHandler.text : "") + ")."); }
            }
        }

        internal void Unregister()
        {
            if (string.IsNullOrEmpty(_currentToken)) { return; }
            StartCoroutine(CoDelete(_currentToken));
        }

        IEnumerator CoDelete(string token)
        {
            string apiKey, baseUrl;
            ResolveConfig(out apiKey, out baseUrl);
            if (string.IsNullOrEmpty(apiKey)) { yield break; }
            string url = baseUrl + "/sdk/push-token?token=" + UnityWebRequest.EscapeURL(token);
            using (var req = new UnityWebRequest(url, "DELETE"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"token\":\"" + Esc(token) + "\"}"));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-App-Key", apiKey);
                req.timeout = 15;
                yield return req.SendWebRequest();
            }
            _lastSent = null;
        }

        static string Esc(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static void ResolveConfig(out string apiKey, out string baseUrl)
        {
            apiKey = null; baseUrl = "https://www.twiceapps.co/api/v1";
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s != null)
            {
                apiKey = string.IsNullOrEmpty(s.apiKey) ? null : s.apiKey.Trim();
                if (!string.IsNullOrEmpty(s.endpointBaseUrl)) { baseUrl = s.endpointBaseUrl.Trim(); }
            }
            baseUrl = baseUrl.TrimEnd('/');
        }
    }
}
