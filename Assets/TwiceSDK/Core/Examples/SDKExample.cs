using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TwiceSDK.Core;
using TwiceSDK.Modules.Playfab;
using TwiceSDK.Modules.GameAnalytics;
using TwiceSDK.Modules.IAP;
using TwiceSDK.Modules.Crashlytics;

namespace TwiceSDK.Examples
{
    /// <summary>
    /// TwiceSDK'nın nasıl kullanılacağını gösteren örnek sınıf
    /// </summary>
    public class SDKExample : MonoBehaviour
    {
        private Core.TwiceSDK _sdk;
        
        private void Start()
        {
            // SDK örneğini al
            _sdk = Core.TwiceSDK.Instance;
            
            // 1. SDK'yı başlatma
            InitializeSDK();
            
            // 2. SDK'yı kullanma örnekleri
            UsePlayfab();
            UseGameAnalytics();
            UseIAP();
            UseCrashlytics();
        }

        private void InitializeSDK()
        {
            // SDK'yı manuel olarak başlatmak için
            // Not: Eğer ayarlarda AutoInitialize aktifse, bu adım gerekli değildir
            _sdk.Initialize();
            
            Debug.Log("SDK başlatıldı");
            
            // SDK ayarlarına erişim
            var settings = _sdk.GetSettings();
            Debug.Log($"Geçerli log seviyesi: {settings.CurrentLogLevel}");
            Debug.Log($"PlayFab etkin mi: {settings.UsePlayfab}");
            Debug.Log($"GameAnalytics etkin mi: {settings.UseGameAnalytics}");
        }

        private void UsePlayfab()
        {
            // PlayfabModule'ün erişimi
            PlayfabModule playfab = _sdk.GetModule<PlayfabModule>();
            if (playfab != null)
            {
                Debug.Log($"Playfab modülü durumu: {playfab.IsEnabled}");
                
                // PlayFab ayarlarına erişim
                var config = _sdk.GetSettings().PlayfabConfig;
                Debug.Log($"PlayFab Title ID: {config.TitleId}");
                
                // Devre dışı bırakmak için
                // _sdk.DisableModule<PlayfabModule>();
                
                // Tekrar etkinleştirmek için
                // _sdk.EnableModule<PlayfabModule>();
            }
            else
            {
                Debug.Log("Playfab modülü yüklenmemiş!");
            }
        }

        private void UseGameAnalytics()
        {
            // GameAnalyticsModule'ün erişimi
            GameAnalyticsModule analytics = _sdk.GetModule<GameAnalyticsModule>();
            if (analytics != null)
            {
                Debug.Log($"GameAnalytics modülü durumu: {analytics.IsEnabled}");
                
                // GameAnalytics ayarlarına erişim
                var config = _sdk.GetSettings().GameAnalyticsConfig;
                Debug.Log($"GameAnalytics Game Key: {config.GameKey}");
                Debug.Log($"GameAnalytics Secret Key: {config.SecretKey}");
                
                // Örnek kullanım
                analytics.SendDesignEvent("Button_Clicked", 1);
                analytics.SendProgressionEvent("Start", "Level_1");
                analytics.SendResourceEvent("Spend", "Coins", 100, "Item", "Sword");
            }
            else
            {
                Debug.Log("GameAnalytics modülü yüklenmemiş!");
            }
        }

        private void UseIAP()
        {
            // IAPModule'ün erişimi
            IAPModule iap = _sdk.GetModule<IAPModule>();
            if (iap != null)
            {
                Debug.Log($"IAP modülü durumu: {iap.IsEnabled}");
                
                // IAP ayarlarına erişim
                var config = _sdk.GetSettings().IAPConfig;
                
                // Örnek ürün ekleme
                IAPProduct product = new IAPProduct(
                    "com.twicesdk.goldpack",
                    "Consumable",
                    "Gold Pack",
                    "1000 gold"
                );
                
                iap.AddProduct(product);
                
                // Ürün satın alma
                iap.PurchaseProduct("com.twicesdk.goldpack");
            }
            else
            {
                Debug.Log("IAP modülü yüklenmemiş!");
            }
        }

        private void UseCrashlytics()
        {
            // CrashlyticsModule'ün erişimi
            CrashlyticsModule crashlytics = _sdk.GetModule<CrashlyticsModule>();
            if (crashlytics != null)
            {
                Debug.Log($"Crashlytics modülü durumu: {crashlytics.IsEnabled}");
                
                // Crashlytics ayarlarına erişim
                var config = _sdk.GetSettings().CrashlyticsConfig;
                
                // Örnek kullanım
                crashlytics.SetUserId("user123");
                crashlytics.SetCustomKey("level", "5");
                crashlytics.Log("Oyun başlatıldı");
                
                // Örnek hata raporlama
                try
                {
                    // Hata üretecek bir kod
                    int x = 0;
                    int y = 10 / x; // Divide by zero
                }
                catch (System.Exception e)
                {
                    crashlytics.LogException(e);
                }
            }
            else
            {
                Debug.Log("Crashlytics modülü yüklenmemiş!");
            }
        }
    }
} 