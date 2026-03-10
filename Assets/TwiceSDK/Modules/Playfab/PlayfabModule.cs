using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TwiceSDK.Core;

namespace TwiceSDK.Modules.Playfab
{
    /// <summary>
    /// Playfab entegrasyonu için modül
    /// </summary>
    public class PlayfabModule : ISDKModule
    {
        private bool _isEnabled = false;
        private TwiceSDKSettings _settings;

        /// <summary>
        /// Modülün adı
        /// </summary>
        public string ModuleName => "Playfab";

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
            
            // Playfab SDK'yı başlat
            if (string.IsNullOrEmpty(_settings.PlayfabTitleId))
            {
                Debug.LogError($"[{ModuleName}] Playfab Title ID ayarlanmamış!");
                return;
            }

            // Playfab SDK başlatma kodu buraya gelecek
            // PlayFabSettings.TitleId = _settings.PlayfabTitleId;
            
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

        // Playfab API için yardımcı metodlar buraya eklenecek
        // Örnek: Kullanıcı girişi, veri kaydetme, liderlik tablosu vb.
    }
} 