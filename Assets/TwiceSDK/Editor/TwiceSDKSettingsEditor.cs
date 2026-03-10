using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TwiceSDK.Core;
using System.Linq;
using System.IO;
using System.Reflection;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// TwiceSDK ayarları için özel editör penceresi
    /// </summary>
    [CustomEditor(typeof(TwiceSDKSettings))]
    public class TwiceSDKSettingsEditor : UnityEditor.Editor
    {
        // 3rdParty asset'lerin konumları
        private const string PLAYFAB_EXTENSION_PATH = "Assets/TwiceSDK/3rdParty/PlayFabEditorExtensions.unitypackage";
        private const string GAMEANALYTICS_SDK_PATH = "Assets/TwiceSDK/3rdParty/GA_SDK_UNITY.unitypackage";
        private const string APPLOVIN_MAX_PATH = "Assets/TwiceSDK/3rdParty/AppLovin-MAX.unitypackage";
        private const string TWICE_PLAYFAB_SCRIPTS_PATH = "Assets/TwiceSDK/3rdParty/TwicePlayfab.unitypackage";
        private const string TWICE_GAMEANALYTICS_SCRIPTS_PATH = "Assets/TwiceSDK/3rdParty/TwiceGameAnalytics.unitypackage";
        private const string TWICE_IAP_SCRIPTS_PATH = "Assets/TwiceSDK/3rdParty/TwiceIAP.unitypackage";
        private const string TWICE_CRASHLYTICS_SCRIPTS_PATH = "Assets/TwiceSDK/3rdParty/TwiceCrashlytics.unitypackage";
        private const string TWICE_ADS_SCRIPTS_PATH = "Assets/TwiceSDK/3rdParty/TwiceAds.unitypackage";
        private const string PLAYFAB_NAMESPACE = "PlayFab";
        private const string PLAYFAB_EDITOR_NAMESPACE = "PlayFabEditor";
        private const string APPLOVIN_NAMESPACE = "MaxSdk";
        
        // Stilleri saklamak için statik cache
        private static class Styles
        {
            public static GUIStyle Header;
            public static GUIStyle ModuleBox;
            public static GUIStyle ModuleTitle;
            public static GUIStyle ModuleDescription;
            public static GUIStyle ToggleStyle;
            public static GUIStyle ImportButton;
            public static GUIStyle ImportedButton;
            
            public static Color ImportColor = new Color(0.2f, 0.4f, 0.8f);
            public static Color ImportedColor = new Color(0.35f, 0.75f, 0.35f);
            public static Color DisabledColor = new Color(0.6f, 0.6f, 0.6f);
            
            public static Texture2D ImportTexture;
            public static Texture2D ImportedTexture;
            public static Texture2D TickIcon;
            
            static Styles()
            {
                Header = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    margin = new RectOffset(0, 0, 10, 10)
                };
                
                ModuleBox = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 10, 10),
                    margin = new RectOffset(0, 0, 8, 8)
                };
                
                ModuleTitle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 0, 2)
                };
                
                ModuleDescription = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 10,
                    wordWrap = true,
                    margin = new RectOffset(0, 0, 0, 5)
                };
                
                ToggleStyle = new GUIStyle(EditorStyles.toggle)
                {
                    margin = new RectOffset(5, 5, 5, 5)
                };
                
                // Import buton stili
                ImportButton = new GUIStyle(GUI.skin.button)
                {
                    fixedWidth = 100,
                    fixedHeight = 30,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                
                // Imported buton stili
                ImportedButton = new GUIStyle(GUI.skin.button)
                {
                    fixedWidth = 100,
                    fixedHeight = 30,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = ImportedColor }
                };
                
                // Buton textureleri
                CreateButtonTextures();
                
                // Tick (onay) ikonu
                TickIcon = EditorGUIUtility.FindTexture("d_FilterSelectedOnly@2x");
            }
            
            // Import butonları için texture oluştur
            private static void CreateButtonTextures()
            {
                ImportTexture = new Texture2D(100, 30);
                ImportedTexture = new Texture2D(100, 30);
                
                // Import buton - mavi
                Color importColor = ImportColor;
                for (int y = 0; y < 30; y++)
                {
                    for (int x = 0; x < 100; x++)
                    {
                        ImportTexture.SetPixel(x, y, importColor);
                    }
                }
                ImportTexture.Apply();
                
                // Imported buton - yeşil
                Color importedColor = ImportedColor;
                for (int y = 0; y < 30; y++)
                {
                    for (int x = 0; x < 100; x++)
                    {
                        ImportedTexture.SetPixel(x, y, importedColor);
                    }
                }
                ImportedTexture.Apply();
            }
        }
        
        private SerializedProperty _autoInitialize;
        private SerializedProperty _logLevel;
        
        private SerializedProperty _playfabConfig;
        private SerializedProperty _gameAnalyticsConfig;
        private SerializedProperty _iapConfig;
        private SerializedProperty _crashlyticsConfig;
        private SerializedProperty _adsConfig;
        private SerializedProperty _customModules;
        
        private TwiceSDKSettings _settings;
        private bool _showGeneralSettings = true;
        private bool _showCustomModules = false;
        private List<Texture2D> _moduleIcons = new List<Texture2D>();
        
        // Debug panelindeki modül durumu etiketleri için cache
        private Dictionary<Core.ModuleType, bool> _moduleImportStatusCache = new Dictionary<Core.ModuleType, bool>();
        private bool _isCacheValid = false;
        
        // Belirli aralıklarla GUI'yı yeniler
        private float _lastUpdateTime = 0f;
        
        private void OnEnable()
        {
            _settings = (TwiceSDKSettings)target;
            
            // Genel ayarlar
            _autoInitialize = serializedObject.FindProperty("_autoInitialize");
            _logLevel = serializedObject.FindProperty("_logLevel");
            
            // Modül ayarları
            _playfabConfig = serializedObject.FindProperty("_playfabConfig");
            _gameAnalyticsConfig = serializedObject.FindProperty("_gameAnalyticsConfig");
            _iapConfig = serializedObject.FindProperty("_iapConfig");
            _crashlyticsConfig = serializedObject.FindProperty("_crashlyticsConfig");
            _adsConfig = serializedObject.FindProperty("_adsConfig");
            _customModules = serializedObject.FindProperty("_customModules");
            
            // İkonları yükle
            LoadIcons();
            
            // Modül durumlarının cachesini hazırla
            RefreshModuleStatusCache();
            
            // Editor update olayına abone ol
            EditorApplication.update += OnEditorUpdate;
        }
        
        private void OnDisable()
        {
            // Editor update olayından çık
            EditorApplication.update -= OnEditorUpdate;
        }
        
        // Belirli aralıklarla GUI'yı yeniler
        private void OnEditorUpdate()
        {
            // 5 saniyede bir cache'i yenile
            if (EditorApplication.timeSinceStartup > _lastUpdateTime + 5.0f)
            {
                _lastUpdateTime = (float)EditorApplication.timeSinceStartup;
                RefreshModuleStatusCache();
                Repaint();
            }
        }
        
        private void LoadIcons()
        {
            // Modül ikonları
            _moduleIcons.Clear();
            _moduleIcons.Add(Resources.Load<Texture2D>("TwiceSDK/Icons/playfab"));
            _moduleIcons.Add(Resources.Load<Texture2D>("TwiceSDK/Icons/gameanalytics"));
            _moduleIcons.Add(Resources.Load<Texture2D>("TwiceSDK/Icons/iap"));
            _moduleIcons.Add(Resources.Load<Texture2D>("TwiceSDK/Icons/crashlytics"));
            _moduleIcons.Add(Resources.Load<Texture2D>("TwiceSDK/Icons/ads"));
            _moduleIcons.Add(Resources.Load<Texture2D>("TwiceSDK/Icons/custom"));
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            // TwiceSDK Logo
            var logo = Resources.Load<Texture2D>("TwiceSDK/Icons/twicesdk_logo");
            if (logo != null)
            {
                GUILayout.Space(10);
                var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(64));
                GUI.DrawTexture(rect, logo, ScaleMode.ScaleToFit);
                GUILayout.Space(10);
            }
            
            // Başlık
            GUILayout.Label("TwiceSDK Ayarları", Styles.Header);
            
            EditorGUILayout.Space(10);
            
            // Genel Ayarlar
            EditorGUILayout.LabelField("Genel Ayarlar", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_autoInitialize, new GUIContent("Otomatik Başlat", "SDK oyun başladığında otomatik olarak başlatılsın mı?"));
            EditorGUILayout.PropertyField(_logLevel, new GUIContent("Log Seviyesi", "SDK log mesajlarının detay seviyesi"));

            // Yol göster
            EditorGUILayout.HelpBox("SDK ayarları otomatik olarak Resources/TwiceSDKSettings.asset dosyasından yüklenir.", MessageType.Info);

            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            GUILayout.Label("Modüller", Styles.Header);
            EditorGUILayout.Space(5);
            
            // Modül kartlarını çiz
            DrawModuleCard(Core.ModuleType.Playfab, _playfabConfig, "PlayFab");
            DrawModuleCard(Core.ModuleType.GameAnalytics, _gameAnalyticsConfig, "GameAnalytics");
            DrawModuleCard(Core.ModuleType.IAP, _iapConfig, "In-App Purchase");
            DrawModuleCard(Core.ModuleType.Crashlytics, _crashlyticsConfig, "Firebase Crashlytics");
            DrawModuleCard(Core.ModuleType.Ads, _adsConfig, "AppLovin MAX");
            
            // Özel modüller
            if (_customModules.arraySize > 0)
            {
                EditorGUILayout.Space(10);
                _showCustomModules = EditorGUILayout.Foldout(_showCustomModules, "Özel Modüller", true);
                
                if (_showCustomModules)
                {
                    for (int i = 0; i < _customModules.arraySize; i++)
                    {
                        SerializedProperty moduleWrapper = _customModules.GetArrayElementAtIndex(i);
                        SerializedProperty moduleConfig = moduleWrapper.FindPropertyRelative("Config");
                        
                        // TODO: Özel modüller için uygun arayüz gösterimi
                    }
                }
            }
            
            EditorGUILayout.Space(10);
            
            // Modül durumlarını yenile butonu - sayfanın en altında
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            if (GUILayout.Button("Tüm Modül Durumlarını Yenile", GUILayout.Height(30)))
            {
                RefreshImportedState();
                RefreshModuleStatusCache();
            }
            
            EditorGUILayout.EndVertical();
            
            serializedObject.ApplyModifiedProperties();
        }
        
        /// <summary>
        /// Modül kartı görseli çizer
        /// </summary>
        private void DrawModuleCard(Core.ModuleType moduleType, SerializedProperty moduleConfig, string title)
        {
            if (moduleConfig == null) return;
            
            Rect moduleRect = EditorGUILayout.BeginVertical(Styles.ModuleBox);
            
            // Modül başlığı ve durum bilgisi
            EditorGUILayout.BeginHorizontal();
            
            // İkon
            var icon = GetModuleIcon(moduleType);
            if (icon != null)
            {
                GUILayout.Label(new GUIContent(icon), GUILayout.Width(32), GUILayout.Height(32));
                GUILayout.Space(5);
            }
            
            EditorGUILayout.BeginVertical();
            
            // Başlık
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(title, Styles.ModuleTitle);
            
            EditorGUILayout.EndHorizontal();
            
            // Modül durumu - basitleştirilmiş
            bool isImported = GetModuleImportedStatus(moduleType);
            GUIStyle statusStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = isImported ? Color.green : Color.red },
                fontStyle = FontStyle.Bold
            };
            GUILayout.Label(isImported ? "Yüklü" : "Yüklü Değil", statusStyle);
            
            EditorGUILayout.EndVertical();
            
            // Import Butonu
            GUILayout.FlexibleSpace();
            DrawImportButton(moduleType);
            
            EditorGUILayout.EndHorizontal();
            
            // Twice Scriptleri Import Butonu - Sadece modül yüklüyse göster
            if (isImported)
            {
                EditorGUILayout.Space(5);
                
                string buttonText = "";
                string scriptPath = "";
                
                switch (moduleType)
                {
                    case Core.ModuleType.Playfab:
                        buttonText = "Twice PlayFab Scriptlerini Importla";
                        scriptPath = TWICE_PLAYFAB_SCRIPTS_PATH;
                        break;
                    case Core.ModuleType.GameAnalytics:
                        buttonText = "Twice GameAnalytics Scriptlerini Importla";
                        scriptPath = TWICE_GAMEANALYTICS_SCRIPTS_PATH;
                        break;
                    case Core.ModuleType.IAP:
                        buttonText = "Twice IAP Scriptlerini Importla";
                        scriptPath = TWICE_IAP_SCRIPTS_PATH;
                        break;
                    case Core.ModuleType.Crashlytics:
                        buttonText = "Twice Crashlytics Scriptlerini Importla";
                        scriptPath = TWICE_CRASHLYTICS_SCRIPTS_PATH;
                        break;
                    case Core.ModuleType.Ads:
                        buttonText = "Twice Ads Scriptlerini Importla";
                        scriptPath = TWICE_ADS_SCRIPTS_PATH;
                        break;
                }
                
                if (!string.IsNullOrEmpty(buttonText) && !string.IsNullOrEmpty(scriptPath))
                {
                    // Twice scripti var mı kontrol et
                    bool scriptExists = File.Exists(scriptPath);
                    
                    // Düğmeyi devre dışı bırak veya bilgi ver
                    using (new EditorGUI.DisabledGroupScope(!scriptExists))
                    {
                        if (GUILayout.Button(buttonText, GUILayout.Height(24)))
                        {
                            ImportTwiceScripts(moduleType, scriptPath);
                        }
                    }
                    
                    // Script bulunamadıysa bilgi ver
                    if (!scriptExists)
                    {
                        EditorGUILayout.HelpBox($"Script paketi bulunamadı: {scriptPath}", MessageType.Warning);
                    }
                }
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }
        
        /// <summary>
        /// Import butonunu çizer ve duruma göre görünümünü değiştirir
        /// </summary>
        private void DrawImportButton(Core.ModuleType moduleType)
        {
            bool isImported = GetModuleImportedStatus(moduleType);
            
            // Buton stili
            GUIStyle buttonStyle = isImported ? Styles.ImportedButton : Styles.ImportButton;
            string buttonText = isImported ? "  Imported" : "Import";
            
            EditorGUI.BeginDisabledGroup(isImported);
            
            Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(buttonText), buttonStyle, GUILayout.Width(100), GUILayout.Height(30));
            
            // Buton arka planı
            GUI.DrawTexture(buttonRect, isImported ? Styles.ImportedTexture : Styles.ImportTexture);
            
            // Onay işareti (tick)
            if (isImported && Styles.TickIcon != null)
            {
                Rect tickRect = new Rect(buttonRect.x + 8, buttonRect.y + 7, 16, 16);
                GUI.DrawTexture(tickRect, Styles.TickIcon);
            }
            
            // Buton metni
            if (GUI.Button(buttonRect, buttonText, buttonStyle))
            {
                ImportModule(moduleType);
            }
            
            EditorGUI.EndDisabledGroup();
        }
        
        /// <summary>
        /// Assembly yüklenmiş mi kontrol eder
        /// </summary>
        private bool IsAssemblyLoaded(string assemblyName)
        {
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (assembly.GetName().Name.Contains(assemblyName))
                    return true;
                
                // Tip bazlı kontrol (GameAnalytics özel kontrolü)
                if (assemblyName == "GameAnalyticsSDK")
                {
                    try
                    {
                        var types = assembly.GetTypes();
                        foreach (var type in types)
                        {
                            if (type.FullName != null && type.FullName.Contains("GameAnalytics"))
                                return true;
                        }
                    }
                    catch (System.Exception)
                    {
                        // Hata durumunda devam et
                    }
                }
            }
            return false;
        }
        
        /// <summary>
        /// Modülün import edilip edilmediğini kontrol eder
        /// </summary>
        private bool IsModuleImported(Core.ModuleType moduleType)
        {
            // Her frame'de AssetDatabase.Refresh çağırmıyoruz
            // sadece import sırasında RefreshImportedState metodu içinde çağrılacak
            
            switch (moduleType)
            {
                case Core.ModuleType.Playfab:
                    return IsPlayFabInstalled();
                    
                case Core.ModuleType.GameAnalytics:
                    // En doğrudan GameAnalytics varlık kontrolü - klasör temelli
                    bool gaExists = Directory.Exists("Assets/GameAnalytics");
                    // Debug.Log deaktive edildi
                    // Debug.Log($"GameAnalytics klasörü kontrol: {gaExists}");
                    return gaExists;
                    
                case Core.ModuleType.IAP:
                    return IsAssemblyLoaded("UnityEngine.Purchasing");
                    
                case Core.ModuleType.Crashlytics:
                    return IsAssemblyLoaded("Firebase.Crashlytics");
                    
                case Core.ModuleType.Ads:
                    return IsAssemblyLoaded(APPLOVIN_NAMESPACE) || Directory.Exists("Assets/MaxSdk");
                    
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// İmport işlemi sonrası UI'ı günceller
        /// </summary>
        private void RefreshImportedState()
        {
            // Cache durumunu geçersiz kıl
            _isCacheValid = false;
            
            // AssetDatabase'i yenile - sadece bir kez çağırıyoruz
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            
            // Cache'i yenile
            RefreshModuleStatusCache();
            
            // Tüm Inspectorları yeniden çiz
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            
            // Editörü yenile
            Repaint();
        }
        
        /// <summary>
        /// PlayFab SDK'nın yüklü olup olmadığını kontrol eder
        /// </summary>
        private bool IsPlayFabInstalled()
        {
            return IsAssemblyLoaded(PLAYFAB_NAMESPACE);
        }
        
        /// <summary>
        /// PlayFab Editor Extension'ın yüklü olup olmadığını kontrol eder
        /// </summary>
        private bool IsPlayFabEditorExtensionInstalled()
        {
            return IsAssemblyLoaded(PLAYFAB_EDITOR_NAMESPACE);
        }
        
        /// <summary>
        /// Seçilen modülü import eder
        /// </summary>
        private void ImportModule(Core.ModuleType moduleType)
        {
            switch (moduleType)
            {
                case Core.ModuleType.Playfab:
                    ImportPlayFab();
                    break;
                case Core.ModuleType.GameAnalytics:
                    ImportGameAnalytics();
                    break;
                case Core.ModuleType.IAP:
                    ImportIAP();
                    break;
                case Core.ModuleType.Crashlytics:
                    ImportCrashlytics();
                    break;
                case Core.ModuleType.Ads:
                    ImportAds();
                    break;
            }
        }
        
        /// <summary>
        /// PlayFab SDK ve Editor Extension'ı import eder
        /// </summary>
        private void ImportPlayFab()
        {
            // PlayFab Editor Extensions package varsa import et
            if (File.Exists(PLAYFAB_EXTENSION_PATH))
            {
                bool result = EditorUtility.DisplayDialog(
                    "PlayFab Import", 
                    "PlayFab SDK ve Editor Extension import edilecek. Devam etmek istiyor musunuz?", 
                    "Evet", "Hayır");
                
                if (result)
                {
                    // İmport öncesi AssetDatabase'i durdur
                    AssetDatabase.StartAssetEditing();
                    
                    try
                    {
                        AssetDatabase.ImportPackage(PLAYFAB_EXTENSION_PATH, true);
                        Debug.Log("PlayFab Editor Extensions import ediliyor...");
                    }
                    finally
                    {
                        // İşlem ne olursa olsun AssetDatabase'i tekrar başlat
                        AssetDatabase.StopAssetEditing();
                        
                        // Cache'i geçersiz kıl
                        _isCacheValid = false;
                        
                        // UI'ı yenile (tek seferlik)
                        EditorApplication.delayCall += () => {
                            RefreshModuleStatusCache();
                            Repaint();
                        };
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "PlayFab Import Hatası", 
                    "PlayFab Editor Extensions package bulunamadı.\nLütfen 3rdParty klasöründe PlayFabEditorExtensions.unitypackage olduğundan emin olun.", 
                    "Tamam");
            }
        }
        
        /// <summary>
        /// GameAnalytics SDK'yı import eder
        /// </summary>
        private void ImportGameAnalytics()
        {
            // GameAnalytics SDK package varsa import et
            if (File.Exists(GAMEANALYTICS_SDK_PATH))
            {
                bool result = EditorUtility.DisplayDialog(
                    "GameAnalytics Import", 
                    "GameAnalytics SDK import edilecek. Devam etmek istiyor musunuz?", 
                    "Evet", "Hayır");
                
                if (result)
                {
                    // İmport öncesi AssetDatabase'i durdur
                    AssetDatabase.StartAssetEditing();
                    
                    try
                    {
                        AssetDatabase.ImportPackage(GAMEANALYTICS_SDK_PATH, true);
                        Debug.Log("GameAnalytics SDK import ediliyor...");
                    }
                    finally
                    {
                        // İşlem ne olursa olsun AssetDatabase'i tekrar başlat
                        AssetDatabase.StopAssetEditing();
                        
                        // Cache'i geçersiz kıl
                        _isCacheValid = false;
                        
                        // UI'ı yenile (tek seferlik)
                        EditorApplication.delayCall += () => {
                            RefreshModuleStatusCache();
                            Repaint();
                        };
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "GameAnalytics Import Hatası", 
                    "GameAnalytics SDK package bulunamadı.\nLütfen 3rdParty klasöründe GA_SDK_UNITY.unitypackage olduğundan emin olun.", 
                    "Tamam");
            }
        }
        
        /// <summary>
        /// Unity IAP'yi import eder
        /// </summary>
        private void ImportIAP()
        {
            bool result = EditorUtility.DisplayDialog(
                "Unity IAP Import", 
                "Unity IAP modülünü Package Manager üzerinden indirmek istiyor musunuz?", 
                "Evet", "Hayır");
            
            if (result)
            {
                EditorApplication.ExecuteMenuItem("Window/Package Manager");
                EditorUtility.DisplayDialog(
                    "Unity IAP Import", 
                    "Package Manager açıldı. Lütfen 'Packages: Unity Registry' listesinden 'In App Purchasing' paketini bulun ve 'Install' butonuna tıklayın.", 
                    "Tamam");
                
                // Cache'i geçersiz kıl
                _isCacheValid = false;
                
                // 5 saniye sonra durumu kontrol et ve yenile
                EditorApplication.delayCall += () => {
                    // 5 saniye bekle ve sonra yenile
                    float delayTime = (float)EditorApplication.timeSinceStartup;
                    EditorApplication.update += function;
                    
                    void function()
                    {
                        if ((float)EditorApplication.timeSinceStartup - delayTime >= 5f)
                        {
                            EditorApplication.update -= function;
                            RefreshModuleStatusCache();
                            Repaint();
                        }
                    }
                };
            }
        }
        
        /// <summary>
        /// Firebase Crashlytics'i import eder
        /// </summary>
        private void ImportCrashlytics()
        {
            bool result = EditorUtility.DisplayDialog(
                "Firebase Crashlytics Import", 
                "Firebase Crashlytics kurulumu için Firebase websitesine yönlendirilmek istiyor musunuz?", 
                "Evet", "Hayır");
            
            if (result)
            {
                Application.OpenURL("https://firebase.google.com/docs/unity/setup");
                
                // Cache'i geçersiz kıl
                _isCacheValid = false;
                
                // 1 saniye sonra durumu kontrol et ve yenile
                EditorApplication.delayCall += () => {
                    RefreshModuleStatusCache();
                    Repaint();
                };
            }
        }
        
        /// <summary>
        /// AppLovin MAX'i import eder
        /// </summary>
        private void ImportAds()
        {
            // AppLovin MAX package varsa import et
            if (File.Exists(APPLOVIN_MAX_PATH))
            {
                bool result = EditorUtility.DisplayDialog(
                    "AppLovin MAX Import", 
                    "AppLovin MAX SDK import edilecek. Devam etmek istiyor musunuz?", 
                    "Evet", "Hayır");
                
                if (result)
                {
                    // İmport öncesi AssetDatabase'i durdur
                    AssetDatabase.StartAssetEditing();
                    
                    try
                    {
                        AssetDatabase.ImportPackage(APPLOVIN_MAX_PATH, true);
                        Debug.Log("AppLovin MAX SDK import ediliyor...");
                    }
                    finally
                    {
                        // İşlem ne olursa olsun AssetDatabase'i tekrar başlat
                        AssetDatabase.StopAssetEditing();
                        
                        // Cache'i geçersiz kıl
                        _isCacheValid = false;
                        
                        // UI'ı yenile (tek seferlik)
                        EditorApplication.delayCall += () => {
                            RefreshModuleStatusCache();
                            Repaint();
                        };
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "AppLovin MAX Import Hatası", 
                    "AppLovin MAX SDK package bulunamadı.\nLütfen 3rdParty klasöründe AppLovin-MAX.unitypackage olduğundan emin olun.", 
                    "Tamam");
            }
        }
        
        /// <summary>
        /// Modül ikonunu döndürür
        /// </summary>
        private Texture2D GetModuleIcon(Core.ModuleType moduleType)
        {
            int index = (int)moduleType;
            if (index >= 0 && index < _moduleIcons.Count && _moduleIcons[index] != null)
                return _moduleIcons[index];
                
            return null;
        }
        
        /// <summary>
        /// TwiceSDK ayarlar dosyasını oluşturur veya açar
        /// </summary>
        [MenuItem("TwiceSDK/Ayarlar", false, 0)]
        public static void OpenSettings()
        {
            // Simgeler klasörünü hazırla
            PrepareResourcesFolder();
            
            TwiceSDKSettings settings = Resources.Load<TwiceSDKSettings>("TwiceSDKSettings");
            
            if (settings == null)
            {
                // Ayarlar dosyası yoksa oluştur
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }
                
                settings = CreateInstance<TwiceSDKSettings>();
                AssetDatabase.CreateAsset(settings, "Assets/Resources/TwiceSDKSettings.asset");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            
            Selection.activeObject = settings;
        }
        
        /// <summary>
        /// Simgeler için kaynak klasörünü hazırlar
        /// </summary>
        private static void PrepareResourcesFolder()
        {
            // Resources/TwiceSDK/Icons klasörünü oluştur
            string path = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            
            path = "Assets/Resources/TwiceSDK";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "TwiceSDK");
            }
            
            path = "Assets/Resources/TwiceSDK/Icons";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder("Assets/Resources/TwiceSDK", "Icons");
            }
        }
        
        [MenuItem("TwiceSDK/Dokümantasyon", false, 1)]
        public static void OpenDocumentation()
        {
            // Burada dokümantasyon sayfasına yönlendirme yapılabilir
            EditorUtility.DisplayDialog("TwiceSDK Dokümantasyon", 
                "TwiceSDK dokümantasyonu için lütfen /TwiceSDK/README.md dosyasına bakın.", "Tamam");
        }
        
        /// <summary>
        /// GameAnalytics önbelleğini temizler ve durumu sıfırlar
        /// </summary>
        private void ClearGameAnalyticsCache()
        {
            // Editor PlayerPrefs'ten ilgili kayıtları temizle
            if (EditorPrefs.HasKey("TwiceSDK_GA_Imported"))
                EditorPrefs.DeleteKey("TwiceSDK_GA_Imported");
            
            // Assembly cache'i temizle
            System.AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= null;
            
            // Asset veritabanını tamamen yenile
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            
            // Tüm inspectorları ve editör pencerelerini yenile 
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            
            Debug.Log("GameAnalytics cache temizlendi ve durum sıfırlandı.");
            
            // Doğrudan klasör varlığını kontrol et ve logla
            bool gaExists = Directory.Exists("Assets/GameAnalytics");
            Debug.Log($"GameAnalytics klasörü kontrol: {gaExists}");
            
            // Editörü zorla yenile
            EditorUtility.RequestScriptReload();
        }
        
        // Modül durumlarını cacheleyen metod (çok sık çağırmamak için)
        private bool GetModuleImportedStatus(Core.ModuleType moduleType)
        {
            // Eğer cache geçerli değilse veya modül durumu cachede yoksa
            if (!_isCacheValid || !_moduleImportStatusCache.ContainsKey(moduleType))
            {
                _moduleImportStatusCache[moduleType] = IsModuleImported(moduleType);
            }
            
            return _moduleImportStatusCache[moduleType];
        }
        
        // Modül durumu cachesini yenile
        private void RefreshModuleStatusCache()
        {
            _moduleImportStatusCache.Clear();
            _isCacheValid = true;
            
            // Tüm modüllerin durumlarını cache'e ekle
            _moduleImportStatusCache[Core.ModuleType.Playfab] = IsModuleImported(Core.ModuleType.Playfab);
            _moduleImportStatusCache[Core.ModuleType.GameAnalytics] = IsModuleImported(Core.ModuleType.GameAnalytics);
            _moduleImportStatusCache[Core.ModuleType.IAP] = IsModuleImported(Core.ModuleType.IAP);
            _moduleImportStatusCache[Core.ModuleType.Crashlytics] = IsModuleImported(Core.ModuleType.Crashlytics);
            _moduleImportStatusCache[Core.ModuleType.Ads] = IsModuleImported(Core.ModuleType.Ads);
        }
        
        /// <summary>
        /// Belirtilen modül için cache'i temizler ve durumu sıfırlar
        /// </summary>
        private void ClearModuleCache(Core.ModuleType moduleType)
        {
            // Cache'i geçersiz kıl
            _isCacheValid = false;
            
            // Asset veritabanını tamamen yenile
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            
            // Tüm inspectorları ve editör pencerelerini yenile 
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            
            Debug.Log($"{moduleType} cache temizlendi ve durum sıfırlandı.");
            
            // Editörü zorla yenile
            EditorUtility.RequestScriptReload();
        }
        
        /// <summary>
        /// Twice script paketini import eder
        /// </summary>
        private void ImportTwiceScripts(Core.ModuleType moduleType, string scriptPath)
        {
            if (File.Exists(scriptPath))
            {
                string moduleName = moduleType.ToString();
                bool result = EditorUtility.DisplayDialog(
                    $"Twice {moduleName} Scriptleri", 
                    $"Twice {moduleName} scriptlerini import etmek istiyor musunuz?", 
                    "Evet", "Hayır");
                
                if (result)
                {
                    // İmport öncesi AssetDatabase'i durdur
                    AssetDatabase.StartAssetEditing();
                    
                    try
                    {
                        AssetDatabase.ImportPackage(scriptPath, true);
                        Debug.Log($"Twice {moduleName} scriptleri import ediliyor...");
                    }
                    finally
                    {
                        // İşlem ne olursa olsun AssetDatabase'i tekrar başlat
                        AssetDatabase.StopAssetEditing();
                        
                        // UI'ı yenile
                        EditorApplication.delayCall += () => {
                            AssetDatabase.Refresh();
                            Repaint();
                        };
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Import Hatası", 
                    $"Script paketi bulunamadı: {scriptPath}", 
                    "Tamam");
            }
        }
    }
} 