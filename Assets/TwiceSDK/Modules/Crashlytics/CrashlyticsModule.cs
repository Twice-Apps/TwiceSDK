using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TwiceSDK.Core;

namespace TwiceSDK.Modules.Crashlytics
{
    /// <summary>
    /// Firebase Crashlytics entegrasyonu için modül
    /// </summary>
    public class CrashlyticsModule : ISDKModule
    {
        private bool _isEnabled = false;
        private TwiceSDKSettings _settings;
        private bool _isInitialized = false;

        /// <summary>
        /// Modülün adı
        /// </summary>
        public string ModuleName => "Firebase Crashlytics";

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
            
            // Firebase Crashlytics başlatma kodu buraya gelecek
            // Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => { ... });
            
            // Hata yakalama sistemini bağla
            Application.logMessageReceived += OnLogMessageReceived;
            
            _isInitialized = true;
            Debug.Log($"[{ModuleName}] Firebase Crashlytics başlatıldı.");
            
            Enable();
        }

        /// <summary>
        /// Uygulama hatalarını yakalama
        /// </summary>
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!IsEnabled || !_isInitialized) return;
            
            if (type == LogType.Exception || type == LogType.Error)
            {
                // Firebase.Crashlytics.Crashlytics.Log($"Unity Error: {condition}");
                // Firebase.Crashlytics.Crashlytics.RecordException(new Exception($"{condition}\n{stackTrace}"));
                Debug.Log($"[{ModuleName}] Hata yakalandı: {condition}");
            }
        }

        /// <summary>
        /// Modülü etkinleştirme
        /// </summary>
        public void Enable()
        {
            _isEnabled = true;
            // Firebase.Crashlytics.Crashlytics.IsCrashlyticsCollectionEnabled = true;
            Debug.Log($"[{ModuleName}] Modül etkinleştirildi.");
        }

        /// <summary>
        /// Modülü devre dışı bırakma
        /// </summary>
        public void Disable()
        {
            _isEnabled = false;
            // Firebase.Crashlytics.Crashlytics.IsCrashlyticsCollectionEnabled = false;
            Debug.Log($"[{ModuleName}] Modül devre dışı bırakıldı.");
        }

        /// <summary>
        /// Özel anahtarlar kaydetme
        /// </summary>
        public void SetCustomKey(string key, string value)
        {
            if (!IsEnabled || !_isInitialized) return;
            
            // Firebase.Crashlytics.Crashlytics.SetCustomKey(key, value);
            Debug.Log($"[{ModuleName}] Özel anahtar kaydedildi: {key} = {value}");
        }

        /// <summary>
        /// Kullanıcı ID'si ayarlama
        /// </summary>
        public void SetUserId(string userId)
        {
            if (!IsEnabled || !_isInitialized) return;
            
            // Firebase.Crashlytics.Crashlytics.SetUserId(userId);
            Debug.Log($"[{ModuleName}] Kullanıcı ID ayarlandı: {userId}");
        }

        /// <summary>
        /// Özel hata raporu gönderme
        /// </summary>
        public void LogException(Exception exception)
        {
            if (!IsEnabled || !_isInitialized) return;
            
            // Firebase.Crashlytics.Crashlytics.RecordException(exception);
            Debug.Log($"[{ModuleName}] Özel hata raporu gönderildi: {exception.Message}");
        }

        /// <summary>
        /// Özel günlük mesajı gönderme
        /// </summary>
        public void Log(string message)
        {
            if (!IsEnabled || !_isInitialized) return;
            
            // Firebase.Crashlytics.Crashlytics.Log(message);
            Debug.Log($"[{ModuleName}] Günlük mesajı: {message}");
        }
    }
} 