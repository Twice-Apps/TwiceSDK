using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class VersionChecker : MonoBehaviour
{
    #region Singleton
    public static VersionChecker Instance { get; set; }
    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    #endregion
    
    [Tooltip("1.0(1)")]
    public string version;
    [SerializeField] private int appleAppID;

    [Space(20)] 
    [SerializeField] private CanvasGroup canvasGroup;
    
    
    public void UpdateGame()
    {
        #if UNITY_ANDROID
            Application.OpenURL("market://details?id=" + Application.identifier);
        #elif UNITY_IOS
            Application.OpenURL($"itms-apps://itunes.apple.com/app/{appleAppID}");
        #endif
    }

    public void OpenCanvas()
    {
        canvasGroup.gameObject.SetActive(true);
        
        canvasGroup.alpha = 0;
        canvasGroup.DOFade(1, .35f).SetEase(Ease.OutBack);
    }
}
