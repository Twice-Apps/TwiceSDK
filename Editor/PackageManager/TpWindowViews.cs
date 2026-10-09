using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static TwiceSDK.PackageManager.TpUi;

namespace TwiceSDK.PackageManager
{
    /// <summary>Installed, Upload and Settings tabs.</summary>
    public sealed partial class TpWindow
    {
        /* =========================================================== installed === */

        VisualElement BuildInstalled()
        {
            var page = new ScrollView(ScrollViewMode.Vertical) { name = "installed" };
            page.style.flexGrow = 1;
            var inner = El("tp-page");
            page.Add(inner);
            var state = TpInstaller.State.installed;
            var updates = state.Select(i => TpCatalog.Find(i.slug)).Where(p => p != null && TpInstaller.HasUpdate(p)).ToList();

            var head = Row();
            head.Add(L("Bu projede yüklü", "tp-toolbar-title"));
            head.Add(L("  " + state.Count + " paket · kayıt " + TpInstaller.StatePath, "tp-muted"));
            head.Add(Flex());
            var all = Btn("Hepsini güncelle (" + updates.Count + ")", () =>
                Run(InstallAsync(updates.Select(p => new KeyValuePair<TpPackage, TpVersion>(p, p.Default)).ToList())), "tp-btn tp-btn--primary");
            all.SetEnabled(updates.Count > 0 && !TpInstaller.Busy);
            head.Add(all);
            inner.Add(head);
            inner.Add(Space(12));

            if (state.Count == 0)
            {
                inner.Add(Empty("Henüz bir şey kurulmadı", "Kütüphaneden bir paketi içe aktardığında burada görünür; kayıt projeyle birlikte commit'lenir."));
                return page;
            }
            foreach (var i in state.ToList())
            {
                var p = TpCatalog.Find(i.slug);
                var row = El("tp-list-row" + (p != null && p.deprecated ? " tp-card--deprecated" : ""));
                var th = El("tp-list-thumb");
                if (p != null) SetImage(th, p);
                row.Add(th);
                var mid = El("tp-grow");
                mid.Add(L((p != null ? p.DisplayName : i.slug) + "   v" + i.version, "tp-bold"));
                mid.Add(L(TpFormat.Date(i.installedAt) + (string.IsNullOrEmpty(i.installedBy) ? "" : " · " + i.installedBy) + " · " + i.guids.Count + " varlık", "tp-muted tp-small"));
                var iv = p != null ? p.Find(i.version) : null;
                if (iv != null && iv.IsBroken) mid.Add(L("Kurulu sürüm bozuk işaretli" + (string.IsNullOrEmpty(iv.note) ? "" : ": " + iv.note), "tp-small tp-bold"));
                row.Add(mid);
                if (p == null) row.Add(Chip("katalogda yok"));
                else if (p.deprecated) row.Add(Chip("DEPRECATED", "dep"));
                else if (TpInstaller.HasUpdate(p)) row.Add(Chip("v" + p.Default.version + " var", "warn"));
                else row.Add(Chip("güncel", "ok"));
                if (p != null && TpInstaller.HasUpdate(p))
                {
                    var u = Btn("Güncelle", () => Install(p, p.Default), "tp-btn tp-btn--small tp-btn--primary");
                    u.SetEnabled(!TpInstaller.Busy);
                    row.Add(u);
                }
                if (p != null) row.Add(Btn("Detay", () => { _tab = Tab.Library; _cat = CatAll; _sub = ""; _selected = p.slug; RebuildAll(); }, "tp-btn tp-btn--small"));
                var slug = i.slug;
                row.Add(Btn("Kaldır", () =>
                {
                    string err = TpInstaller.Uninstall(slug);
                    SetStatus(err ?? (slug + " kaldırıldı."), err != null && err != "Vazgeçildi.");
                }, "tp-btn tp-btn--small tp-btn--danger"));
                var forget = Btn("Unut", () =>
                {
                    if (EditorUtility.DisplayDialog("Twice Package Hub", slug + " kaydı silinsin mi? Dosyalar projede kalır.", "Unut", "Vazgeç")) TpInstaller.Forget(slug);
                }, "tp-btn tp-btn--small");
                forget.tooltip = "Kaydı siler, dosyalara dokunmaz";
                row.Add(forget);
                inner.Add(row);
            }
            return page;
        }

        /* ============================================================== upload === */

