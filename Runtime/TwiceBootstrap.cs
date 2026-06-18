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

        [Tooltip("The update prompt (a child of this object). If empty, the first one found in children is used.")]
        public TwiceUpdatePrompt updatePrompt;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (updatePrompt == null) updatePrompt = GetComponentInChildren<TwiceUpdatePrompt>(true);
            if (updatePrompt != null) updatePrompt.Hide(); // stay hidden until the check resolves

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
            if (status.UpdateAvailable && updatePrompt != null)
            {
                Debug.Log("[Twice] Update " + status.Action + " — showing prompt.");
                updatePrompt.Show(status);
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
