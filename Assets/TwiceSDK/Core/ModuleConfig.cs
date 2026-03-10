using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace TwiceSDK.Core
{
    /// <summary>
    /// Modül konfigürasyonu temel sınıfı
    /// </summary>
    [Serializable]
    public abstract class ModuleConfig
    {
        [SerializeField] protected bool _isEnabled = false;
        
        /// <summary>
        /// Modülün etkin olup olmadığı
        /// </summary>
        public bool IsEnabled 
        { 
            get => _isEnabled; 
            set => _isEnabled = value; 
        }
        
        /// <summary>
        /// Modül tipi
        /// </summary>
        public abstract ModuleType ModuleType { get; }
        
        /// <summary>
        /// Modül adı
        /// </summary>
        public abstract string DisplayName { get; }
        
        /// <summary>
        /// Modülün açıklaması
        /// </summary>
        public abstract string Description { get; }
        
        /// <summary>
        /// Modül için simge
        /// </summary>
        public abstract Texture2D Icon { get; }
    }
    
    /// <summary>
    /// PlayFab modülü ayarları
    /// </summary>
    [Serializable]
    public class PlayfabConfig : ModuleConfig
    {
        [SerializeField] private string _titleId;
        public string TitleId => _titleId;
        
        public override string DisplayName => "PlayFab";
        public override string Description => "Çevrimiçi hizmetler ve bulut çözümleri";
        public override ModuleType ModuleType => ModuleType.Playfab;
        public override Texture2D Icon => Resources.Load<Texture2D>("TwiceSDK/Icons/playfab");
    }
    
    /// <summary>
    /// GameAnalytics modülü ayarları
    /// </summary>
    [Serializable]
    public class GameAnalyticsConfig : ModuleConfig
    {
        [SerializeField] private string _gameKey;
        [SerializeField] private string _secretKey;
        
        public string GameKey => _gameKey;
        public string SecretKey => _secretKey;
        
        public override string DisplayName => "GameAnalytics";
        public override string Description => "Oyun analiz ve metrik servisi";
        public override ModuleType ModuleType => ModuleType.GameAnalytics;
        public override Texture2D Icon => Resources.Load<Texture2D>("TwiceSDK/Icons/gameanalytics");
    }
    
    /// <summary>
    /// In-App Purchase modülü ayarları
    /// </summary>
    [Serializable]
    public class IAPConfig : ModuleConfig
    {
        public override string DisplayName => "In-App Purchase";
        public override string Description => "Uygulama içi satın alma sistemi";
        public override ModuleType ModuleType => ModuleType.IAP;
        public override Texture2D Icon => Resources.Load<Texture2D>("TwiceSDK/Icons/iap");
    }
    
    /// <summary>
    /// Firebase Crashlytics modülü ayarları
    /// </summary>
    [Serializable]
    public class CrashlyticsConfig : ModuleConfig
    {
        public override string DisplayName => "Firebase Crashlytics";
        public override string Description => "Çökme raporlama ve analiz servisi";
        public override ModuleType ModuleType => ModuleType.Crashlytics;
        public override Texture2D Icon => Resources.Load<Texture2D>("TwiceSDK/Icons/crashlytics");
    }
} 