        enum Source { Project = 0, File = 1, Folder = 2 }
        static readonly string[] SourceLabels = { "Projeden", ".unitypackage", "Klasör" };

        Source _upSource;
        string _upFile = "", _upFolder = "";
        readonly List<string> _upPaths = new List<string>();
        bool _upIncludeDeps;
        string _upTarget;                    // existing slug for a new version, null = new package
        string _upName = "", _upVersion = "1.0.0", _upCategory = "Tools", _upSub = "", _upOrigin = TpOrigin.Twice,
            _upPublisher = "", _upDesc = "", _upTags = "", _upStoreUrl = "", _upUnity = "", _upChangelog = "", _upDeps = "", _upUpm = "", _upImage = "";
        bool _upDeprecated;
        bool _upAuto = true;
        TpIdentifyResult _upCheck;           // last AI / rules check shown in the form

        void StartNewVersion(TpPackage p)
        {
            _tab = Tab.Upload;
            _upTarget = p.slug;
            _upName = p.name;
            _upVersion = TpSemVer.BumpPatch(p.latest);
            _upCategory = p.category;
            _upSub = p.subcategory;
            _upOrigin = string.IsNullOrEmpty(p.origin) ? TpOrigin.Other : p.origin;
            _upPublisher = p.publisher;
            _upDesc = p.description;
            _upTags = p.tags != null ? string.Join(", ", p.tags) : "";
            _upStoreUrl = p.assetStoreUrl;
            var l = p.Latest;
            _upDeps = l != null && l.dependencies != null ? string.Join(", ", l.dependencies) : "";
            _upUpm = l != null && l.upmDependencies != null ? string.Join(", ", l.upmDependencies) : "";
            _upChangelog = "";
            _upCheck = null;
            RebuildAll();
        }

        static string UnityMajorMinor()
        {
            var p = Application.unityVersion.Split('.');
            return p.Length >= 2 ? p[0] + "." + p[1] : Application.unityVersion;
        }

