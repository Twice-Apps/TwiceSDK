using System;
using System.Collections;
using System.Collections.Generic;
using AssetKits.ParticleImage;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class ToastMessage : MonoBehaviour
{
    public static ToastMessage Instance { get; set; }

    private void Awake()
    {
        Instance = this;
    }

    public ParticleImage particle;
    public ParticleImage coinParticle;
    public Text toastMessage;
    public Text coinToastMessage;
    public CanvasGroup canvasGroup;
    public CanvasGroup coinCanvasGroup;
    
    public void PlayToastMessage(string _string)
    {
        toastMessage.transform.localScale = Vector3.zero;
        toastMessage.text = _string;

        canvasGroup.alpha = 1;
        canvasGroup.gameObject.SetActive(true);
        
        toastMessage.transform.DOScale(Vector3.one, 1f).SetEase(Ease.OutBack).OnComplete(() =>
        {
            particle.Play();
        });
        
        DOVirtual.DelayedCall(4, ()=>
        {
            canvasGroup.DOFade(0,0.5f).OnComplete(() =>
            {
                canvasGroup.gameObject.SetActive(false);
            });
        });
    }

    private bool isPlaying = false;
    
    public void PlayCoinToastMessage(string _string)
    {
        if (isPlaying) return;

        isPlaying = true;
        coinToastMessage.transform.localScale = Vector3.zero;
        coinToastMessage.text = _string;

        coinCanvasGroup.alpha = 1;
        coinCanvasGroup.gameObject.SetActive(true);
        
        coinToastMessage.transform.DOScale(Vector3.one, 1f).SetEase(Ease.OutBack).OnComplete(() =>
        {
            coinParticle.Play();
        });
        
        DOVirtual.DelayedCall(2.5f, ()=>
        {
            coinCanvasGroup.DOFade(0,0.5f).OnComplete(() =>
            {
                coinCanvasGroup.gameObject.SetActive(false);
                isPlaying = false;
            });
        });
    }
}
