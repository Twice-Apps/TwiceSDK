using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TwiceSDK.Core;

namespace TwiceSDK.Modules.GameAnalytics
{
    /// <summary>
    /// GameAnalytics entegrasyonu için modül
    /// </summary>
    public class GameAnalyticsModule : ISDKModule
    {
        private bool _isEnabled = false;
        private TwiceSDKSettings _settings;

        /// <summary>
        /// Modülün adı
        /// </summary>
        public string ModuleName => "GameAnalytics";

        /// <summary>
        /// Modülün aktif olup olmadığını belirtir
        /// </summary>
        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// Modülü başlatma metodu
        /// </summary>
        public void Initialize(TwiceSDKSettings settings)
        {
            _settings = settings;
            
            // GameAnalytics SDK'yı başlat
            if (string.IsNullOrEmpty(_settings.GameAnalyticsGameKey) || string.IsNullOrEmpty(_settings.GameAnalyticsSecretKey))
            {
                Debug.LogError($"[{ModuleName}] GameAnalytics Game Key veya Secret Key ayarlanmamış!");
                return;
            }

            // GameAnalytics SDK başlatma kodu buraya gelecek
            // GameAnalytics.Initialize();
            
            Enable();
        }

        /// <summary>
        /// Modülü etkinleştirme
        /// </summary>
        public void Enable()
        {
            _isEnabled = true;
            Debug.Log($"[{ModuleName}] Modül etkinleştirildi.");
        }

        /// <summary>
        /// Modülü devre dışı bırakma
        /// </summary>
        public void Disable()
        {
            _isEnabled = false;
            Debug.Log($"[{ModuleName}] Modül devre dışı bırakıldı.");
        }

        // Olay gönderme metodları
        public void SendDesignEvent(string eventName, float? eventValue = null)
        {
            if (!IsEnabled) return;
            
            // GameAnalytics.Design.NewEvent(eventName, eventValue);
            Debug.Log($"[{ModuleName}] Design Event: {eventName}, Value: {eventValue}");
        }

        public void SendProgressionEvent(string status, string progression1, string progression2 = null, string progression3 = null, int? score = null)
        {
            if (!IsEnabled) return;
            
            // GameAnalytics.Progression.NewEvent(status, progression1, progression2, progression3, score);
            Debug.Log($"[{ModuleName}] Progression Event: {status}, {progression1}, {progression2}, {progression3}, Score: {score}");
        }

        public void SendResourceEvent(string flowType, string currency, float amount, string itemType, string itemId)
        {
            if (!IsEnabled) return;
            
            // GameAnalytics.Resource.NewEvent(flowType, currency, amount, itemType, itemId);
            Debug.Log($"[{ModuleName}] Resource Event: {flowType}, {currency}, {amount}, {itemType}, {itemId}");
        }
    }
} 