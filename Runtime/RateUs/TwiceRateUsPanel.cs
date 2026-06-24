using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TwiceSDK.RateUs
{
    /// <summary>
    /// The "do you like the game?" gate (AskFirst mode). It is its own Canvas prefab shipped at
    /// <c>Resources/TwiceRateUsPanel</c> and instantiated on demand by <see cref="TwiceRateUs.Show"/>
    /// — it is NOT a child of the bootstrap object, so the whole look is editable in that one prefab.
    /// "Yes" → native in-app review, "No" → just closes (so unhappy users never reach the store).
    /// </summary>
    [DisallowMultipleComponent]
    public class TwiceRateUsPanel : MonoBehaviour
    {
        /// <summary>Resources path (no extension) of the gate prefab.</summary>
        public const string PrefabResource = "TwiceRateUsPanel";

        [Tooltip("Positive button → opens the native review. If empty, the child named 'YesButton' is used.")]
        public Button yesButton;
        [Tooltip("Negative button → just closes. If empty, the child named 'NoButton' is used.")]
        public Button noButton;

        static TwiceRateUsPanel _current;

        /// <summary>Instantiate the gate prefab and show it. Falls back to the native review directly
        /// if the prefab can't be found. The window's texts live in the prefab itself.</summary>
        internal static void Present()
        {
            if (_current != null) { return; } // already on screen

            var prefab = Resources.Load<GameObject>(PrefabResource);
            if (prefab == null)
            {
                Debug.LogWarning("[TwiceRateUs] '" + PrefabResource + "' prefab'i bir Resources klasöründe bulunamadı → " +
                                 "doğrudan native review açılıyor.");
                TwiceRateUs.RequestNativeReview();
                return;
            }

            EnsureEventSystem();
            var go = Instantiate(prefab);
            go.name = prefab.name; // drop the "(Clone)" suffix
            DontDestroyOnLoad(go);

            var panel = go.GetComponentInChildren<TwiceRateUsPanel>(true);
            if (panel == null)
            {
                Debug.LogError("[TwiceRateUs] Gate prefab'inde TwiceRateUsPanel bileşeni yok.");
                Destroy(go);
                TwiceRateUs.RequestNativeReview();
                return;
            }
            _current = panel;
            panel.WireButtons();
        }

        void WireButtons()
        {
            if (yesButton == null) yesButton = FindByName<Button>("YesButton");
            if (noButton == null)  noButton  = FindByName<Button>("NoButton");

            if (yesButton != null) { yesButton.onClick.RemoveListener(OnYes); yesButton.onClick.AddListener(OnYes); }
            if (noButton != null)  { noButton.onClick.RemoveListener(OnNo);   noButton.onClick.AddListener(OnNo); }

            gameObject.SetActive(true);
        }

        void OnYes()
        {
            Close();
            TwiceRateUs.RequestNativeReview();
        }

        void OnNo()
        {
            Close();
        }

        void Close()
        {
            if (_current == this) _current = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_current == this) _current = null;
        }

        // ---- helpers ----
        static void EnsureEventSystem()
        {
#if UNITY_2023_1_OR_NEWER
            if (Object.FindFirstObjectByType<EventSystem>() == null)
#else
            if (Object.FindObjectOfType<EventSystem>() == null)
#endif
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                DontDestroyOnLoad(es);
            }
        }

        T FindByName<T>(string childName) where T : Component
        {
            foreach (var c in GetComponentsInChildren<T>(true))
                if (c.gameObject.name == childName) return c;
            return null;
        }
    }
}
