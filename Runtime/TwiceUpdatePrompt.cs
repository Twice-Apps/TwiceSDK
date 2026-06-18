using UnityEngine;
using UnityEngine.UI;
using TwiceSDK;

namespace TwiceSDK.VersionCheck
{
    /// <summary>
    /// Bootstrap + controller for the VersionChecker prefab (a full-screen, input-blocking Canvas).
    /// Drop the prefab into your first scene. On play it initializes the SDK, lets analytics fire
    /// <c>session_start</c> (which reports this build's version → admin panel), runs the version
    /// check, and:
    ///   • update needed  → reveals the prompt and wires the Update button to the store,
    ///   • up to date     → destroys itself.
    /// The store deep link is built here from the ids the backend returns (iOS App ID → itms-apps,
    /// Android bundle id → market). Survives scene loads (singleton + DontDestroyOnLoad).
    ///
    /// UI children are found by name (Blocker / a Button), so the prefab needs no manual wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public class TwiceUpdatePrompt : MonoBehaviour
    {
        static TwiceUpdatePrompt _instance;

        [Tooltip("Also show the prompt for optional (non-forced) updates. If false, optional updates only log and the object is destroyed.")]
        public bool showForOptional = true;

        GameObject _content; // the "Blocker" tint subtree
        Button _button;
        string _appId;
        string _bundleId;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            var blocker = transform.Find("Blocker");
            _content = blocker != null ? blocker.gameObject : null;
            _button = GetComponentInChildren<Button>(true);
            if (_content != null) _content.SetActive(false); // stay hidden until the check resolves
        }

        void Start()
        {
            if (_instance != this) return; // duplicate already destroyed in Awake
            Twice.Initialize();
            Debug.Log("[TwiceUpdatePrompt] Checking app version…");
            TwiceVersionChecker.Check(OnResult);
        }

        void OnResult(UpdateStatus status)
        {
            _appId = status.AppId;
            _bundleId = status.BundleId;

            bool show = status.IsForced || (status.IsOptional && showForOptional);
            if (!show)
            {
                Debug.Log("[TwiceUpdatePrompt] No update needed (action=" + status.Action + ") — destroying prompt.");
                Destroy(gameObject);
                return;
            }

            Debug.Log("[TwiceUpdatePrompt] Update " + status.Action + " — showing prompt. appId='" + _appId + "' bundleId='" + _bundleId + "'");
            if (_button != null)
            {
                _button.onClick.RemoveListener(OpenStore);
                _button.onClick.AddListener(OpenStore);
            }
            if (_content != null) _content.SetActive(true);
        }

        /// <summary>Open the platform store page (built from the backend-provided ids).</summary>
        public void OpenStore()
        {
            string url = BuildStoreUrl();
            Debug.Log("[TwiceUpdatePrompt] Opening store: " + url);
            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
        }

        string BuildStoreUrl()
        {
#if UNITY_IOS && !UNITY_EDITOR
            // App Store app, opened directly via the numeric app id.
            return string.IsNullOrEmpty(_appId) ? "" : ("itms-apps://apps.apple.com/app/id" + _appId);
#else
            // Android (and Editor for quick testing): Play Store via package name.
            string pkg = string.IsNullOrEmpty(_bundleId) ? Application.identifier : _bundleId;
            return string.IsNullOrEmpty(pkg) ? "" : ("market://details?id=" + pkg);
#endif
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
