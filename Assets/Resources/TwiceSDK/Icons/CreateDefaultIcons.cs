using UnityEngine;
using UnityEditor;
using System.IO;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// SDK için varsayılan ikonları oluşturur
    /// </summary>
    public static class CreateDefaultIcons
    {
        [MenuItem("TwiceSDK/İkonları Oluştur", false, 100)]
        public static void CreateIcons()
        {
            string iconPath = "Assets/Resources/TwiceSDK/Icons";
            
            // Klasörleri oluştur
            if (!Directory.Exists("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
                
            if (!Directory.Exists("Assets/Resources/TwiceSDK"))
                AssetDatabase.CreateFolder("Assets/Resources", "TwiceSDK");
                
            if (!Directory.Exists(iconPath))
                AssetDatabase.CreateFolder("Assets/Resources/TwiceSDK", "Icons");
            
            // Logo oluştur
            CreateLogo(iconPath);
            
            // Modül ikonları oluştur
            CreateModuleIcon(iconPath, "playfab", new Color(0.1f, 0.4f, 0.8f));
            CreateModuleIcon(iconPath, "gameanalytics", new Color(0.2f, 0.6f, 0.1f));
            CreateModuleIcon(iconPath, "iap", new Color(0.8f, 0.5f, 0.1f));
            CreateModuleIcon(iconPath, "crashlytics", new Color(0.8f, 0.2f, 0.2f));
            CreateModuleIcon(iconPath, "ads", new Color(0.9f, 0.3f, 0.7f));
            CreateModuleIcon(iconPath, "custom", new Color(0.5f, 0.5f, 0.5f));
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.Log("TwiceSDK ikonları oluşturuldu.");
        }
        
        private static void CreateLogo(string path)
        {
            int width = 256;
            int height = 64;
            
            Texture2D texture = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];
            
            // Arka plan rengini ayarla - koyu mavi
            Color bgColor = new Color(0.1f, 0.1f, 0.2f);
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = bgColor;
                }
            }
            
            // Logo yazısını ekle
            Color textColor = new Color(0.9f, 0.9f, 0.9f);
            
            // "Twice" kelimesi için basit bir yazı
            DrawText(pixels, width, height, "Twice", textColor, 20, 22, 2);
            
            // "SDK" kelimesi için basit bir yazı
            DrawText(pixels, width, height, "SDK", new Color(0.4f, 0.7f, 1f), 140, 22, 3);
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            SaveTextureToFile(texture, Path.Combine(path, "twicesdk_logo.png"));
        }
        
        private static void DrawText(Color[] pixels, int width, int height, string text, Color color, int startX, int startY, int thickness)
        {
            // Çok basit piksel tabanlı yazı
            foreach (char c in text)
            {
                switch (c)
                {
                    case 'T':
                        for (int x = 0; x < 10; x++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX + x, startY + t, color);
                                
                        for (int y = 0; y < 20; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX + 5, startY + y, color);
                        break;
                        
                    case 'w':
                        for (int y = 0; y < 10; y++)
                        {
                            for (int t = 0; t < thickness; t++)
                            {
                                SetPixelSafe(pixels, width, height, startX, startY + y, color);
                                SetPixelSafe(pixels, width, height, startX + 5, startY + y, color);
                                SetPixelSafe(pixels, width, height, startX + 10, startY + y, color);
                            }
                        }
                        
                        for (int t = 0; t < thickness; t++)
                        {
                            SetPixelSafe(pixels, width, height, startX + 1, startY + 10, color);
                            SetPixelSafe(pixels, width, height, startX + 2, startY + 11, color);
                            SetPixelSafe(pixels, width, height, startX + 3, startY + 12, color);
                            SetPixelSafe(pixels, width, height, startX + 4, startY + 11, color);
                            SetPixelSafe(pixels, width, height, startX + 5, startY + 10, color);
                            SetPixelSafe(pixels, width, height, startX + 6, startY + 11, color);
                            SetPixelSafe(pixels, width, height, startX + 7, startY + 12, color);
                            SetPixelSafe(pixels, width, height, startX + 8, startY + 11, color);
                            SetPixelSafe(pixels, width, height, startX + 9, startY + 10, color);
                        }
                        break;
                        
                    case 'i':
                        for (int y = 0; y < 15; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX, startY + y, color);
                        break;
                        
                    case 'c':
                        for (int y = 5; y < 15; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX, startY + y, color);
                                
                        for (int x = 0; x < 8; x++)
                        {
                            for (int t = 0; t < thickness; t++)
                            {
                                SetPixelSafe(pixels, width, height, startX + x, startY + 5, color);
                                SetPixelSafe(pixels, width, height, startX + x, startY + 15, color);
                            }
                        }
                        break;
                        
                    case 'e':
                        for (int y = 5; y < 15; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX, startY + y, color);
                                
                        for (int x = 0; x < 8; x++)
                        {
                            for (int t = 0; t < thickness; t++)
                            {
                                SetPixelSafe(pixels, width, height, startX + x, startY + 5, color);
                                SetPixelSafe(pixels, width, height, startX + x, startY + 10, color);
                                SetPixelSafe(pixels, width, height, startX + x, startY + 15, color);
                            }
                        }
                        break;
                        
                    case 'S':
                        for (int x = 0; x < 10; x++)
                        {
                            for (int t = 0; t < thickness; t++)
                            {
                                SetPixelSafe(pixels, width, height, startX + x, startY, color);
                                SetPixelSafe(pixels, width, height, startX + x, startY + 10, color);
                                SetPixelSafe(pixels, width, height, startX + x, startY + 20, color);
                            }
                        }
                        
                        for (int y = 0; y < 10; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX, startY + y, color);
                                
                        for (int y = 10; y < 20; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX + 9, startY + y, color);
                        break;
                        
                    case 'D':
                        for (int y = 0; y < 20; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX, startY + y, color);
                                
                        for (int x = 0; x < 10; x++)
                        {
                            for (int t = 0; t < thickness; t++)
                            {
                                SetPixelSafe(pixels, width, height, startX + x, startY, color);
                                SetPixelSafe(pixels, width, height, startX + x, startY + 20, color);
                            }
                        }
                        
                        for (int y = 0; y < 20; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX + 9, startY + y, color);
                        break;
                        
                    case 'K':
                        for (int y = 0; y < 20; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX, startY + y, color);
                                
                        for (int y = 0; y < 10; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX + 9 - y, startY + 10 + y, color);
                                
                        for (int y = 0; y < 10; y++)
                            for (int t = 0; t < thickness; t++)
                                SetPixelSafe(pixels, width, height, startX + 9 - y, startY + 10 - y, color);
                        break;
                }
                
                startX += 15;
            }
        }
        
        private static void SetPixelSafe(Color[] pixels, int width, int height, int x, int y, Color color)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                pixels[y * width + x] = color;
            }
        }
        
        private static void CreateModuleIcon(string path, string name, Color color)
        {
            int size = 32;
            
            Texture2D texture = new Texture2D(size, size);
            Color[] pixels = new Color[size * size];
            
            // Arka plan rengini ayarla
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Kenarları yumuşat
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2, size / 2));
                    float alpha = Mathf.Clamp01(1 - (distance / (size / 2)));
                    
                    pixels[y * size + x] = new Color(color.r, color.g, color.b, alpha);
                }
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            SaveTextureToFile(texture, Path.Combine(path, name + ".png"));
        }
        
        private static void SaveTextureToFile(Texture2D texture, string filePath)
        {
            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(filePath, bytes);
            
            // Texture2D olarak import etmek için meta bilgileri güncelle
            AssetDatabase.ImportAsset(filePath);
            TextureImporter importer = AssetImporter.GetAtPath(filePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                AssetDatabase.ImportAsset(filePath);
            }
        }
    }
} 