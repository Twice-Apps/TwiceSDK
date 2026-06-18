using UnityEngine;
using TwiceSDK.VersionCheck;

namespace TwiceSDK
{
    /// <summary>
    /// Single drop-in SDK entry point. Drag the <c>Twice</c> prefab into your first / preloader
    /// scene — it carries this component plus a child update-prompt Canvas you can restyle per game.
    /// On start it initializes the SDK and, when a newer required/optional store version exists,
    /// reveals the prompt; otherwise the prompt stays hidden. Nothing else to wire — analytics +
    /// remote config already auto-start from the TwiceSettings asset. Singleton; survives scene loads.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Twice/Twice Bootstrap")]
    public class TwiceBootstrap : MonoBehaviour
    {
        static TwiceBootstrap _instance;

        [Tooltip("Run the version check on start and reveal the update prompt when an update is needed.")]
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
        }

        void Start()
        {
            if (_instance != this) return;
            Twice.Initialize();
            if (!checkForUpdates) return;

            Debug.Log("[Twice] Bootstrap: checking app version…");
            TwiceVersionChecker.Check(OnVersionResult);
        }

        void OnVersionResult(UpdateStatus status)
        {
            if (status.UpdateAvailable)
            {
                Debug.Log("[Twice] Update " + status.Action + " — showing prompt.");
                if (updatePrompt != null) updatePrompt.Show(status);
                else Debug.LogWarning("[Twice] Update needed but no TwiceUpdatePrompt found under the Twice object.");
            }
            else
            {
                Debug.Log("[Twice] App is up to date — no prompt.");
            }
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