        VisualElement BuildUpload()
        {
            if (string.IsNullOrEmpty(_upUnity)) _upUnity = UnityMajorMinor();
            var page = new ScrollView(ScrollViewMode.Vertical) { name = "upload" };
            page.style.flexGrow = 1;
            var inner = El("tp-page");
            page.Add(inner);
            inner.Add(L("Yükle", "tp-toolbar-title"));
            inner.Add(L("Yeni bir asset ya da mevcut bir pakete yeni sürüm. Yüklemeden önce hub bunu kütüphanedekilerle karşılaştırır (aynı asset başka adla varsa yeni sürüm olur) ve boş bıraktığın ad, kategori, açıklamayı Claude doldurur.", "tp-text tp-muted"));
            inner.Add(Space(12));

            // ---- target
            var box = El("tp-card-box");
            box.Add(L("Hedef", "tp-box-title"));
            if (_upTarget != null && TpCatalog.Find(_upTarget) == null) { _upTarget = null; _upCheck = null; }   // target gone (deleted / other hub)
            var pk = TpCatalog.Packages.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            var labels = new List<string> { "+ Yeni paket" };
            labels.AddRange(pk.Select(x => x.DisplayName + "  (v" + x.latest + ")"));
            int idx = _upTarget == null ? 0 : pk.FindIndex(x => x.slug == _upTarget) + 1;
            var dd = new DropdownField("Paket", labels, Math.Max(0, idx));
            dd.AddToClassList("tp-field");
            dd.RegisterValueChangedCallback(e =>
            {
                int i = labels.IndexOf(e.newValue);
                if (i <= 0) { _upTarget = null; _upCheck = null; RebuildAll(); }
                else StartNewVersion(pk[i - 1]);
            });
            box.Add(dd);
            inner.Add(box);

            // ---- source
            box = El("tp-card-box");
            box.Add(L("Kaynak", "tp-box-title"));
            var src = new List<string>(SourceLabels);
            box.Add(Dropdown("Nereden", src, SourceLabels[(int)_upSource], v => { _upSource = (Source)src.IndexOf(v); RebuildAll(); }));
            if (_upSource == Source.Project) BuildExportPaths(box);
            else if (_upSource == Source.File)
                box.Add(Row("", L(string.IsNullOrEmpty(_upFile) ? "Dosya seçilmedi" : _upFile + "  ·  " + (File.Exists(_upFile) ? TpFormat.Size(new FileInfo(_upFile).Length) : "yok"), "tp-grow tp-text"),
                    Btn("Seç…", () =>
                    {
                        string f = EditorUtility.OpenFilePanel("Unity paketi", "", "unitypackage");
                        if (!string.IsNullOrEmpty(f)) { _upFile = f; if (_upTarget == null && string.IsNullOrEmpty(_upName)) _upName = Path.GetFileNameWithoutExtension(f); _upOrigin = TpOrigin.Other; RebuildAll(); }
                    }, "tp-btn tp-btn--small")));
            else
            {
                box.Add(L("Unity paketi olmayan, düz klasör olarak duran asset'ler. Klasör .unitypackage'a çevrilir; .meta'sı olan dosyalar guid'lerini korur, olmayanlara kararlı guid üretilir (tekrar yüklemede aynı kalır). Klasör bir Unity projesiyse Assets/ içeriği alınır.", "tp-text tp-muted tp-small"));
                box.Add(Row("", L(string.IsNullOrEmpty(_upFolder) ? "Klasör seçilmedi" : _upFolder + (Directory.Exists(_upFolder) ? "  ·  " + TpFormat.Size(TpPacker.FolderSize(_upFolder)) : ""), "tp-grow tp-text"),
                    Btn("Seç…", () =>
                    {
                        string d = EditorUtility.OpenFolderPanel("Asset klasörü", _upFolder, "");
                        if (!string.IsNullOrEmpty(d)) { _upFolder = d; if (_upTarget == null && string.IsNullOrEmpty(_upName)) _upName = Path.GetFileName(d); _upOrigin = TpOrigin.Other; RebuildAll(); }
                    }, "tp-btn tp-btn--small")));
            }
            inner.Add(box);

            // ---- info
            box = El("tp-card-box");
            var titleRow = Row();
            titleRow.Add(L("Bilgiler", "tp-box-title tp-grow"));
            var check = Btn("AI ile kontrol et ve doldur", () => Run(CheckUpload()), "tp-btn tp-btn--small");
            check.SetEnabled(!_busy && (!string.IsNullOrEmpty(_upName) || _upTarget != null));
            titleRow.Add(check);
            box.Add(titleRow);
            if (_upCheck != null)
            {
                string m = _upCheck.MatchSlug != null
                    ? "Kütüphanede var: " + (TpCatalog.Find(_upCheck.MatchSlug) != null ? TpCatalog.Find(_upCheck.MatchSlug).DisplayName : _upCheck.MatchSlug) + " → yeni sürüm olarak eklenecek" + (_upCheck.deprecated ? " (paket deprecated)" : "") + "."
                    : "Kütüphanede yok → yeni paket." + (_upCheck.candidates != null && _upCheck.candidates.Count > 0 ? "  Benzerler: " + string.Join(", ", _upCheck.candidates.Select(c => c.name)) : "");
                box.Add(L(m + (string.IsNullOrEmpty(_upCheck.note) ? "" : "\n" + _upCheck.note), "tp-callout"));
            }
            if (_upTarget == null) box.Add(Field("Ad", _upName, v => { _upName = v; _upCheck = null; }));
            box.Add(Field("Sürüm", _upVersion, v => _upVersion = v));
            var tgt = TpCatalog.Find(_upTarget);
            if (tgt != null) box.Add(L("Şu an v" + tgt.latest + (tgt.versions != null && tgt.Find(_upVersion) != null ? " — bu sürüm zaten var; yüklersen üzerine yazmak isteyip istemediğin sorulur." : ""), "tp-muted tp-small"));
            box.Add(Dropdown("Kategori", TpCatalog.Categories.ToList(), _upCategory, v => { _upCategory = v; _upSub = ""; RebuildAll(); }));
            var subs = new List<string> { "—" };
            subs.AddRange(TpCatalog.Subcategories(_upCategory));
            if (subs.Count > 1) box.Add(Dropdown("Alt kategori", subs, string.IsNullOrEmpty(_upSub) ? "—" : _upSub, v => _upSub = v == "—" ? "" : v));
            box.Add(Dropdown("Köken", TpOrigin.Labels.ToList(), TpOrigin.Label(_upOrigin), v => _upOrigin = TpOrigin.All[Array.IndexOf(TpOrigin.Labels, v)]));
            box.Add(Field("Yayıncı", _upPublisher, v => _upPublisher = v));
            box.Add(Field("Açıklama", _upDesc, v => _upDesc = v, true));
            box.Add(Field("Etiketler", _upTags, v => _upTags = v));
            box.Add(Field("Asset Store URL", _upStoreUrl, v => _upStoreUrl = v));
            box.Add(Field("En düşük Unity", _upUnity, v => _upUnity = v));
            box.Add(Field("Değişiklik notu", _upChangelog, v => _upChangelog = v, true));
            box.Add(Field("Twice bağımlılıkları", _upDeps, v => _upDeps = v));
            box.Add(Field("UPM bağımlılıkları", _upUpm, v => _upUpm = v));
            box.Add(Check("Deprecated olarak yükle", _upDeprecated, v => _upDeprecated = v));
            box.Add(Check("Yüklerken otomatik AI kontrolü", _upAuto, v => _upAuto = v));
            inner.Add(box);

            // ---- image
            box = El("tp-card-box");
            box.Add(L("Önizleme görseli", "tp-box-title"));
            box.Add(Row("", L(string.IsNullOrEmpty(_upImage) ? (tgt != null ? "Boş bırakırsan mevcut görsel kalır." : "İsteğe bağlı; 640 px'e küçültülür.") : _upImage, "tp-grow tp-text tp-muted"),
                Btn("Seç…", () => { string f = EditorUtility.OpenFilePanel("Önizleme görseli", "", "png,jpg,jpeg"); if (!string.IsNullOrEmpty(f)) { _upImage = f; RebuildAll(); } }, "tp-btn tp-btn--small"),
                string.IsNullOrEmpty(_upImage) ? null : Btn("Kaldır", () => { _upImage = ""; RebuildAll(); }, "tp-btn tp-btn--small")));
            inner.Add(box);

            string problem = UploadProblem();
            if (problem != null) inner.Add(L(problem, "tp-callout tp-callout--warn"));
            var go = Btn(_upTarget == null ? "Paketi yükle" : "v" + _upVersion + " sürümünü yükle", () => Run(DoUpload(false)), "tp-btn tp-btn--primary");
            go.style.height = 36;
            go.style.marginTop = 8;
            go.SetEnabled(problem == null && !TpUploader.Running && !_busy);
            inner.Add(go);
            return page;
        }

