using UnityEngine;
using TMPro;
using TwiceSDK;

/// <summary>
/// Test helper: writes each Twice init step's name to a TMP text as the sequence runs
/// (Version Checker → Analytics → Remote Config → Ready). Put on the LoadingCanvas.
/// </summary>
public class LoadingInfo : MonoBehaviour
{
    [Tooltip("If empty, the first TMP_Text in children is used.")]
    public TMP_Text infoText;

    void Awake()
    {
        if (infoText == null) infoText = GetComponentInChildren<TMP_Text>(true);
        if (infoText != null) infoText.text = "";
        Twice.OnProgress += OnProgress;
    }

    void OnProgress(string step)
    {
        if (infoText == null) return;
        infoText.text += (infoText.text.Length > 0 ? "\n" : "") + "- " + step;
    }

    void OnDestroy()
    {
        Twice.OnProgress -= OnProgress;
    }
}
