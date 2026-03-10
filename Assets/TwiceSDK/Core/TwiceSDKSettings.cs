using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace TwiceSDK.Core
{
    /// <summary>
    /// TwiceSDK için ayarlar sınıfı
    /// </summary>
    [CreateAssetMenu(fileName = "TwiceSDKSettings", menuName = "TwiceSDK/Settings", order = 1)]
    public class TwiceSDKSettings : ScriptableObject
    {
        [Header("Genel Ayarlar")]
        [Tooltip("SDK oyun başladığında otomatik olarak başlatılsın mı?")]
        [SerializeField] private bool _autoInitialize = true;
        
        [Tooltip("SDK log mesajlarının detay seviyesi")]
        [SerializeField] private LogLevel _logLevel = LogLevel.Warning;
        
        [Header("Modül Konfigürasyonları")]
        [SerializeField] private PlayfabConfig _playfabConfig = new PlayfabConfig();
        [SerializeField] private GameAnalyticsConfig _gameAnalyticsConfig = new GameAnalyticsConfig();
        [SerializeField] private IAPConfig _iapConfig = new IAPConfig();
        [SerializeField] private CrashlyticsConfig _crashlyticsConfig = new CrashlyticsConfig();
        [SerializeField] private AdsConfig _adsConfig = new AdsConfig();
        
        // Özel modüller için liste
        [SerializeField] private List<ModuleConfigWrapper> _customModules = new List<ModuleConfigWrapper>();

        public enum LogLevel
        {
            None,
            Error,
            Warning,
            Info,
            Debug
        }

        // Genel Ayarlar
        public bool AutoInitialize => _autoInitialize;
        public LogLevel CurrentLogLevel => _logLevel;

        // Modül Konfigürasyonları
        public PlayfabConfig PlayfabConfig => _playfabConfig;
        public GameAnalyticsConfig GameAnalyticsConfig => _gameAnalyticsConfig;
        public IAPConfig IAPConfig => _iapConfig;
        public CrashlyticsConfig CrashlyticsConfig => _crashlyticsConfig;
        public AdsConfig AdsConfig => _adsConfig;
        public List<ModuleConfigWrapper> CustomModules => _customModules;

        // Modül erişim yardımcı metodları
        public bool UsePlayfab => _playfabConfig?.IsEnabled ?? false;
        public bool UseGameAnalytics => _gameAnalyticsConfig?.IsEnabled ?? false;
        public bool UseIAP => _iapConfig?.IsEnabled ?? false;
        public bool UseCrashlytics => _crashlyticsConfig?.IsEnabled ?? false;
        public bool UseAds => _adsConfig?.IsEnabled ?? false;

        // Playfab Ayarları
        public string PlayfabTitleId => _playfabConfig?.TitleId ?? "";

        // GameAnalytics Ayarları
        public string GameAnalyticsGameKey => _gameAnalyticsConfig?.GameKey ?? "";
        public string GameAnalyticsSecretKey => _gameAnalyticsConfig?.SecretKey ?? "";

        // Ads Ayarları
        public string AdsSdkKey => _adsConfig?.SdkKey ?? "";
        public string AdsBannerAdUnitId => _adsConfig?.BannerAdUnitId ?? "";
        public string AdsInterstitialAdUnitId => _adsConfig?.InterstitialAdUnitId ?? "";
        public string AdsRewardedAdUnitId => _adsConfig?.RewardedAdUnitId ?? "";

        /// <summary>
        /// Modül konfigürasyonunu döndürür
        /// </summary>
        public T GetModuleConfig<T>() where T : ModuleConfig
        {
            if (typeof(T) == typeof(PlayfabConfig))
                return _playfabConfig as T;
            
            if (typeof(T) == typeof(GameAnalyticsConfig))
                return _gameAnalyticsConfig as T;
            
            if (typeof(T) == typeof(IAPConfig))
                return _iapConfig as T;
            
            if (typeof(T) == typeof(CrashlyticsConfig))
                return _crashlyticsConfig as T;
            
            if (typeof(T) == typeof(AdsConfig))
                return _adsConfig as T;
            
            // Özel modüller arasında ara
            foreach (var wrapper in _customModules)
            {
                if (wrapper.Config != null && wrapper.Config.GetType() == typeof(T))
                    return wrapper.Config as T;
            }
            
            return null;
        }
        
        /// <summary>
        /// Modül tipine göre konfigürasyonu döndürür
        /// </summary>
        public ModuleConfig GetModuleConfig(ModuleType moduleType)
        {
            switch (moduleType)
            {
                case ModuleType.Playfab:
                    return _playfabConfig;
                case ModuleType.GameAnalytics:
                    return _gameAnalyticsConfig;
                case ModuleType.IAP:
                    return _iapConfig;
                case ModuleType.Crashlytics:
                    return _crashlyticsConfig;
                case ModuleType.Ads:
                    return _adsConfig;
                case ModuleType.Custom:
                    Debug.LogWarning("Özel modül tipi için doğrudan GetModuleConfig<T>() metodunu kullanın.");
                    return null;
                default:
                    return null;
            }
        }
        
        /// <summary>
        /// Özel modül ekler
        /// </summary>
        public void AddCustomModule(ModuleConfig config)
        {
            if (config == null)
                return;
                
            var wrapper = new ModuleConfigWrapper
            {
                Config = config
            };
            
            _customModules.Add(wrapper);
        }

        /// <summary>
        /// Ayarları kendine çevirir - editörden erişim için
        /// </summary>
        public void OnValidate()
        {
            // Modül konfigürasyonlarını oluştur
            if (_playfabConfig == null)
                _playfabConfig = new PlayfabConfig();
            
            if (_gameAnalyticsConfig == null)
                _gameAnalyticsConfig = new GameAnalyticsConfig();
            
            if (_iapConfig == null)
                _iapConfig = new IAPConfig();
            
            if (_crashlyticsConfig == null)
                _crashlyticsConfig = new CrashlyticsConfig();
                
            if (_adsConfig == null)
                _adsConfig = new AdsConfig();
        }
    }
    
    /// <summary>
    /// Özel modül config wrapper sınıfı
    /// SerializeReference ile modül konfigürasyon sınıflarını serialize edebilmek için
    /// </summary>
    [Serializable]
    public class ModuleConfigWrapper
    {
        [SerializeReference] public ModuleConfig Config;
    }

    /// <summary>
    /// Reklamlar (AppLovin MAX) ayarları
    /// </summary>
    [Serializable]
    public class AdsConfig : ModuleConfig
    {
        [SerializeField] private string _sdkKey;
        [SerializeField] private string _bannerAdUnitId;
        [SerializeField] private string _interstitialAdUnitId;
        [SerializeField] private string _rewardedAdUnitId;
        
        public string SdkKey => _sdkKey;
        public string BannerAdUnitId => _bannerAdUnitId;
        public string InterstitialAdUnitId => _interstitialAdUnitId;
        public string RewardedAdUnitId => _rewardedAdUnitId;
        
        public override string DisplayName => "AppLovin MAX";
        public override string Description => "Reklam entegrasyonu için AppLovin MAX SDK";
        public override ModuleType ModuleType => ModuleType.Ads;
        public override Texture2D Icon => Resources.Load<Texture2D>("TwiceSDK/Icons/ads");
    }
} 