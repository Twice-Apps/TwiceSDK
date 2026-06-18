using UnityEngine;
using TwiceSDK.VersionCheck;

namespace TwiceSDK
{
    /// <summary>
    /// Single drop-in SDK entry point. Drag the <c>TwiceSDK</c> prefab into your first / preloader
    /// scene — one object, nothing else to wire. On start it calls <see cref="Twice.Initialize"/>
    /// (version check → analytics → remote config, in order) and, when the version check reports a
    /// required/optional update, reveals its child update-prompt Canvas (restylable per game).
    /// Singleton; survives scene loads.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Twice/Twice Bootstrap")]
    public class TwiceBootstrap : MonoBehaviour
    {
        static TwiceBootstrap _instance;

        [Tooltip("Reveal the update prompt when the version check reports an update is needed.")]
        public bool checkForUpdates = true;

        // Auto-resolved from the child VersionChecker Canvas — no wiring needed.
        TwiceUpdatePrompt _prompt;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _prompt = GetComponentInChildren<TwiceUpdatePrompt>(true);
            if (_prompt != null) _prompt.Hide(); // stay hidden until the check resolves

            Twice.OnVersionChecked += OnVersionChecked;
        }

        void Start()
        {
            if (_instance != this) return;
            Twice.Initialize();
        }

        void OnVersionChecked(UpdateStatus status)
        {
            if (!checkForUpdates) return;
            if (status.UpdateAvailable && _prompt != null)
            {
                Debug.Log("[Twice] Update " + status.Action + " — showing prompt.");
                _prompt.Show(status);
            }
            else
            {
                Debug.Log("[Twice] App is up to date — no prompt.");
            }
        }

        void OnDestroy()
        {
            Twice.OnVersionChecked -= OnVersionChecked;
            if (_instance == this) _instance = null;
        }
    }
}
