using UnityEngine;
using UnityEngine.UI;

namespace TwiceSDK.VersionCheck
{
    /// <summary>
    /// Display-only update prompt — lives as a (hidden) child of the Twice bootstrap object so the
    /// whole look is a prefab you can restyle per game. The bootstrap calls <see cref="Show"/> when
    /// an update is required, which reveals it and wires the Update button to the store. The UI
    /// (tint / title / button) is whatever you build in the prefab; only an Update button is needed.
    /// </summary>
    [DisallowMultipleComponent]
    public class TwiceUpdatePrompt : MonoBehaviour
    {
        [Tooltip("The Update button. If left empty, the first Button found in children is used.")]
        public Button updateButton;

        string _appId;
        string _bundleId;

        /// <summary>Reveal the prompt and wire the Update button for this status.</summary>
        public void Show(UpdateStatus status)
        {
            _appId = status.AppId;
            _bundleId = status.BundleId;

            if (updateButton == null) updateButton = GetComponentInChildren<Button>(true);
            if (updateButton != null)
            {
                updateButton.onClick.RemoveListener(OpenStore);
                updateButton.onClick.AddListener(OpenStore);
            }
            gameObject.SetActive(true);
        }

        /// <summary>Hide the prompt (bootstrap calls this at startup so it stays hidden until needed).</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
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
            return string.IsNullOrEmpty(_appId) ? "" : ("itms-apps://apps.apple.com/app/id" + _appId);
#else
            string pkg = string.IsNullOrEmpty(_bundleId) ? Application.identifier : _bundleId;
            return string.IsNullOrEmpty(pkg) ? "" : ("market://details?id=" + pkg);
#endif
        }
    }
}
