using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class Popup : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Transform popup;

    [SerializeField] private PopupType popupType;
    [SerializeField] private bool isLeaderboard;
    
    public virtual void OnEnable()
    {
        canvasGroup.alpha = 0;
        
        if (popupType == PopupType.Pop)
        {
            canvasGroup.DOFade(1, 0.5f);
            
            popup.transform.localScale = Vector3.zero;
            popup.DOScale(Vector3.one, 0.35f).SetDelay(0.25f).SetEase(Ease.OutBack);
            
            EventManager.OnPopupOpened?.Invoke(this);
        }
        else
        {
            RectTransform p = popup.GetComponent<RectTransform>();

            if (isLeaderboard)
            {
                p.offsetMin = new Vector2(-Screen.width, p.offsetMin.y);
                p.offsetMax = new Vector2(-Screen.width, p.offsetMax.y);
            }
            else
            {
                p.offsetMin = new Vector2(Screen.width, p.offsetMin.y);
                p.offsetMax = new Vector2(Screen.width, p.offsetMax.y);
            }

            canvasGroup.DOFade(1, 0.2f);
            p.DOAnchorPosX(0, 0.25f).SetEase(Ease.OutQuad);
        }
    }

    public void SlideBack()
    {
        RectTransform p = popup.GetComponent<RectTransform>();

        Vector2 targetOffsetMin, targetOffsetMax;

        if (isLeaderboard)
        {
            targetOffsetMin = new Vector2(-Screen.width, p.offsetMin.y);
            targetOffsetMax = new Vector2(-Screen.width, p.offsetMax.y);
        }
        else
        {
            targetOffsetMin = new Vector2(Screen.width, p.offsetMin.y);
            targetOffsetMax = new Vector2(Screen.width, p.offsetMax.y);
        }

        canvasGroup.DOFade(0, 0.2f);
        
        // Paneli kaydırma animasyonu
        DOTween.To(() => p.offsetMin, x => p.offsetMin = x, targetOffsetMin, 0.25f);
        DOTween.To(() => p.offsetMax, x => p.offsetMax = x, targetOffsetMax, 0.25f)
            .OnComplete(() => gameObject.SetActive(false)); // Animasyon tamamlandıktan sonra paneli kapat
    }
}
public enum PopupType{Pop,Slide}