using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class HeartbeatAnimation : MonoBehaviour
{
    public float initialDelay = 0.5f;
    public float animationEndDelay = 1.0f;
    public float scaleUpSize = 1.2f; // Scale up size
    public float animationDuration = 0.5f; // Duration for one heartbeat
    private RectTransform rectTransform; // Reference to RectTransform of the UI element
    private Sequence heartbeatSequence;
    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        DOVirtual.DelayedCall(initialDelay, StartHeartbeat);
    }

    void StartHeartbeat()
    {
        // Create a sequence for the heartbeat animation
        heartbeatSequence = DOTween.Sequence();

        heartbeatSequence.PrependInterval(animationEndDelay);
        
        // Add scaling up and down to the sequence
        heartbeatSequence.Append(rectTransform.DOScale(scaleUpSize, animationDuration / 2).SetEase(Ease.InOutQuad));
        heartbeatSequence.Append(rectTransform.DOScale(1f, animationDuration / 2).SetEase(Ease.InOutQuad));

        // Loop the sequence indefinitely
        heartbeatSequence.SetLoops(-1);
    }

    public void StopAnimation()
    {
        heartbeatSequence.Kill();
    }
}