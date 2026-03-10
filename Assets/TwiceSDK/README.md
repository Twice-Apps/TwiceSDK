# TwiceSDK

## Genel Bakış

TwiceSDK, Unity projeleri için hazırlanmış modüler ve genişletilebilir bir SDK paketidir. Çeşitli popüler servisleri tek bir SDK altında birleştirir ve projenizde kullanımı kolaylaştırır.

## Özellikler

- **Modüler Yapı**: İhtiyacınız olan servisleri seçip kullanabilirsiniz
- **Kolay Entegrasyon**: Tek bir sınıf üzerinden tüm servislere erişim
- **Genişletilebilirlik**: Yeni modüller ekleyerek SDK'yı genişletebilirsiniz
- **Yapılandırılabilir**: Unity Editor üzerinden kolay yapılandırma

## Desteklenen Modüller

- **Playfab**: Bulut tabanlı oyun back-end servisi
- **GameAnalytics**: Oyun analitik servisi
- **In-App Purchase**: Uygulama içi satın alma sistemi
- **Firebase Crashlytics**: Hata raporlama servisi

## Kurulum

1. TwiceSDK paketini Unity projenize import edin
2. Unity Editor menüsünden "TwiceSDK > Ayarlar" seçeneğine tıklayın
3. Kullanmak istediğiniz modülleri etkinleştirin ve gerekli ayarları yapın
4. SDK'yı oyununuza entegre edin

## Kullanım

### SDK'yı Başlatma

SDK, `Resources` klasöründeki `TwiceSDKSettings` ayarlarına göre otomatik olarak başlatılabilir veya manuel olarak başlatılabilir:

```csharp
// Manuel başlatma:
TwiceSDK.Core.TwiceSDK.Instance.Initialize();
```

### Modüllere Erişim

```csharp
// Playfab modülüne erişim
PlayfabModule playfab = TwiceSDK.Core.TwiceSDK.Instance.GetModule<PlayfabModule>();

// GameAnalytics modülüne erişim
GameAnalyticsModule analytics = TwiceSDK.Core.TwiceSDK.Instance.GetModule<GameAnalyticsModule>();

// IAP modülüne erişim
IAPModule iap = TwiceSDK.Core.TwiceSDK.Instance.GetModule<IAPModule>();

// Crashlytics modülüne erişim
CrashlyticsModule crashlytics = TwiceSDK.Core.TwiceSDK.Instance.GetModule<CrashlyticsModule>();
```

### Modülleri Etkinleştirme/Devre Dışı Bırakma

```csharp
// Modülü etkinleştirme
TwiceSDK.Core.TwiceSDK.Instance.EnableModule<PlayfabModule>();

// Modülü devre dışı bırakma
TwiceSDK.Core.TwiceSDK.Instance.DisableModule<PlayfabModule>();
```

## Bağımlılıklar

TwiceSDK'yı kullanmak için aşağıdaki paketleri projenize eklemeniz gerekebilir:

- PlayFab SDK
- GameAnalytics SDK
- Unity IAP (Unity Package Manager üzerinden)
- Firebase SDK (Firebase Crashlytics için)

## SDK'yı Genişletme

Kendi modülünüzü eklemek için:

1. `ISDKModule` arayüzünü uygulayan yeni bir sınıf oluşturun
2. Modül işlevselliğini uygulayın
3. SDK'ya modülünüzü kaydedin:

```csharp
TwiceSDK.Core.TwiceSDK.Instance.RegisterModule<YourCustomModule>();
```

## Lisans

[MIT Lisansı](LICENSE)

## İletişim

Herhangi bir soru veya geri bildirim için iletişime geçebilirsiniz:
[iletisim@twicesdk.com](mailto:iletisim@twicesdk.com) 