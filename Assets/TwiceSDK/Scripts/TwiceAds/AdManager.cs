using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VInspector;

public class AdManager : AppLovinCustomSettings
{
    #region Singleton
    public static AdManager Instance { get; set; }
    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    #endregion

    [Tab("Timed Inter")]
    public bool isTimedInter;
    public float interval;
    public float timer = 180f;
    [EndTab]
    [Tab("Welcome User")]
    public bool welcomeUser;
    public float welcomeInterval;
    public float welcomeTimer = 120f;
    [EndTab]
    private void Start()
    {
        MaxSdkCallbacks.OnSdkInitializedEvent += (MaxSdkBase.SdkConfiguration sdkConfiguration) => 
        {
            // AppLovin SDK is initialized, start loading ads
            //MaxSdk.CreateBanner(bannerAdUnitId,MaxSdkBase.BannerPosition.BottomCenter);
            //MaxSdk.SetBannerBackgroundColor(bannerAdUnitId,Color.black);
            
            InitializeBannerAds();
            InitializeRewardedAds();
            InitializeInterstitialAds();
        };


        if (PlayerPrefs.GetInt("WelcomeUser",0) == 0)
        {
            welcomeUser = true;
            PlayerPrefs.SetInt("WelcomeUser", 1);
        }
        else
        {
            welcomeUser = false;
        }
        
        //MaxSdk.SetSdkKey(sdkKey);
        MaxSdk.SetUserId(userId);
        MaxSdk.InitializeSdk();
    }

    private void Update()
    {
        if (welcomeUser)
        {
            welcomeInterval += Time.deltaTime;
       
            if (welcomeInterval >= welcomeTimer)
            {
                interval = 0;
                welcomeUser = false;
            }
        }

        if (!welcomeUser)//!GleyIAP.Instance.adsRemoved)
        {
            if (isTimedInter)
            {
                interval += Time.deltaTime;

                /*
                if (interval + 10 >= timer)
                {
                    if(!adsComing.activeSelf) adsComing.SetActive(true);
                    adsComingText.text = $"Ads in {(timer - interval):0}..";
                }
                */
        
                if (interval >= timer)
                {
                    interval = 0f;
                    ShowInterstitial();
                }
            }
        }
    }

    #region Banner Usage

    public void ShowBanner()
    {
        //if (GleyIAP.Instance.adsRemoved) return;
        MaxSdk.ShowBanner(bannerAdUnitId);
    }

    public void HideBanner() => MaxSdk.HideBanner(bannerAdUnitId);
    public void DestroyBanner() => MaxSdk.DestroyBanner(bannerAdUnitId);

    #endregion

    #region Interstitial Usage
    public void ShowInterstitial()
    {
        //if (GleyIAP.Instance.adsRemoved) return;
        if (welcomeUser) return;
        //if (First2Minutes.Instance.isActive == 1) return;
        //if (GleyIAP.Instance.weeklySub) return;
        
        //ToastManager.Instance.Toast();
        
        if (MaxSdk.IsInterstitialReady(interstitialAdUnitId))
        {
            int currentInterCount = PlayerPrefs.GetInt("InterstitialCount", 0);
            PlayerPrefs.SetInt("InterstitialCount", currentInterCount + 1);
        
            if (PlayerPrefs.GetInt("InterstitialCount", 0) >= GetInterstitialClickCount())
            {
                MaxSdk.ShowInterstitial(interstitialAdUnitId);
                interval = 0f;
                //adsComing.SetActive(false);
                
                PlayerPrefs.SetInt("InterstitialCount",0);
            }
        }
        else
        {
            //AD NOT READY
            //ToastManager.Instance.Toast();
        }
    }
    public void ShowInterstitial(RewardCallbackInterstitial _rewardCallback)
    {
        if (true)//GleyIAP.Instance.adsRemoved || welcomeUser || GleyIAP.Instance.weeklySub)
        {
            ResetCallbackInterstitial();
            rewardCallbackInterstitial = _rewardCallback;
            OnRewardInterstitial += rewardCallbackInterstitial;
        }
        else
        {
            if (MaxSdk.IsInterstitialReady(interstitialAdUnitId))
            {
                MaxSdk.ShowInterstitial(interstitialAdUnitId);
                interval = 0f;
                //adsComing.SetActive(false);
            
                ResetCallbackInterstitial();
                rewardCallbackInterstitial = _rewardCallback;
                OnRewardInterstitial += rewardCallbackInterstitial;
                
            }
        }
    }
    
    public int GetInterstitialClickCount()
    {
        int currentLevel = PlayfabManager.Instance.playerLevel;
        return 1;
        //return PlayfabManager.Instance.currentInterstitialData.FirstOrDefault(d => currentLevel >= d.from && currentLevel < d.to)?.buttonClickedForInterstitial ?? PlayfabManager.Instance.currentInterstitialData[0].buttonClickedForInterstitial;
    }
    #endregion

    #region Rewarded Usage
    public void ShowRewardedAd(RewardCallback _rewardCallback)
    {
        if (MaxSdk.IsRewardedAdReady(rewardedAdUnitId))
        {
            MaxSdk.ShowRewardedAd(rewardedAdUnitId);
            
            ResetCallback();
            rewardCallback = _rewardCallback;
            OnReward += rewardCallback;
        }
    }

    #endregion


}
