using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TwiceSDK.Core
{
    /// <summary>
    /// TwiceSDK modülleri için temel arayüz
    /// </summary>
    public interface ISDKModule
    {
        /// <summary>
        /// Modülün adı
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// Modülün aktif olup olmadığını belirtir
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Modülü başlatma metodu
        /// </summary>
        void Initialize(TwiceSDKSettings settings);

        /// <summary>
        /// Modülü etkinleştirme
        /// </summary>
        void Enable();

        /// <summary>
        /// Modülü devre dışı bırakma
        /// </summary>
        void Disable();
    }
} 