        void BuildExportPaths(VisualElement box)
        {
            var drop = L(_upPaths.Count == 0 ? "Project penceresinden klasör/dosya sürükle" : "Daha fazla eklemek için sürükle", "tp-drop");
            drop.RegisterCallback<DragUpdatedEvent>(e => { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; drop.AddToClassList("tp-drop--hot"); });
            drop.RegisterCallback<DragLeaveEvent>(e => drop.RemoveFromClassList("tp-drop--hot"));
            drop.RegisterCallback<DragPerformEvent>(e =>
            {
                DragAndDrop.AcceptDrag();
                foreach (var o in DragAndDrop.objectReferences) AddExportPath(AssetDatabase.GetAssetPath(o));
                RebuildAll();
            });
            box.Add(drop);
            box.Add(Btn("Seçili olanları ekle", () => { foreach (var o in Selection.objects) AddExportPath(AssetDatabase.GetAssetPath(o)); RebuildAll(); }, "tp-btn tp-btn--small"));
            foreach (var p in _upPaths.ToList())
            {
                var pp = p;
                box.Add(Row("", L(p, "tp-grow tp-text"), Btn("✕", () => { _upPaths.Remove(pp); RebuildAll(); }, "tp-icon-btn")));
            }
            box.Add(Check("Bağımlı varlıkları da dahil et", _upIncludeDeps, v => _upIncludeDeps = v));
        }

        void AddExportPath(string p)
        {
            if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets", StringComparison.Ordinal) || _upPaths.Contains(p)) return;
            _upPaths.Add(p);
            _upOrigin = TpOrigin.Twice;   // exported from our own project: ours unless said otherwise
            if (_upTarget == null && string.IsNullOrEmpty(_upName)) _upName = Path.GetFileNameWithoutExtension(p);
        }

        string UploadProblem()
        {
            if (_upSource == Source.Project && _upPaths.Count == 0) return "Kaynak seç: proje klasörü sürükle.";
            if (_upSource == Source.File && !File.Exists(_upFile)) return "Kaynak seç: .unitypackage dosyası.";
            if (_upSource == Source.Folder && !Directory.Exists(_upFolder)) return "Kaynak seç: asset klasörü.";
            if (_upTarget == null && string.IsNullOrEmpty(_upName)) return "Ad gerekli (ya da AI ile kontrol et).";
            if (!TpSemVer.IsValid(_upVersion)) return "Sürüm 1.0.0 biçiminde olmalı.";
            return null;
        }

