using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// "Toplu yükle" tab: scan the Asset Store download cache or any folder, review
    /// category / version per package, upload the ticked ones one after another.
    /// Already-uploaded versions are detected (by Asset Store id, then slug) and skipped,
    /// so every teammate can run it on their own machine without creating duplicates.
    /// </summary>
    public sealed partial class TpWindow
    {
        sealed class BulkRow
        {
            public TpLocalScan.Item Item;
            public bool On;
            public string Category;
            public string Version;
            public string TargetSlug;   // existing package to add a version to, or the new slug
            public string State;        // Yeni / Yeni sürüm / Zaten var
            public string Result;       // after upload
        }

        List<BulkRow> _bulk = new List<BulkRow>();
        string _bulkRoot = "";
        bool _bulkFromStore;
        string _bulkFilter = "";
        string _bulkSetCat = "";
        Vector2 _bulkScroll;
        bool _bulkRunning;
        TpCancel _bulkCancel;

        // Asset Store account (My Assets)
        List<TpAssetStore.Owned> _owned;
        HashSet<long> _ownedSkip = new HashSet<long>();   // unticked by the user
        bool _ownedShowList;
        bool _ownedIncludeHidden;
        bool _ownedAutoUpload = true;
        bool _ownedBusy;
        TpCancel _ownedCancel;
        Vector2 _ownedScroll;
        HashSet<long> _cachedIds;

        void DrawAccount()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Asset Store hesabım", _h2);
            GUILayout.FlexibleSpace();
            GUILayout.Label(TpAssetStore.LoggedIn ? CloudProjectSettings.userName : "Unity hesabına giriş yapılmamış", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            string un = TpAssetStore.Unsupported;
            if (un != null) { EditorGUILayout.HelpBox(un, MessageType.Warning); EditorGUILayout.EndVertical(); return; }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_ownedBusy && !_bulkRunning && TpAssetStore.LoggedIn;
            if (GUILayout.Button(_owned == null ? "Sahip olduklarımı listele" : "Listeyi yenile", GUILayout.Height(24), GUILayout.Width(180))) Run(LoadOwned());
            GUI.enabled = true;
            if (_owned != null)
            {
                var missing = MissingOwned();
                int onServer = _owned.Count(o => OnServer(o.Id));
                int cached = _owned.Count(o => !OnServer(o.Id) && Cached(o.Id));
                GUILayout.Label(_owned.Count + " asset · " + onServer + " sunucuda · " + cached + " indirilmiş · " + missing.Count + " indirilecek", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (_ownedBusy)
                {
                    if (GUILayout.Button("Durdur", GUILayout.Height(24), GUILayout.Width(80)) && _ownedCancel != null) _ownedCancel.Requested = true;
                }
                else
                {
                    GUI.enabled = missing.Count > 0 && !_bulkRunning;
                    var bg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
                    if (GUILayout.Button("Eksikleri indir (" + missing.Count + ")" + (_ownedAutoUpload ? " ve yükle" : ""), GUILayout.Height(24), GUILayout.Width(200))) Run(DownloadOwned(missing));
                    GUI.backgroundColor = bg;
                    GUI.enabled = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_owned != null)
            {
                EditorGUILayout.BeginHorizontal();
                _ownedAutoUpload = EditorGUILayout.ToggleLeft(new GUIContent("İndirme bitince tara ve sunucuya yükle", "Kapalıysa yalnız indirir; aşağıdaki listeden kontrol edip sen yüklersin."), _ownedAutoUpload, GUILayout.Width(260));
                _ownedIncludeHidden = EditorGUILayout.ToggleLeft("Gizlediklerimi de dahil et", _ownedIncludeHidden, GUILayout.Width(180));
                _ownedShowList = EditorGUILayout.ToggleLeft("Tek tek seç", _ownedShowList, GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();
                if (_ownedShowList)
                {
                    _ownedScroll = EditorGUILayout.BeginScrollView(_ownedScroll, GUILayout.Height(160));
                    foreach (var o in _owned.Where(x => _ownedIncludeHidden || !x.Hidden))
                    {
                        EditorGUILayout.BeginHorizontal();
                        string st = OnServer(o.Id) ? "sunucuda" : Cached(o.Id) ? "indirilmiş" : "indirilecek";
                        GUI.enabled = st == "indirilecek" && !_ownedBusy;
                        bool on = !_ownedSkip.Contains(o.Id);
                        bool n = EditorGUILayout.ToggleLeft(o.Name, on);
                        if (n != on) { if (n) _ownedSkip.Remove(o.Id); else _ownedSkip.Add(o.Id); }
                        GUI.enabled = true;
                        GUILayout.Label(st, EditorStyles.miniLabel, GUILayout.Width(80));
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
            GUILayout.Label("Unity'nin kendi indiricisi kullanılır (My Assets ile aynı); dosyalar Asset Store önbelleğine iner. Sunucuda zaten olanlar indirilmez — ekipten biri yüklediyse sen indirmezsin.", _miniWrap);
            EditorGUILayout.EndVertical();
        }

        bool OnServer(long id)
        {
            string suffix = "/" + id;
            foreach (var p in TpCatalog.Packages)
                if (!string.IsNullOrEmpty(p.assetStoreUrl) && p.assetStoreUrl.EndsWith(suffix, StringComparison.Ordinal)) return true;
            return false;
        }

        bool Cached(long id)
        {
            if (_cachedIds == null) RefreshCachedIds();
            return _cachedIds.Contains(id);
        }

        void RefreshCachedIds()
        {
            _cachedIds = new HashSet<long>();
            foreach (var it in TpLocalScan.Scan(TpLocalScan.AssetStoreCache, true))
            {
                long id;
                if (long.TryParse(it.AssetStoreId, out id)) _cachedIds.Add(id);
            }
        }

        List<TpAssetStore.Owned> MissingOwned()
        {
            return _owned.Where(o => (_ownedIncludeHidden || !o.Hidden) && !_ownedSkip.Contains(o.Id) && !OnServer(o.Id) && !Cached(o.Id)).ToList();
        }

        async Task LoadOwned()
        {
            _ownedBusy = true;
            try
            {
                SetStatus("Asset Store kütüphanesi okunuyor…");
                await TpCatalog.Refresh(false);
                RefreshCachedIds();
                var r = await TpAssetStore.ListOwned((n, total) => SetStatus("Asset Store kütüphanesi okunuyor… " + n + "/" + total));
                if (r.Value != null) { SetStatus(r.Value, true); return; }
                _owned = r.Key.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
                SetStatus(_owned.Count + " asset bulundu, " + MissingOwned().Count + " tanesi indirilecek.");
            }
            finally { _ownedBusy = false; }
        }

        async Task DownloadOwned(List<TpAssetStore.Owned> items)
        {
            if (!EditorUtility.DisplayDialog("Twice Packages — Asset Store",
                    items.Count + " asset Unity'nin indiricisiyle indirilecek (3'er paralel). Boyuta göre uzun sürebilir ve diskte yer kaplar (Asset Store önbelleği: " + TpLocalScan.AssetStoreCache + ").\n\n" +
                    (_ownedAutoUpload ? "Bitince sunucuya yüklenecek." : "Bitince listeye alınacak; yüklemeyi sen başlatırsın."), "Başla", "Vazgeç")) return;
            _ownedBusy = true;
            _ownedCancel = new TpCancel();
            Dictionary<long, string> errors;
            try
            {
                errors = await TpAssetStore.Download(items, 3, pr =>
                {
                    SetStatus("Asset Store indirme: " + (pr.Done + pr.Failed) + "/" + pr.Total + (pr.Failed > 0 ? " (" + pr.Failed + " hata)" : "") +
                              (pr.Active > 0 ? " · " + pr.Current + " " + TpFormat.Size((long)pr.Bytes) + "/" + TpFormat.Size((long)pr.TotalBytes) : ""));
                }, _ownedCancel);
            }
            finally
            {
                _ownedBusy = false;
                _ownedCancel = null;
            }
            RefreshCachedIds();
            foreach (var n in items.Where(i => !_cachedIds.Contains(i.Id) && !errors.ContainsKey(i.Id)).ToList())
                errors[n.Id] = "İndirme bitti ama dosya önbellekte bulunamadı.";
            foreach (var kv in errors)
            {
                var it = items.FirstOrDefault(i => i.Id == kv.Key);
                TpLog.Warn("Asset Store indirilemedi: " + (it != null ? it.Name : kv.Key.ToString()) + " — " + kv.Value);
            }
            if (errors.Count > 0) SetStatus((items.Count - errors.Count) + " indirildi, " + errors.Count + " hata (ayrıntı Console'da).", true);
            else SetStatus(items.Count + " asset indirildi.");
            if (items.Count > errors.Count)
            {
                BulkScan(TpLocalScan.AssetStoreCache, true);
                if (_ownedAutoUpload) await BulkUpload(false);
            }
        }

        void DrawBulk()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Space(6);
            DrawAccount();
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Asset Store indirmelerini tara", "My Assets'ten indirdiğin her paket: " + TpLocalScan.AssetStoreCache), GUILayout.Height(26), GUILayout.Width(210)))
                BulkScan(TpLocalScan.AssetStoreCache, true);
            if (GUILayout.Button("Klasör tara…", GUILayout.Height(26), GUILayout.Width(110)))
            {
                string d = EditorUtility.OpenFolderPanel(".unitypackage klasörü", TpSettings.GetString("BulkFolder", ""), "");
                if (!string.IsNullOrEmpty(d)) { TpSettings.SetString("BulkFolder", d); BulkScan(d, false); }
            }
            if (GUILayout.Button(new GUIContent("My Assets'i aç", "Unity Package Manager ▸ My Assets: henüz indirmediklerini oradan 'Download' et, sonra yeniden tara"), GUILayout.Height(26), GUILayout.Width(110)))
                UnityEditor.PackageManager.UI.Window.Open("");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Label("Asset Store önbelleği: " + TpLocalScan.AssetStoreCache + "   — sahip olduğun ama indirmediğin asset'ler burada görünmez: My Assets'ten indir, sonra tara.", _miniWrap);

            if (_bulk.Count == 0)
            {
                GUILayout.Space(40);
                GUILayout.Label("Tara: Asset Store indirmelerin ya da .unitypackage dolu bir klasör.\nKategori, sürüm ve yayıncı Asset Store paketlerinin içinden okunur; sunucuda olanlar atlanır.", _center);
                EditorGUILayout.EndVertical();
                return;
            }

            // ---- summary + bulk actions
            int nNew = _bulk.Count(r => r.State == "Yeni"), nVer = _bulk.Count(r => r.State == "Yeni sürüm"), nHave = _bulk.Count(r => r.State == "Zaten var");
            var on = _bulk.Where(r => r.On).ToList();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(_bulk.Count + " paket · " + nNew + " yeni · " + nVer + " yeni sürüm · " + nHave + " zaten var  |  seçili " + on.Count + " (" + TpFormat.Size(on.Sum(r => r.Item.Size)) + ")", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            _bulkFilter = GUILayout.TextField(_bulkFilter, EditorStyles.toolbarSearchField, GUILayout.Width(160));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_bulkRunning;
            if (GUILayout.Button("Tümünü seç", EditorStyles.miniButtonLeft, GUILayout.Width(80))) foreach (var r in Visible()) r.On = r.State != "Zaten var";
            if (GUILayout.Button("Hiçbiri", EditorStyles.miniButtonRight, GUILayout.Width(60))) foreach (var r in Visible()) r.On = false;
            GUILayout.Space(16);
            _bulkSetCat = EditorGUILayout.TextField(_bulkSetCat, GUILayout.Width(140));
            if (GUILayout.Button("Seçililere kategori ver", EditorStyles.miniButton, GUILayout.Width(140)) && _bulkSetCat.Trim().Length > 0)
                foreach (var r in _bulk.Where(x => x.On)) r.Category = _bulkSetCat.Trim();
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // ---- table
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("", GUILayout.Width(18));
            GUILayout.Label("Paket", EditorStyles.miniBoldLabel, GUILayout.MinWidth(200));
            GUILayout.Label("Sürüm", EditorStyles.miniBoldLabel, GUILayout.Width(90));
            GUILayout.Label("Kategori", EditorStyles.miniBoldLabel, GUILayout.Width(150));
            GUILayout.Label("Boyut", EditorStyles.miniBoldLabel, GUILayout.Width(64));
            GUILayout.Label("Durum", EditorStyles.miniBoldLabel, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();

            _bulkScroll = EditorGUILayout.BeginScrollView(_bulkScroll);
            foreach (var r in Visible())
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !_bulkRunning;
                r.On = EditorGUILayout.Toggle(r.On, GUILayout.Width(18));
                EditorGUILayout.BeginVertical(GUILayout.MinWidth(200));
                GUILayout.Label(r.Item.Title, EditorStyles.label);
                GUILayout.Label((string.IsNullOrEmpty(r.Item.Publisher) ? "" : r.Item.Publisher + " · ") + r.TargetSlug +
                                (string.IsNullOrEmpty(r.Item.SubCategory) ? "" : " · " + r.Item.SubCategory), _mini);
                EditorGUILayout.EndVertical();
                string v = EditorGUILayout.TextField(r.Version, GUILayout.Width(90));
                if (v != r.Version) { r.Version = v; Classify(r); }
                r.Category = EditorGUILayout.TextField(r.Category, GUILayout.Width(150));
                GUI.enabled = true;
                GUILayout.Label(TpFormat.Size(r.Item.Size), EditorStyles.miniLabel, GUILayout.Width(64));
                var c = GUI.color;
                if (!string.IsNullOrEmpty(r.Result)) GUI.color = r.Result.StartsWith("✓") ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.6f, 0.5f);
                else if (r.State == "Zaten var") GUI.color = new Color(0.7f, 0.7f, 0.7f);
                GUILayout.Label(new GUIContent(string.IsNullOrEmpty(r.Result) ? r.State : r.Result, r.Result), EditorStyles.miniLabel, GUILayout.Width(150));
                GUI.color = c;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            // ---- go
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (_bulkRunning)
            {
                if (GUILayout.Button("Durdur (bu paket bitince)", GUILayout.Height(30), GUILayout.Width(200)) && _bulkCancel != null) _bulkCancel.Requested = true;
            }
            else
            {
                GUI.enabled = on.Count > 0 && !TpUploader.Running && TpSettings.HasToken;
                var bg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
                if (GUILayout.Button("Seçili " + on.Count + " paketi yükle", GUILayout.Height(30), GUILayout.Width(220))) Run(BulkUpload(true));
                GUI.backgroundColor = bg;
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        IEnumerable<BulkRow> Visible()
        {
            string f = (_bulkFilter ?? "").Trim();
            if (f.Length == 0) return _bulk;
            return _bulk.Where(r => Has(r.Item.Title, f) || Has(r.Item.Publisher, f) || Has(r.Category, f) || Has(r.Item.SubCategory, f));
        }

        void BulkScan(string root, bool fromStore)
        {
            if (!Directory.Exists(root))
            {
                SetStatus("Klasör yok: " + root + (fromStore ? " (Asset Store'dan hiç indirme yapılmamış olabilir; Ayarlar'dan önbellek yolunu değiştirebilirsin)" : ""), true);
                return;
            }
            _bulkRoot = root;
            _bulkFromStore = fromStore;
            EditorUtility.DisplayProgressBar("Twice Packages", "Taranıyor: " + root, 0.5f);
            List<TpLocalScan.Item> items;
            try { items = TpLocalScan.Scan(root, fromStore); }
            finally { EditorUtility.ClearProgressBar(); }
            _bulk = items.Select(i => new BulkRow { Item = i, Category = i.Category ?? "", Version = i.Version }).ToList();
            foreach (var r in _bulk) { Classify(r); r.On = r.State != "Zaten var"; }
            _bulkScroll = Vector2.zero;
            SetStatus(items.Count + " paket bulundu.");
        }

        static string StoreUrl(string id) { return string.IsNullOrEmpty(id) ? "" : "https://assetstore.unity.com/packages/slug/" + id; }

        /// <summary>Same asset already in the catalog? Asset Store id first (titles get renamed), then slug.</summary>
        static TpPackage Match(TpLocalScan.Item it)
        {
            if (!string.IsNullOrEmpty(it.AssetStoreId))
            {
                string suffix = "/" + it.AssetStoreId;
                foreach (var p in TpCatalog.Packages)
                    if (!string.IsNullOrEmpty(p.assetStoreUrl) && p.assetStoreUrl.EndsWith(suffix, StringComparison.Ordinal)) return p;
            }
            return TpCatalog.Find(it.Slug);
        }

        static void Classify(BulkRow r)
        {
            var p = Match(r.Item);
            r.TargetSlug = p != null ? p.slug : r.Item.Slug;
            if (p == null) r.State = "Yeni";
            else if (p.Find(r.Version) != null) r.State = "Zaten var";
            else r.State = "Yeni sürüm";
            if (!TpSemVer.IsValid(r.Version)) r.State = "Sürüm geçersiz";
        }

        async Task BulkUpload(bool confirm = true)
        {
            // refresh first so a teammate's upload from five minutes ago is not duplicated
            string err = await TpCatalog.Refresh(true);
            if (err != null) { SetStatus(err, true); return; }
            foreach (var r in _bulk) Classify(r);
            var todo = _bulk.Where(r => r.On && (r.State == "Yeni" || r.State == "Yeni sürüm")).ToList();
            if (todo.Count == 0) { SetStatus("Yüklenecek yeni paket ya da sürüm yok."); return; }
            if (confirm && !EditorUtility.DisplayDialog("Twice Packages — toplu yükleme",
                    todo.Count + " paket yüklenecek (" + TpFormat.Size(todo.Sum(r => r.Item.Size)) + ").\n\n" +
                    "Not: Asset Store'da 'Editor Extensions' ve araç asset'leri çoğunlukla kişi başı lisanslıdır; ekipte kullanan herkesin kendi lisansı olmalı.",
                    "Yükle", "Vazgeç")) return;

            _bulkRunning = true;
            _bulkCancel = new TpCancel();
            int ok = 0, fail = 0;
            try
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    if (_bulkCancel.Requested) break;
                    var r = todo[i];
                    SetStatus("(" + (i + 1) + "/" + todo.Count + ") " + r.Item.Title);
                    var it = r.Item;
                    bool isNew = r.State == "Yeni";
                    var tags = new List<string>();
                    if (it.FromAssetStore) tags.Add("asset-store");
                    if (!string.IsNullOrEmpty(it.SubCategory)) tags.AddRange(it.SubCategory.Split('/').Select(x => x.Trim()).Where(x => x.Length > 0));
                    string unity = it.UnityVersion ?? "";
                    var up = unity.Split('.');
                    if (up.Length >= 2) unity = up[0] + "." + up[1];

                    // a slug taken by a different asset (same title, other publisher) → suffix with the store id
                    string slug = r.TargetSlug;
                    if (isNew && TpCatalog.Find(slug) != null)
                        slug = (slug.Length > 50 ? slug.Substring(0, 50).TrimEnd('-') : slug) + "-" + (string.IsNullOrEmpty(it.AssetStoreId) ? "2" : it.AssetStoreId);

                    string e = await TpUploader.Upload(new TpUploader.Request
                    {
                        IsNewPackage = isNew,
                        Slug = slug,
                        Name = it.Title,
                        Version = r.Version,
                        Category = r.Category,
                        Publisher = it.Publisher,
                        Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        AssetStoreUrl = StoreUrl(it.AssetStoreId),
                        Unity = unity,
                        Changelog = it.FromAssetStore ? "Asset Store " + r.Version : "",
                        SourceFile = it.Path
                    });
                    if (e == null) { r.Result = "✓ yüklendi"; r.State = "Zaten var"; r.On = false; ok++; }
                    else
                    {
                        r.Result = "✗ " + e;
                        fail++;
                        if (e == "İptal edildi.") break;
                    }
                    Repaint();
                }
            }
            finally
            {
                _bulkRunning = false;
                _bulkCancel = null;
            }
            SetStatus("Toplu yükleme bitti: " + ok + " yüklendi" + (fail > 0 ? ", " + fail + " hata (Durum sütununa bak)" : "") + ".", fail > 0);
            foreach (var r in _bulk) if (string.IsNullOrEmpty(r.Result)) Classify(r);
        }
    }
}
