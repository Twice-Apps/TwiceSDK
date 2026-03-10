using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TwiceSDK.Core;

namespace TwiceSDK.Modules.IAP
{
    /// <summary>
    /// In-App Purchase entegrasyonu için modül
    /// </summary>
    public class IAPModule : ISDKModule
    {
        private bool _isEnabled = false;
        private TwiceSDKSettings _settings;
        private bool _isInitialized = false;

        // Ürün tanımları
        private Dictionary<string, IAPProduct> _products = new Dictionary<string, IAPProduct>();

        /// <summary>
        /// Modülün adı
        /// </summary>
        public string ModuleName => "In-App Purchase";

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
            
            // Unity IAP SDK'yı başlat
            InitializeIAP();
            
            Enable();
        }

        /// <summary>
        /// IAP sistemini başlat
        /// </summary>
        private void InitializeIAP()
        {
            // Unity IAP'yi başlatma kodu buraya gelecek
            // Example: UnityPurchasing.Initialize(this, products);
            
            _isInitialized = true;
            Debug.Log($"[{ModuleName}] IAP sistemi başlatıldı.");
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

        /// <summary>
        /// Ürün satın alma işlemini başlatır
        /// </summary>
        public void PurchaseProduct(string productId)
        {
            if (!IsEnabled || !_isInitialized)
            {
                Debug.LogWarning($"[{ModuleName}] IAP sistemi başlatılmamış veya devre dışı!");
                return;
            }

            if (!_products.ContainsKey(productId))
            {
                Debug.LogError($"[{ModuleName}] Ürün bulunamadı: {productId}");
                return;
            }

            // Unity IAP ile satın alma işlemi başlatma kodu
            // Example: UnityPurchasing.Purchase(productId);
            Debug.Log($"[{ModuleName}] Ürün satın alma başlatıldı: {productId}");
        }

        /// <summary>
        /// Ürün tanımını ekler
        /// </summary>
        public void AddProduct(IAPProduct product)
        {
            if (_products.ContainsKey(product.ProductId))
            {
                Debug.LogWarning($"[{ModuleName}] Bu ürün zaten tanımlı: {product.ProductId}");
                return;
            }

            _products.Add(product.ProductId, product);
            Debug.Log($"[{ModuleName}] Ürün eklendi: {product.ProductId}");
        }

        /// <summary>
        /// Ürün listesini döndürür
        /// </summary>
        public Dictionary<string, IAPProduct> GetProducts()
        {
            return _products;
        }

        /// <summary>
        /// Belirtilen ürünü döndürür
        /// </summary>
        public IAPProduct GetProduct(string productId)
        {
            if (_products.TryGetValue(productId, out IAPProduct product))
            {
                return product;
            }
            return null;
        }
    }

    /// <summary>
    /// IAP Ürün tanımı
    /// </summary>
    [Serializable]
    public class IAPProduct
    {
        public string ProductId;
        public string ProductType; // Consumable, NonConsumable, Subscription
        public string Title;
        public string Description;
        public string PriceString;
        public decimal Price;
        public string Currency;

        public IAPProduct(string productId, string productType, string title, string description)
        {
            ProductId = productId;
            ProductType = productType;
            Title = title;
            Description = description;
        }
    }
} 