        TpUploader.Request UploadRequest()
        {
            return new TpUploader.Request
            {
                IsNewPackage = _upTarget == null,
                Slug = _upTarget ?? TpFormat.Slugify(_upName),
                Name = _upName,
                Version = _upVersion.Trim(),
                Category = _upCategory,
                Subcategory = _upSub,
                Origin = _upOrigin,
                StoreId = "",
                Deprecated = _upDeprecated,
                Description = _upDesc,
                Publisher = _upPublisher,
                Tags = TpFormat.SplitList(_upTags),
                AssetStoreUrl = _upStoreUrl,
                Unity = _upUnity,
                Changelog = _upChangelog,
                Dependencies = TpFormat.SplitList(_upDeps),
                UpmDependencies = TpFormat.SplitList(_upUpm),
                SourceFile = _upSource == Source.File ? _upFile : null,
                SourceFolder = _upSource == Source.Folder ? _upFolder : null,
                ExportPaths = _upSource == Source.Project ? new List<string>(_upPaths) : null,
                IncludeDependencies = _upIncludeDeps,
                ImagePath = _upImage,
                AutoIdentify = _upAuto && _upTarget == null && _upCheck == null
            };
        }

        /// <summary>Same check the upload runs, on demand: shows the match and fills empty fields.</summary>
        async Task CheckUpload()
        {
            _busy = true;
            SetStatus("Kütüphaneyle karşılaştırılıyor (Claude)…");
            var r = await TpIdentify.Run(new List<TpIdentifyItem> { new TpIdentifyItem {
                title = _upName, version = _upVersion, publisher = _upPublisher, category = _upCategory,
                filename = Path.GetFileName(_upSource == Source.File ? _upFile : _upSource == Source.Folder ? _upFolder : ""), origin = _upOrigin } }, true);
            if (r.Value != null || r.Key.Count == 0) { SetStatus(r.Value ?? "Sonuç yok.", true); return; }
            var req = UploadRequest();
            TpUploader.ApplyIdentity(req, r.Key[0]);
            _upCheck = r.Key[0];
            if (r.Key[0].MatchSlug != null) { var p = TpCatalog.Find(r.Key[0].MatchSlug); if (p != null) { StartNewVersion(p); _upCheck = r.Key[0]; } }
            else
            {
                _upName = req.Name; _upCategory = req.Category; _upSub = req.Subcategory; _upDesc = req.Description;
                _upPublisher = req.Publisher; _upOrigin = req.Origin;
                if (string.IsNullOrEmpty(_upTags)) _upTags = string.Join(", ", req.Tags ?? new string[0]);
            }
            SetStatus(r.Key[0].MatchSlug != null ? "Kütüphanede bulundu: yeni sürüm olarak eklenecek." : "Yeni paket; bilgiler dolduruldu (" + r.Key[0].source + ").");
        }

        async Task DoUpload(bool overwrite)
        {
            _busy = true;
            var req = UploadRequest();
            req.Overwrite = overwrite;
            SetStatus("Yükleniyor: " + (req.Name ?? req.Slug) + " " + req.Version);
            string err = await TpUploader.Upload(req);
            if (err != null && err.StartsWith(TpUploader.VersionExists, StringComparison.Ordinal))
            {
                string msg = err.Substring(TpUploader.VersionExists.Length);
                if (EditorUtility.DisplayDialog("Twice Package Hub — bu sürüm zaten var", msg + "\n\nÜzerine yazılsın mı? Eski dosya bu sürümle değiştirilir; bu sürümü kurmuş projeler bir sonraki içe aktarmada yeni dosyayı alır.", "Üzerine yaz", "Vazgeç"))
                {
                    _upCheck = req.Identified ?? _upCheck;
                    if (req.Identified != null && req.Identified.MatchSlug != null) _upTarget = req.Identified.MatchSlug;
                    await DoUpload(true);
                    return;
                }
                SetStatus("Yüklenmedi: " + msg);
                return;
            }
            if (err != null) { SetStatus(err, true); EditorUtility.DisplayDialog("Twice Package Hub — yükleme", err, "Tamam"); return; }
            string slug = req.Slug;
            SetStatus((req.Identified != null && req.Identified.MatchSlug != null ? "Kütüphanedeki pakete yeni sürüm olarak eklendi: " : "Yüklendi: ") + slug + " v" + req.Version);
            _upImage = "";
            _upChangelog = "";
            _upCheck = null;
            _selected = slug;
            _tab = Tab.Library;
            _cat = CatAll;
            _sub = "";
        }

