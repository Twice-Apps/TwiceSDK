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

    /// <summary>
    /// Dev-time notice: when Play mode starts and no <see cref="TwiceBootstrap"/> (the TwiceSDK
    /// prefab) is present in the loaded scene, write a coloured console line explaining what that
    /// means for the chosen <see cref="InitializationMode"/>. Editor / development builds only.
    /// </summary>
    internal static class TwiceSceneCheck
    {
        const string Tag = "<color=#FF5A36><b>[TwiceSDK]</b></color>";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CheckBootstrapPresence()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Object.FindAnyObjectByType<TwiceBootstrap>(FindObjectsInactive.Include) != null) return;

            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s == null)
            {
                Debug.LogWarning(Tag + " Sahnede TwiceSDK objesi yok ve TwiceSettings asset'i de bulunamadı → SDK pasif.");
            }
            else if (s.initialization == InitializationMode.RequireBootstrap)
            {
                Debug.LogWarning(Tag + " Sahnede <b>TwiceSDK</b> objesi YOK ve init modu <b>RequireBootstrap</b> → " +
                                 "SDK BAŞLATILMADI. TwiceSDK prefab'ini ilk/preloader sahnene ekle.");
            }
            else
            {
                Debug.Log(Tag + " Sahnede TwiceSDK objesi yok; <b>Auto</b> modda ayar asset'inden otomatik başlatıldı. " +
                          "Sıralı init + güncelleme promptu istiyorsan TwiceSDK prefab'ini ekleyebilirsin.");
            }
#endif
        }
    }
}
