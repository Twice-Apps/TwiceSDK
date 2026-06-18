using UnityEngine;
using UnityEngine.UI;

namespace TwiceSDK.VersionCheck
{
    /// <summary>
    /// Controller for the VersionChecker prefab (a full-screen, input-blocking Canvas). On Start it
    /// asks the backend whether the running build is behind the configured version; if an update is
    /// needed it reveals the prompt and wires the Update button to the store, otherwise it destroys
    /// itself. The store URL — including the iOS App Store app id — comes from the Twice web panel
    /// via the version-check response, so nothing platform-specific is baked into the build.
    ///
    /// UI children are found by name (Blocker / a Button in the hierarchy), so the prefab needs no
    /// manual reference wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public class TwiceUpdatePrompt : MonoBehaviour
    {
        [Tooltip("Also show the prompt for optional (non-forced) updates. If false, optional updates only log and the object is destroyed.")]
        public bool showForOptional = true;

        GameObject _content; // the "Blocker" tint subtree
        Button _button;
        string _storeUrl;

        void Awake()
        {
            var blocker = transform.Find("Blocker");
            _content = blocker != null ? blocker.gameObject : null;
            _button = GetComponentInChildren<Button>(true);
            if (_content != null) _content.SetActive(false); // stay hidden until the check resolves
        }

        void Start()
        {
            Debug.Log("[TwiceUpdatePrompt] Checking app version…");
            TwiceVersionChecker.Check(OnResult);
        }

        void OnResult(UpdateStatus status)
        {
            _storeUrl = status.StoreUrl;

            bool show = status.IsForced || (status.IsOptional && showForOptional);
            if (!show)
            {
                Debug.Log("[TwiceUpdatePrompt] No update needed (action=" + status.Action + ") — destroying prompt.");
                Destroy(gameObject);
                return;
            }

            Debug.Log("[TwiceUpdatePrompt] Update " + status.Action + " — showing prompt. store_url='" + _storeUrl + "'");
            if (_button != null)
            {
                _button.onClick.RemoveListener(OpenStore);
                _button.onClick.AddListener(OpenStore);
            }
            if (_content != null) _content.SetActive(true);
        }

        /// <summary>Open the store page. Uses the panel-provided URL; falls back to the platform store.</summary>
        public void OpenStore()
        {
            string url = _storeUrl;
            if (string.IsNullOrEmpty(url))
            {
#if UNITY_ANDROID
                url = "market://details?id=" + Application.identifier; // Play uses the package name
#elif UNITY_IOS
                // Weak fallback — the numeric App Store id must come from the panel's iOS store URL.
                url = "itms-apps://itunes.apple.com/app/" + Application.identifier;
#endif
            }
            Debug.Log("[TwiceUpdatePrompt] Opening store: " + url);
            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
        }
    }
}
