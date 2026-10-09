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
    /// <summary>
    /// "Toplu yükle": the Asset Store library (list + download with Unity's own downloader),
    /// folder scans (.unitypackage files or plain asset folders), automatic identification
    /// against the hub (rules first, Claude for the rest), review, then sequential upload.
    /// Already-uploaded versions and deprecated packages are recognised and left unticked, so
    /// every teammate can run it on their own machine without creating duplicates.
    /// </summary>
    public sealed partial class TpWindow
    {
        const string StNew = "Yeni", StVersion = "Yeni sürüm", StHave = "Zaten var", StDep = "Deprecated";

        sealed class BulkRow
        {
            public TpLocalScan.Item Item;
            public bool On;
            public string Name, Version, Category, Sub, Origin, Publisher, Description;
            public string[] Tags = new string[0];
            public string Target;               // existing slug, or null = new package
            public string State;
            public string Result;
            public bool Deprecated;
            public TpIdentifyResult Id;
        }

        List<BulkRow> _bulk = new List<BulkRow>();
        string _bulkFilter = "";
        string _bulkSetCat = "Tools";
        bool _bulkRunning, _bulkIdentifying;
        TpCancel _bulkCancel;

        // Asset Store account (My Assets)
        List<TpAssetStore.Owned> _owned;
        bool _ownedIncludeHidden, _ownedAutoUpload = true, _ownedBusy;
        TpCancel _ownedCancel;
        HashSet<long> _cachedIds;

        VisualElement BuildBulk()
        {
            var page = new ScrollView(ScrollViewMode.Vertical) { name = "bulk" };
            page.style.flexGrow = 1;
            var inner = El("tp-page");
            inner.style.maxWidth = 1400;
            page.Add(inner);
            inner.Add(L("Toplu yükle", "tp-toolbar-title"));
            inner.Add(L("Taranan her asset kütüphaneyle otomatik karşılaştırılır: aynısı başka adla varsa yeni sürüm olur, aynı sürüm varsa ya da paket deprecated ise işaretsiz gelir. Ad, kategori ve açıklamayı Claude doldurur; yüklemeden önce tabloda düzeltebilirsin.", "tp-text tp-muted"));
            inner.Add(Space(12));
            inner.Add(BuildAccount());
            inner.Add(BuildScanBox());
            if (_bulk.Count > 0) inner.Add(BuildBulkTable());
            return page;
        }

        /* --------------------------------------------------------- Asset Store --- */

        VisualElement BuildAccount()
        {
            var box = El("tp-card-box");
            var top = Row();
            top.Add(L("Asset Store hesabım", "tp-box-title tp-grow"));
            top.Add(L(TpAssetStore.LoggedIn ? CloudProjectSettings.userName : "Unity hesabına giriş yapılmamış", "tp-muted tp-small"));
            box.Add(top);
            string un = TpAssetStore.Unsupported;
            if (un != null) { box.Add(L(un, "tp-callout tp-callout--warn")); return box; }

            var row = Row("tp-wrap");
            var list = Btn(_owned == null ? "Sahip olduklarımı listele" : "Listeyi yenile", () => Run(LoadOwned()), "tp-btn tp-btn--small");
            list.SetEnabled(!_ownedBusy && !_bulkRunning && TpAssetStore.LoggedIn);
            row.Add(list);
            if (_owned != null)
            {
                var missing = MissingOwned();
                int onServer = _owned.Count(o => OnServer(o.Id));
                int cached = _owned.Count(o => !OnServer(o.Id) && Cached(o.Id));
                if (_ownedBusy) row.Add(Btn("Durdur", () => { if (_ownedCancel != null) _ownedCancel.Requested = true; }, "tp-btn tp-btn--small tp-btn--danger"));
                else
                {
                    var dl = Btn("Eksikleri indir (" + missing.Count + ")" + (_ownedAutoUpload ? " ve yükle" : ""), () => Run(DownloadOwned(missing)), "tp-btn tp-btn--small tp-btn--primary");
                    dl.SetEnabled(missing.Count > 0 && !_bulkRunning);
                    row.Add(dl);
                }
                row.Add(L("   " + _owned.Count + " asset · " + onServer + " sunucuda · " + cached + " indirilmiş · " + missing.Count + " indirilecek", "tp-muted"));
                box.Add(row);
                box.Add(Row("tp-wrap", Check("İndirme bitince tara ve yükle", _ownedAutoUpload, v => { _ownedAutoUpload = v; RebuildAll(); }),
                    Check("Gizlediklerimi de dahil et", _ownedIncludeHidden, v => { _ownedIncludeHidden = v; RebuildAll(); })));
            }
            else box.Add(row);
            box.Add(L("Unity'nin kendi indiricisi kullanılır (My Assets ile aynı). Sunucuda zaten olanlar indirilmez.", "tp-muted tp-small"));
            return box;
        }

        bool OnServer(long id)
        {
            string sid = id.ToString();
            foreach (var p in TpCatalog.Packages)
                if (p.storeId == sid || (!string.IsNullOrEmpty(p.assetStoreUrl) && p.assetStoreUrl.EndsWith("/" + sid, StringComparison.Ordinal))) return true;
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
            return _owned.Where(o => (_ownedIncludeHidden || !o.Hidden) && !OnServer(o.Id) && !Cached(o.Id)).ToList();
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
            if (!EditorUtility.DisplayDialog("Twice Package Hub — Asset Store",
                    items.Count + " asset Unity'nin indiricisiyle indirilecek (3'er paralel). Boyuta göre uzun sürebilir ve diskte yer kaplar (" + TpLocalScan.AssetStoreCache + ").\n\n" +
                    (_ownedAutoUpload ? "Bitince kütüphaneyle eşleştirilip yüklenecek." : "Bitince listeye alınacak; yüklemeyi sen başlatırsın."), "Başla", "Vazgeç")) return;
            _ownedBusy = true;
            _ownedCancel = new TpCancel();
            Dictionary<long, string> errors;
            try
            {
                errors = await TpAssetStore.Download(items, 3, pr =>
                    SetStatus("Asset Store indirme: " + (pr.Done + pr.Failed) + "/" + pr.Total + (pr.Failed > 0 ? " (" + pr.Failed + " hata)" : "") +
                              (pr.Active > 0 ? " · " + pr.Current + " " + TpFormat.Size((long)pr.Bytes) + "/" + TpFormat.Size((long)pr.TotalBytes) : "")), _ownedCancel);
            }
            finally { _ownedBusy = false; _ownedCancel = null; }
            RefreshCachedIds();
            foreach (var n in items.Where(i => !_cachedIds.Contains(i.Id) && !errors.ContainsKey(i.Id)).ToList())
                errors[n.Id] = "İndirme bitti ama dosya önbellekte bulunamadı.";
            foreach (var kv in errors)
            {
                var it = items.FirstOrDefault(i => i.Id == kv.Key);
                TpLog.Warn("Asset Store indirilemedi: " + (it != null ? it.Name : kv.Key.ToString()) + " — " + kv.Value);
            }
            SetStatus(errors.Count > 0 ? (items.Count - errors.Count) + " indirildi, " + errors.Count + " hata (ayrıntı Console'da)." : items.Count + " asset indirildi.", errors.Count > 0);
            if (items.Count > errors.Count)
            {
                await Scan(TpLocalScan.Scan(TpLocalScan.AssetStoreCache, true));
                if (_ownedAutoUpload) await BulkUpload(false);
            }
        }

        /* --------------------------------------------------------------- scan --- */

        VisualElement BuildScanBox()
        {
            var box = El("tp-card-box");
            box.Add(L("Bilgisayarımdakiler", "tp-box-title"));
            var row = Row("tp-wrap");
            Button b;
            row.Add(b = Btn("Asset Store indirmelerini tara", () => Run(Scan(TpLocalScan.Scan(TpLocalScan.AssetStoreCache, true))), "tp-btn tp-btn--small"));
            b.tooltip = TpLocalScan.AssetStoreCache;
            row.Add(Btn(".unitypackage klasörü tara…", () =>
            {
                string d = EditorUtility.OpenFolderPanel(".unitypackage klasörü", TpSettings.GetString("BulkFolder", ""), "");
                if (string.IsNullOrEmpty(d)) return;
                TpSettings.SetString("BulkFolder", d);
                Run(Scan(TpLocalScan.Scan(d, false)));
            }, "tp-btn tp-btn--small"));
            row.Add(b = Btn("Asset klasörlerini tara…", () =>
            {
                string d = EditorUtility.OpenFolderPanel("Asset klasörlerinin bulunduğu klasör", TpSettings.GetString("BulkAssetFolders", ""), "");
                if (string.IsNullOrEmpty(d)) return;
                TpSettings.SetString("BulkAssetFolders", d);
                Run(Scan(TpLocalScan.ScanFolders(d)));
            }, "tp-btn tp-btn--small"));
            b.tooltip = "Seçtiğin klasörün her alt klasörü bir asset sayılır (unitypackage olmayanlar için). Yüklerken .unitypackage'a çevrilir.";
            foreach (var x in row.Children()) x.SetEnabled(!_bulkRunning && !_bulkIdentifying);
            box.Add(row);
            return box;
        }

        async Task Scan(List<TpLocalScan.Item> items)
        {
            _busy = true;
            _bulk = items.Select(i => new BulkRow
            {
                Item = i, Name = i.Title, Version = i.Version, Category = i.Category ?? "", Sub = i.SubCategory ?? "",
                Origin = i.FromAssetStore ? TpOrigin.Store : TpOrigin.Other, Publisher = i.Publisher, State = StNew, On = true
            }).ToList();
            SetStatus(items.Count + " asset bulundu; kütüphaneyle karşılaştırılıyor…");
            RebuildAll();
            await Identify(false);
            var unknown = _bulk.Where(r => r.Target == null).ToList();
            if (unknown.Count > 0) await Identify(true);
        }

        /// <summary>ai=false: fast exact pass over everything; ai=true: Claude only for rows still without a match.</summary>
        async Task Identify(bool ai)
        {
            var rows = ai ? _bulk.Where(r => r.Target == null && r.Result == null).ToList() : _bulk.ToList();
            if (rows.Count == 0) return;
            _bulkIdentifying = true;
            try
            {
                var items = rows.Select(r => new TpIdentifyItem
                {
                    title = r.Item.Title, version = r.Version, publisher = r.Item.Publisher,
                    category = string.IsNullOrEmpty(r.Item.SubCategory) ? r.Item.Category : r.Item.Category + "/" + r.Item.SubCategory,
                    storeId = r.Item.AssetStoreId, filename = Path.GetFileName(r.Item.Path), origin = r.Origin
                }).ToList();
                var res = await TpIdentify.Run(items, ai, (n, t) => SetStatus((ai ? "Claude tanımlıyor… " : "Kütüphaneyle karşılaştırılıyor… ") + n + "/" + t));
                for (int i = 0; i < res.Key.Count && i < rows.Count; i++) ApplyRow(rows[i], res.Key[i]);
                if (res.Value != null) SetStatus((ai ? "Claude: " : "") + res.Value, true);
                else SetStatus(_bulk.Count(r => r.State == StNew) + " yeni · " + _bulk.Count(r => r.State == StVersion) + " yeni sürüm · " + _bulk.Count(r => r.State == StHave) + " zaten var · " + _bulk.Count(r => r.State == StDep) + " deprecated");
            }
            finally { _bulkIdentifying = false; RebuildAll(); }
        }

        void ApplyRow(BulkRow r, TpIdentifyResult id)
        {
            r.Id = id;
            if (!string.IsNullOrEmpty(id.name)) r.Name = id.name;
            if (!string.IsNullOrEmpty(id.category)) r.Category = id.category;
            r.Sub = id.subcategory ?? "";
            if (!string.IsNullOrEmpty(id.publisher)) r.Publisher = id.publisher;
            if (!string.IsNullOrEmpty(id.description)) r.Description = id.description;
            if (id.tags != null && id.tags.Length > 0) r.Tags = id.tags;
            if (!string.IsNullOrEmpty(id.origin)) r.Origin = id.origin;
            SetTarget(r, id.MatchSlug);
        }

        void SetTarget(BulkRow r, string slug)
        {
            r.Target = slug;
            var p = TpCatalog.Find(slug);
            if (p == null) { r.Target = null; r.State = StNew; r.On = true; return; }
            r.Name = p.DisplayName;
            if (p.Find(r.Version) != null) { r.State = StHave; r.On = false; }
            else if (p.deprecated) { r.State = StDep; r.On = false; }
            else { r.State = StVersion; r.On = true; }
        }

        /* -------------------------------------------------------------- table --- */

        IEnumerable<BulkRow> Visible()
        {
            string f = (_bulkFilter ?? "").Trim();
            if (f.Length == 0) return _bulk;
            return _bulk.Where(r => Has(r.Name, f) || Has(r.Publisher, f) || Has(r.Category, f) || Has(r.State, f));
        }

        VisualElement BuildBulkTable()
        {
            var box = El("tp-card-box");
            var on = _bulk.Where(r => r.On).ToList();
            var head = Row("tp-wrap");
            head.Add(L(_bulk.Count + " asset · " + _bulk.Count(r => r.State == StNew) + " yeni · " + _bulk.Count(r => r.State == StVersion) + " yeni sürüm · " +
                       _bulk.Count(r => r.State == StHave) + " zaten var · " + _bulk.Count(r => r.State == StDep) + " deprecated  |  seçili " + on.Count + " (" + TpFormat.Size(on.Sum(r => r.Item.Size)) + ")", "tp-grow tp-text"));
            var ai = Btn("Claude ile yeniden tanımla", () => Run(Identify(true)), "tp-btn tp-btn--small");
            ai.SetEnabled(!_bulkRunning && !_bulkIdentifying);
            head.Add(ai);
            box.Add(head);
            box.Add(Space(6));

            var tools = Row("tp-wrap");
            tools.Add(Btn("Yeni olanları seç", () => { foreach (var r in Visible()) r.On = r.State == StNew || r.State == StVersion; RebuildAll(); }, "tp-btn tp-btn--small"));
            tools.Add(Btn("Hiçbiri", () => { foreach (var r in Visible()) r.On = false; RebuildAll(); }, "tp-btn tp-btn--small"));
            var cat = Dropdown(null, TpCatalog.Categories.ToList(), _bulkSetCat, v => _bulkSetCat = v);
            cat.style.width = 120;
            tools.Add(cat);
            tools.Add(Btn("Seçililere kategori ver", () => { foreach (var r in _bulk.Where(x => x.On)) { r.Category = _bulkSetCat; r.Sub = ""; } RebuildAll(); }, "tp-btn tp-btn--small"));
            tools.Add(Btn("Seçilileri deprecated yükle", () => { foreach (var r in _bulk.Where(x => x.On)) r.Deprecated = !r.Deprecated; RebuildAll(); }, "tp-btn tp-btn--small"));
            var search = new TextField { value = _bulkFilter };
            search.style.width = 180;
            search.RegisterValueChangedCallback(e => { _bulkFilter = e.newValue; search.schedule.Execute(RebuildAll).StartingIn(250); });
            tools.Add(Flex());
            tools.Add(search);
            foreach (var x in tools.Children()) x.SetEnabled(!_bulkRunning);
            box.Add(tools);
            box.Add(Space(6));

            var th = El("tp-table-head");
            th.Add(Cell(L(""), 22)); th.Add(Cell(L("PAKET"), 260)); th.Add(Cell(L("SÜRÜM"), 80)); th.Add(Cell(L("KATEGORİ"), 120));
            th.Add(Cell(L("ALT KATEGORİ"), 140)); th.Add(Cell(L("KÖKEN"), 100)); th.Add(Cell(L("HEDEF"), 220)); th.Add(Cell(L("BOYUT"), 70)); th.Add(L("DURUM"));
            box.Add(th);
            foreach (var r in Visible().Take(600)) box.Add(BulkRowView(r));
            if (Visible().Count() > 600) box.Add(L("İlk 600 satır gösteriliyor; filtreyle daralt.", "tp-muted tp-small"));

            var go = Row();
            go.style.marginTop = 10;
            go.Add(Flex());
            if (_bulkRunning) go.Add(Btn("Durdur (bu paket bitince)", () => { if (_bulkCancel != null) _bulkCancel.Requested = true; }, "tp-btn tp-btn--danger"));
            else
            {
                var up = Btn("Seçili " + on.Count + " asset'i yükle", () => Run(BulkUpload(true)), "tp-btn tp-btn--primary");
                up.style.height = 34;
                up.SetEnabled(on.Count > 0 && !TpUploader.Running && !_bulkIdentifying);
                go.Add(up);
            }
            box.Add(go);
            return box;
        }

        static VisualElement Cell(VisualElement e, float w)
        {
            e.AddToClassList("tp-cell");
            e.style.width = w;
            return e;
        }

        VisualElement BulkRowView(BulkRow r)
        {
            bool muted = !r.On && (r.State == StHave || r.State == StDep);
            var row = El("tp-table-row" + (muted ? " tp-table-row--muted" : ""));
            var t = new Toggle { value = r.On };
            t.RegisterValueChangedCallback(e => { r.On = e.newValue; QueueRebuild(); });
            t.SetEnabled(!_bulkRunning);
            row.Add(Cell(t, 22));

            var name = El("");
            var nf = new TextField { value = r.Name };
            nf.RegisterValueChangedCallback(e => r.Name = e.newValue);
            nf.SetEnabled(r.Target == null && !_bulkRunning);
            name.Add(nf);
            name.Add(L((r.Item.IsFolder ? "📁 " : "") + (string.IsNullOrEmpty(r.Publisher) ? "" : r.Publisher + " · ") + Path.GetFileName(r.Item.Path), "tp-faint tp-small tp-ellipsis"));
            row.Add(Cell(name, 260));

            var vf = new TextField { value = r.Version };
            vf.RegisterValueChangedCallback(e => { r.Version = e.newValue; SetTarget(r, r.Target); });
            row.Add(Cell(vf, 80));

            var cats = TpCatalog.Categories.ToList();
            var cd = new DropdownField(cats, Math.Max(0, cats.IndexOf(r.Category)));
            cd.RegisterValueChangedCallback(e => { r.Category = e.newValue; r.Sub = ""; QueueRebuild(); });
            cd.SetEnabled(r.Target == null);
            row.Add(Cell(cd, 120));

            var subs = new List<string> { "—" };
            subs.AddRange(TpCatalog.Subcategories(r.Category));
            var sd = new DropdownField(subs, Math.Max(0, subs.IndexOf(string.IsNullOrEmpty(r.Sub) ? "—" : r.Sub)));
            sd.RegisterValueChangedCallback(e => r.Sub = e.newValue == "—" ? "" : e.newValue);
            sd.SetEnabled(r.Target == null && subs.Count > 1);
            row.Add(Cell(sd, 140));

            var od = new DropdownField(TpOrigin.Labels.ToList(), Math.Max(0, Array.IndexOf(TpOrigin.All, r.Origin)));
            od.RegisterValueChangedCallback(e => r.Origin = TpOrigin.All[Array.IndexOf(TpOrigin.Labels, e.newValue)]);
            od.SetEnabled(r.Target == null);
            row.Add(Cell(od, 100));

            // target: matched package, or new; ambiguous rows offer the candidates
            var tgt = El("");
            var tp = TpCatalog.Find(r.Target);
            if (tp != null) tgt.Add(L("→ " + tp.DisplayName + "  (v" + tp.latest + ")", "tp-text tp-small tp-ellipsis"));
            else if (r.Id != null && r.Id.candidates != null && r.Id.candidates.Count > 0)
            {
                var ch = new List<string> { "Yeni paket" };
                ch.AddRange(r.Id.candidates.Select(c => c.name + " (" + c.slug + ")"));
                var md = new DropdownField(ch, 0);
                md.RegisterValueChangedCallback(e => { int i = ch.IndexOf(e.newValue); SetTarget(r, i > 0 ? r.Id.candidates[i - 1].slug : null); QueueRebuild(); });
                tgt.Add(md);
                tgt.Add(L("benzer var, seç", "tp-faint tp-small"));
            }
            else tgt.Add(L("Yeni paket", "tp-muted tp-small"));
            if (r.Id != null && r.Id.source == "ai" && !string.IsNullOrEmpty(r.Id.note)) tgt.tooltip = r.Id.note;
            row.Add(Cell(tgt, 220));

            row.Add(Cell(L(TpFormat.Size(r.Item.Size), "tp-muted tp-small"), 70));
            var st = Row();
            if (!string.IsNullOrEmpty(r.Result)) st.Add(Chip(r.Result.StartsWith("✓") ? "yüklendi" : "hata", r.Result.StartsWith("✓") ? "ok" : "danger"));
            else st.Add(Chip(r.State, r.State == StNew ? "accent" : r.State == StVersion ? "warn" : r.State == StDep ? "dep" : ""));
            if (r.Deprecated) st.Add(Chip("deprecated yüklenecek", "dep"));
            if (r.Id != null && r.Id.source == "ai") st.Add(Chip("AI"));
            if (!string.IsNullOrEmpty(r.Result) && !r.Result.StartsWith("✓")) st.tooltip = r.Result;
            row.Add(st);
            return row;
        }

        /* ------------------------------------------------------------- upload --- */

        async Task BulkUpload(bool confirm)
        {
            var todo = _bulk.Where(r => r.On).ToList();
            if (todo.Count == 0) { SetStatus("Seçili asset yok."); return; }
            int have = todo.Count(r => r.State == StHave);
            bool overwriteAll = false;
            if (confirm)
            {
                string msg = todo.Count + " asset yüklenecek (" + TpFormat.Size(todo.Sum(r => r.Item.Size)) + ").";
                if (have > 0) msg += "\n\n" + have + " tanesi kütüphanede aynı sürümle zaten var.";
                msg += "\n\nNot: Asset Store'da araç / editor extension asset'leri çoğunlukla kişi başı lisanslıdır; ekipte kullanan herkesin kendi lisansı olmalı.";
                int c = have > 0
                    ? EditorUtility.DisplayDialogComplex("Twice Package Hub — toplu yükleme", msg, "Yükle, var olanları atla", "Vazgeç", "Yükle, var olanların üzerine yaz")
                    : (EditorUtility.DisplayDialog("Twice Package Hub — toplu yükleme", msg, "Yükle", "Vazgeç") ? 0 : 1);
                if (c == 1) return;
                overwriteAll = c == 2;
                if (!overwriteAll) todo = todo.Where(r => r.State != StHave).ToList();
            }
            _bulkRunning = true;
            _bulkCancel = new TpCancel();
            int ok = 0, fail = 0;
            try
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    if (_bulkCancel.Requested) break;
                    var r = todo[i];
                    SetStatus("(" + (i + 1) + "/" + todo.Count + ") " + r.Name);
                    var it = r.Item;
                    string unity = it.UnityVersion ?? "";
                    var up = unity.Split('.');
                    if (up.Length >= 2) unity = up[0] + "." + up[1];
                    var req = new TpUploader.Request
                    {
                        IsNewPackage = r.Target == null,
                        Slug = r.Target ?? TpFormat.Slugify(r.Name),
                        Name = r.Name,
                        Version = r.Version,
                        Category = r.Category,
                        Subcategory = r.Sub,
                        Origin = r.Origin,
                        StoreId = it.AssetStoreId,
                        Deprecated = r.Deprecated,
                        Publisher = r.Publisher,
                        Description = r.Description,
                        Tags = (r.Tags ?? new string[0]).Concat(it.FromAssetStore ? new[] { "asset-store" } : new string[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        AssetStoreUrl = string.IsNullOrEmpty(it.AssetStoreId) ? "" : "https://assetstore.unity.com/packages/slug/" + it.AssetStoreId,
                        Unity = unity,
                        Changelog = it.FromAssetStore ? "Asset Store " + r.Version : "",
                        SourceFile = it.IsFolder ? null : it.Path,
                        SourceFolder = it.IsFolder ? it.Path : null,
                        AutoIdentify = false,          // identified above (rules + Claude)
                        Overwrite = overwriteAll && r.State == StHave
                    };
                    string e = await TpUploader.Upload(req);
                    if (e != null && e.StartsWith(TpUploader.VersionExists, StringComparison.Ordinal))
                    {
                        // a teammate uploaded it meanwhile
                        if (overwriteAll) { req.Overwrite = true; e = await TpUploader.Upload(req); }
                        else { r.Result = "✓ zaten vardı"; r.State = StHave; r.On = false; continue; }
                    }
                    if (e == null) { r.Result = "✓ yüklendi"; r.State = StHave; r.On = false; ok++; }
                    else { r.Result = "✗ " + e; fail++; if (e == "İptal edildi.") break; }
                    QueueRebuild();
                }
            }
            finally { _bulkRunning = false; _bulkCancel = null; }
            SetStatus("Toplu yükleme bitti: " + ok + " yüklendi" + (fail > 0 ? ", " + fail + " hata (satırın üstüne gel)" : "") + ".", fail > 0);
            await TpCatalog.Refresh(true);
        }
    }
}