        /* ============================================================ settings === */

        VisualElement BuildSettings()
        {
            var page = new ScrollView(ScrollViewMode.Vertical) { name = "settings" };
            page.style.flexGrow = 1;
            var inner = El("tp-page");
            page.Add(inner);
            inner.Add(L("Ayarlar", "tp-toolbar-title"));
            inner.Add(Space(12));

            var box = El("tp-card-box");
            box.Add(L("Bağlantı", "tp-box-title"));
            string who = !string.IsNullOrEmpty(_me) ? _me : !string.IsNullOrEmpty(TpSettings.ConnectedAs) ? "Bağlı: " + TpSettings.ConnectedAs : "Bağlı";
            box.Add(L(who + (TpSettings.UsingPlayTwiceToken ? " (bu projenin PlayTwice token'ı)" : " · " + TpSettings.MachineLabel), "tp-text"));
            box.Add(Space(8));
            var dis = Btn("Bağlantıyı kes", Disconnect, "tp-btn tp-btn--small tp-btn--danger");
            dis.SetEnabled(!TpSettings.UsingPlayTwiceToken);
            box.Add(Row("", Btn("Bağlantıyı test et", () => Run(TestConnection()), "tp-btn tp-btn--small"), dis,
                Btn("Bağlı bilgisayarlarım ↗", () => Application.OpenURL(TpSettings.HubBase + "/packages.php"), "tp-btn tp-btn--small")));
            inner.Add(box);

            box = El("tp-card-box");
            box.Add(L("İçe aktarma", "tp-box-title"));
            box.Add(Check("Unity'nin içe aktarma penceresini göster (dosya dosya seçmek için)", TpSettings.InteractiveImport, v => TpSettings.InteractiveImport = v));
            box.Add(Check("Güncellemede eski sürümden kalan dosyaları silmeyi öner", TpSettings.CleanUpdate, v => TpSettings.CleanUpdate = v));
            box.Add(Check("Ayrıntılı log", TpSettings.Verbose, v => TpSettings.Verbose = v));
            box.Add(L("Kurulum kaydı: " + TpInstaller.StatePath + " (projeyle birlikte commit'le)", "tp-muted tp-small"));
            inner.Add(box);

            box = El("tp-card-box");
            box.Add(L("Asset Store", "tp-box-title"));
            box.Add(Row("", Field("İndirme klasörü", TpLocalScan.AssetStoreCache, v => TpSettings.SetString("AssetStoreCache", v == TpLocalScan.DefaultAssetStoreCache ? "" : v)),
                Btn("…", () => { string d = EditorUtility.OpenFolderPanel("Asset Store indirme klasörü", TpLocalScan.AssetStoreCache, ""); if (!string.IsNullOrEmpty(d)) { TpSettings.SetString("AssetStoreCache", d); RebuildAll(); } }, "tp-icon-btn")));
            inner.Add(box);

            box = El("tp-card-box");
            box.Add(L("Önbellek", "tp-box-title"));
            box.Add(L("İndirilen paketler projeler arasında paylaşılır; aynı sürüm ikinci kez indirilmez.", "tp-muted tp-small"));
            box.Add(Row("", Field("Klasör", TpSettings.CacheDir, v => TpSettings.CustomCacheDir = v == TpSettings.DefaultCacheDir ? "" : v),
                Btn("Aç", () => { Directory.CreateDirectory(TpSettings.CacheDir); EditorUtility.RevealInFinder(TpSettings.CacheDir); }, "tp-btn tp-btn--small")));
            box.Add(Row("", Btn("Boyutu hesapla", () => SetStatus("Önbellek: " + TpFormat.Size(TpCatalog.CacheSize())), "tp-btn tp-btn--small"),
                Btn("Önbelleği temizle", () =>
                {
                    if (!EditorUtility.DisplayDialog("Twice Package Hub", "İndirilmiş tüm paketler ve görseller silinsin mi? (Projelere dokunulmaz.)", "Temizle", "Vazgeç")) return;
                    TpCatalog.ClearFileCache();
                    SetStatus("Önbellek temizlendi.");
                }, "tp-btn tp-btn--small")));
            inner.Add(box);
            return page;
        }
    }
}
