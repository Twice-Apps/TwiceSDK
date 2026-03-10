using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if PLAYFAB_SDK
using TwiceSDK.Modules.Playfab;
#endif

#if GAMEANALYTICS_SDK
using TwiceSDK.Modules.GameAnalytics;
#endif

#if ENABLE_IAP
using TwiceSDK.Modules.IAP;
#endif

#if FIREBASE_CRASHLYTICS
using TwiceSDK.Modules.Crashlytics;
#endif

namespace TwiceSDK.Core
{
    /// <summary>
    /// TwiceSDK ana sınıfı - Singleton yapısı ile modülleri yönetir
    /// </summary>
    public class TwiceSDK : MonoBehaviour
    {
        private static TwiceSDK _instance;
        
        /// <summary>
        /// TwiceSDK örneğine erişim için Singleton örneği
        /// </summary>
        public static TwiceSDK Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject sdkObject = new GameObject("TwiceSDK");
                    _instance = sdkObject.AddComponent<TwiceSDK>();
                    DontDestroyOnLoad(sdkObject);
                }
                return _instance;
            }
        }

        [SerializeField] private TwiceSDKSettings _settings;
        
        private readonly Dictionary<Type, ISDKModule> _modules = new Dictionary<Type, ISDKModule>();
        private bool _isInitialized = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (_settings == null)
            {
                _settings = Resources.Load<TwiceSDKSettings>("TwiceSDKSettings");
                if (_settings == null)
                {
                    LogError("TwiceSDKSettings bulunamadı! Resources klasöründe TwiceSDKSettings bulunduğundan emin olun.");
                    return;
                }
            }

            if (_settings.AutoInitialize)
            {
                Initialize();
            }
        }

        /// <summary>
        /// SDK'yı başlatır ve ayarlara göre modülleri yükler
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
            {
                LogWarning("TwiceSDK zaten başlatılmış!");
                return;
            }

            LogInfo("TwiceSDK başlatılıyor...");
            
            // Modülleri yükle
            InitializeModules();
            
            _isInitialized = true;
            LogInfo("TwiceSDK başlatıldı!");
        }

        /// <summary>
        /// SDK modüllerini ayarlara göre başlatır
        /// </summary>
        private void InitializeModules()
        {
            // Playfab modülü
            if (_settings.UsePlayfab)
            {
                try 
                {
                    RegisterModule<Modules.Playfab.PlayfabModule>();
                }
                catch (Exception ex)
                {
                    LogError($"Playfab modülü yüklenirken hata: {ex.Message}");
                }
            }

            // GameAnalytics modülü
            if (_settings.UseGameAnalytics)
            {
                try 
                {
                    RegisterModule<Modules.GameAnalytics.GameAnalyticsModule>();
                }
                catch (Exception ex)
                {
                    LogError($"GameAnalytics modülü yüklenirken hata: {ex.Message}");
                }
            }

            // IAP modülü
            if (_settings.UseIAP)
            {
                try 
                {
                    RegisterModule<Modules.IAP.IAPModule>();
                }
                catch (Exception ex)
                {
                    LogError($"IAP modülü yüklenirken hata: {ex.Message}");
                }
            }

            // Crashlytics modülü
            if (_settings.UseCrashlytics)
            {
                try 
                {
                    RegisterModule<Modules.Crashlytics.CrashlyticsModule>();
                }
                catch (Exception ex)
                {
                    LogError($"Crashlytics modülü yüklenirken hata: {ex.Message}");
                }
            }

            // Tüm modülleri başlat
            foreach (var module in _modules.Values)
            {
                try
                {
                    module.Initialize(_settings);
                    LogInfo($"{module.ModuleName} modülü başlatıldı.");
                }
                catch (Exception e)
                {
                    LogError($"{module.ModuleName} modülü başlatılırken hata: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Modül ekler ve başlatır
        /// </summary>
        public void RegisterModule<T>() where T : ISDKModule, new()
        {
            Type moduleType = typeof(T);
            
            if (_modules.ContainsKey(moduleType))
            {
                LogWarning($"{moduleType.Name} modülü zaten eklenmiş!");
                return;
            }

            T module = new T();
            _modules[moduleType] = module;
            
            if (_isInitialized)
            {
                module.Initialize(_settings);
                LogInfo($"{module.ModuleName} modülü sonradan eklendi ve başlatıldı.");
            }
        }

        /// <summary>
        /// Belirtilen türdeki modülü döndürür
        /// </summary>
        public T GetModule<T>() where T : class, ISDKModule
        {
            Type moduleType = typeof(T);
            
            if (_modules.TryGetValue(moduleType, out ISDKModule module) && module is T typedModule)
            {
                return typedModule;
            }

            LogWarning($"{moduleType.Name} modülü bulunamadı.");
            return null;
        }

        /// <summary>
        /// Belirtilen türdeki modülü etkinleştirir
        /// </summary>
        public void EnableModule<T>() where T : class, ISDKModule
        {
            T module = GetModule<T>();
            if (module != null)
            {
                module.Enable();
                LogInfo($"{module.ModuleName} modülü etkinleştirildi.");
            }
        }

        /// <summary>
        /// Belirtilen türdeki modülü devre dışı bırakır
        /// </summary>
        public void DisableModule<T>() where T : class, ISDKModule
        {
            T module = GetModule<T>();
            if (module != null)
            {
                module.Disable();
                LogInfo($"{module.ModuleName} modülü devre dışı bırakıldı.");
            }
        }
        
        /// <summary>
        /// SDK ayarlarını döndürür
        /// </summary>
        public TwiceSDKSettings GetSettings()
        {
            return _settings;
        }

        #region Logging

        private void LogDebug(string message)
        {
            if (_settings != null && _settings.CurrentLogLevel >= TwiceSDKSettings.LogLevel.Debug)
                Debug.Log($"[TwiceSDK] {message}");
        }

        private void LogInfo(string message)
        {
            if (_settings != null && _settings.CurrentLogLevel >= TwiceSDKSettings.LogLevel.Info)
                Debug.Log($"[TwiceSDK] {message}");
        }

        private void LogWarning(string message)
        {
            if (_settings != null && _settings.CurrentLogLevel >= TwiceSDKSettings.LogLevel.Warning)
                Debug.LogWarning($"[TwiceSDK] {message}");
        }

        private void LogError(string message)
        {
            if (_settings != null && _settings.CurrentLogLevel >= TwiceSDKSettings.LogLevel.Error)
                Debug.LogError($"[TwiceSDK] {message}");
        }

        #endregion
    }
}
