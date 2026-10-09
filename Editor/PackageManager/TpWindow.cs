using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static TwiceSDK.PackageManager.TpUi;

namespace TwiceSDK.PackageManager
{
    /// <summary>Twice ▸ Twice Package Hub — the team's asset library (UI Toolkit).</summary>
    public sealed partial class TpWindow : EditorWindow
    {
        enum Tab { Library = 0, Twice = 1, Installed = 2, Upload = 3, Bulk = 4, Settings = 5 }
        static readonly string[] TabLabels = { "Kütüphane", "Twice", "Yüklü", "Yükle", "Toplu yükle", "Ayarlar" };

        const string CatAll = "\u0001all", CatInstalled = "\u0001inst", CatUpdates = "\u0001upd", CatFav = "\u0001fav", CatMine = "\u0001mine";
        const string UssPath = "Packages/co.twiceapps.sdk/Editor/PackageManager/TpWindow.uss";

        Tab _tab;
        string _search = "";
        string _cat = CatAll;
        string _sub = "";
        string _sort = "Ad";
        bool _grid = true;
        bool _showDeprecated = true;
        string _selected;
        string _status = "";
        bool _statusError;
        bool _busy;
        string _me = "";

        VisualElement _root, _header, _content, _statusBar;
        Label _statusLabel;
        IVisualElementScheduledItem _rebuildJob;
        readonly Dictionary<string, Vector2> _scroll = new Dictionary<string, Vector2>();

        [MenuItem("Twice/Twice Package Hub", false, 0)]
        public static void Open()
        {
            var w = GetWindow<TpWindow>();
            w.titleContent = new GUIContent("Twice Package Hub", EditorGUIUtility.IconContent("Prefab Icon").image);
            w.minSize = new Vector2(960, 560);
            w.Show();
        }

        void OnEnable()
        {
            _tab = (Tab)Mathf.Clamp(TpSettings.GetInt("Tab2", 0), 0, 5);
            _grid = TpSettings.GetInt("Grid", 1) == 1;
            _showDeprecated = TpSettings.GetInt("ShowDep", 1) == 1;
            TpCatalog.Changed += QueueRebuild;
            TpInstaller.Changed += QueueRebuild;
            if (TpSettings.HasToken) Run(RefreshCatalog(false));
        }

        void OnDisable()
        {
            TpCatalog.Changed -= QueueRebuild;
            TpInstaller.Changed -= QueueRebuild;
            TpSettings.SetInt("Tab2", (int)_tab);
            TpSettings.SetInt("Grid", _grid ? 1 : 0);
            TpSettings.SetInt("ShowDep", _showDeprecated ? 1 : 0);
        }

        public void CreateGUI()
        {
            _root = rootVisualElement;
            _root.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (sheet != null) _root.styleSheets.Add(sheet);
            _root.AddToClassList("tp-root");
            _root.AddToClassList(EditorGUIUtility.isProSkin ? "tp-dark" : "tp-light");

            _header = El("tp-header");
            _content = El("tp-grow");
            _statusBar = El("tp-status");
            _statusLabel = L("", "tp-grow");
            _statusBar.Add(_statusLabel);
            _statusBar.Add(L(TpSettings.HubBase.Replace("https://", ""), "tp-faint"));
            _root.Add(_header);
            _root.Add(_content);
            _root.Add(_statusBar);
            RebuildAll();
        }

        /* ============================================================ plumbing === */

        void QueueRebuild()
        {
            if (_root == null) return;
            if (_rebuildJob != null) _rebuildJob.Pause();
            _rebuildJob = _root.schedule.Execute(RebuildAll).StartingIn(30);
        }

        void RebuildAll()
        {
            if (_root == null || _content == null) return;
            BuildHeader();
            RememberScroll();
            _content.Clear();
            if (!TpSettings.HasToken) { _content.Add(BuildConnect()); UpdateStatus(); return; }
            switch (_tab)
            {
                case Tab.Library: _content.Add(BuildLibrary(false)); break;
                case Tab.Twice: _content.Add(BuildLibrary(true)); break;
                case Tab.Installed: _content.Add(BuildInstalled()); break;
                case Tab.Upload: _content.Add(BuildUpload()); break;
                case Tab.Bulk: _content.Add(BuildBulk()); break;
                default: _content.Add(BuildSettings()); break;
            }
            RestoreScroll();
            UpdateStatus();
        }

        /* ScrollViews are recreated on rebuild; their offsets are kept by name. */
        void RememberScroll()
        {
            _content.Query<ScrollView>().ForEach(sv => { if (!string.IsNullOrEmpty(sv.name)) _scroll[sv.name] = sv.scrollOffset; });
        }

        void RestoreScroll()
        {
            _content.schedule.Execute(() =>
                _content.Query<ScrollView>().ForEach(sv => { Vector2 o; if (!string.IsNullOrEmpty(sv.name) && _scroll.TryGetValue(sv.name, out o)) sv.scrollOffset = o; }));
        }

        void SetStatus(string msg, bool error = false)
        {
            _status = msg ?? "";
            _statusError = error;
            if (error && !string.IsNullOrEmpty(msg)) TpLog.Warn(msg);
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (_statusLabel == null) return;
            _statusLabel.text = (_busy || TpInstaller.Busy ? "⏳  " : "") + _status;
            _statusBar.EnableInClassList("tp-status--error", _statusError);
        }

        /// <summary>Runs an async UI action; exceptions land in the status bar instead of vanishing.</summary>
        async void Run(Task t)
        {
            try { await t; }
            catch (Exception e) { SetStatus(e.Message, true); Debug.LogException(e); }
            finally { _busy = false; UpdateStatus(); QueueRebuild(); }
        }

        async Task RefreshCatalog(bool force)
        {
            _busy = true;
            SetStatus("Katalog yükleniyor…");
            string err = await TpCatalog.Refresh(force);
            SetStatus(err == null ? (TpCatalog.Packages.Count + " paket · " + (TpCatalog.User ?? "")) : err, err != null);
        }

        static bool CanEdit(TpPackage p)
        {
            return p != null && (TpCatalog.IsSuper || (!string.IsNullOrEmpty(TpCatalog.User) && p.createdBy == TpCatalog.User));
        }

        void Go(Tab t) { _tab = t; RebuildAll(); }

        /* ============================================================== header === */

        void BuildHeader()
        {
            _header.Clear();
            _header.Add(El("tp-brand-dot"));
            _header.Add(L("Twice Package Hub", "tp-brand"));
            if (!TpSettings.HasToken) return;

            int updates = TpCatalog.Packages.Count(p => TpInstaller.HasUpdate(p));
            for (int i = 0; i < TabLabels.Length; i++)
            {
                var t = (Tab)i;
                var b = Btn(TabLabels[i], () => Go(t), "tp-tab" + (_tab == t ? " tp-tab--active" : ""));
                if (t == Tab.Installed && updates > 0) b.Add(L(updates.ToString(), "tp-tab-badge"));
                _header.Add(b);
            }
            _header.Add(Flex());
            if (_tab == Tab.Library || _tab == Tab.Twice)
            {
                var s = new ToolbarSearchField();
                s.AddToClassList("tp-search");
                s.value = _search;
                IVisualElementScheduledItem job = null;
                s.RegisterValueChangedCallback(e =>
                {
                    _search = e.newValue;
                    if (job != null) job.Pause();
                    job = s.schedule.Execute(() => { RebuildAll(); }).StartingIn(220);
                });
                _header.Add(s);
                if (!string.IsNullOrEmpty(_search)) s.schedule.Execute(() => s.Q<TextField>()?.Focus());
            }
            var refresh = IconBtn("Refresh", "Kataloğu yenile", () => Run(RefreshCatalog(true)));
            refresh.SetEnabled(!_busy);
            _header.Add(refresh);
        }

        /* ============================================================= connect === */

        string _connCode, _connPoll, _connUrl;
        DateTime _connUntil;
        TpCancel _connCancel;

        VisualElement BuildConnect()
        {
            var wrap = El("tp-empty");
            var box = El("tp-connect");
            box.Add(L("Bu bilgisayar bağlı değil", "tp-h1"));
            box.Add(Space(6));
            box.Add(L("Kütüphaneyi görmek için twicehub hesabınla bağla. Tarayıcıda onay ekranı açılır; Onayla'ya basınca bağlantı bu bilgisayara kendiliğinden gelir. Yalnız yöneticiler bağlanabilir.", "tp-text tp-muted"));
            box.Add(Space(16));
            if (_connCode == null)
            {
                var b = Btn("Twicehub ile bağlan", () => Run(Connect()), "tp-btn tp-btn--primary tp-btn--block");
                b.style.height = 36;
                b.SetEnabled(!_busy);
                box.Add(b);
            }
            else
            {
                box.Add(L("Tarayıcıda onay bekleniyor. Panelde gördüğün kod bununla aynı olmalı:", "tp-text tp-muted"));
                box.Add(L(_connCode, "tp-code"));
                var left = L("", "tp-muted");
                left.style.unityTextAlign = TextAnchor.MiddleCenter;
                left.schedule.Execute(() =>
                {
                    int s = Math.Max(0, (int)(_connUntil - DateTime.UtcNow).TotalSeconds);
                    left.text = "Kalan süre " + (s / 60) + ":" + (s % 60).ToString("00");
                }).Every(500);
                box.Add(left);
                box.Add(Space(12));
                box.Add(Row("", Btn("Onay sayfasını yeniden aç", () => Application.OpenURL(_connUrl), "tp-btn tp-btn--block"),
                    Btn("Vazgeç", () => { if (_connCancel != null) _connCancel.Requested = true; })));
            }
            wrap.Add(box);
            return wrap;
        }

        async Task Connect()
        {
            _busy = true;
            SetStatus("Bağlantı isteği oluşturuluyor…");
            var start = await TpHub.PostJson<TpConnectStartResponse>("connect_start", new TpConnectStartRequest { label = TpSettings.MachineLabel });
            if (!start.Ok) { SetStatus(start.Error, true); return; }
            _connCode = start.Data.code;
            _connPoll = start.Data.pollId;
            _connUrl = start.Data.url;
            _connUntil = DateTime.UtcNow.AddSeconds(start.Data.expiresIn > 0 ? start.Data.expiresIn : 600);
            _connCancel = new TpCancel();
            Application.OpenURL(_connUrl);
            SetStatus("Tarayıcıda onayla…");
            _busy = false;
            RebuildAll();
            try
            {
                while (DateTime.UtcNow < _connUntil && !_connCancel.Requested)
                {
                    await Task.Delay(2500);
                    if (_connCancel.Requested) break;
                    var p = await TpHub.PostJson<TpConnectPollResponse>("connect_poll", new TpConnectPollRequest { code = _connCode, pollId = _connPoll });
                    if (!p.Ok) continue;   // transient network error: keep polling
                    if (p.Data.status == "approved" && !string.IsNullOrEmpty(p.Data.token))
                    {
                        TpSettings.Token = p.Data.token;
                        TpSettings.ConnectedAs = p.Data.user;
                        _connCode = null;
                        _tab = Tab.Library;
                        await TestConnection();
                        return;
                    }
                    if (p.Data.status == "expired") { SetStatus("Bağlantı isteği reddedildi ya da süresi doldu.", true); return; }
                }
                SetStatus(_connCancel.Requested ? "Vazgeçildi." : "Süre doldu; yeniden bağlan.", !_connCancel.Requested);
            }
            finally
            {
                _connCode = null;
                _connPoll = null;
                _connCancel = null;
            }
        }

        async Task TestConnection()
        {
            _busy = true;
            SetStatus("Bağlanılıyor…");
            var r = await TpHub.Get<TpMeResponse>("me");
            if (!r.Ok) { _me = ""; SetStatus(r.Error, true); return; }
            TpSettings.ConnectedAs = r.Data.user + (r.Data.super ? " (yönetici)" : "");
            _me = "Bağlı: " + TpSettings.ConnectedAs;
            SetStatus(_me);
            await RefreshCatalog(false);
        }

        void Disconnect()
        {
            if (!EditorUtility.DisplayDialog("Twice Package Hub", "Bu bilgisayarın bağlantısı kesilsin mi? Tekrar bağlanmak için panel onayı gerekir.", "Bağlantıyı kes", "Vazgeç")) return;
            TpSettings.Token = "";
            TpSettings.ConnectedAs = "";
            TpCatalog.Forget();
            _selected = null;
            SetStatus("Bağlantı kesildi.");
            RebuildAll();
        }

        /* ============================================================= library === */

        IEnumerable<TpPackage> Scope(bool twiceOnly)
        {
            IEnumerable<TpPackage> q = TpCatalog.Packages;
            return twiceOnly ? q.Where(p => p.IsTwice) : q;
        }

        List<TpPackage> Filtered(bool twiceOnly)
        {
            var fav = TpSettings.Favorites;
            var q = Scope(twiceOnly);
            switch (_cat)
            {
                case CatAll: break;
                case CatInstalled: q = q.Where(p => TpInstaller.Installed(p.slug) != null); break;
                case CatUpdates: q = q.Where(p => TpInstaller.HasUpdate(p)); break;
                case CatFav: q = q.Where(p => fav.Contains(p.slug)); break;
                case CatMine: q = q.Where(p => p.createdBy == TpCatalog.User); break;
                default:
                    q = q.Where(p => string.Equals(p.category ?? "", _cat, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(_sub)) q = q.Where(p => string.Equals(p.subcategory ?? "", _sub, StringComparison.OrdinalIgnoreCase));
                    break;
            }
            if (!_showDeprecated) q = q.Where(p => !p.deprecated);
            string s = (_search ?? "").Trim();
            if (s.Length > 0)
                q = q.Where(p => Has(p.name, s) || Has(p.slug, s) || Has(p.publisher, s) || Has(p.description, s) || Has(p.category, s) ||
                                 Has(p.subcategory, s) || Has(p.notes, s) || (p.tags != null && p.tags.Any(t => Has(t, s))));
            IOrderedEnumerable<TpPackage> o;
            // Deprecated always sinks to the bottom, whatever the sort.
            switch (_sort)
            {
                case "En yeni": o = q.OrderBy(p => p.deprecated).ThenByDescending(p => p.updatedAt ?? ""); break;
                case "En çok indirilen": o = q.OrderBy(p => p.deprecated).ThenByDescending(p => p.downloads); break;
                default: o = q.OrderBy(p => p.deprecated).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase); break;
            }
            return o.ToList();
        }

        static bool Has(string hay, string needle) { return !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0; }

        VisualElement BuildLibrary(bool twiceOnly)
        {
            var body = El("tp-body");
            body.Add(BuildSidebar(twiceOnly));

            var main = El("tp-main");
            var list = Filtered(twiceOnly);

            var bar = El("tp-toolbar");
            bar.Add(L(twiceOnly && _cat == CatAll ? "Twice asset'leri" : SideTitle(), "tp-toolbar-title"));
            bar.Add(L("  " + list.Count + " paket", "tp-muted"));
            bar.Add(Flex());
            var sort = Dropdown(null, new List<string> { "Ad", "En yeni", "En çok indirilen" }, _sort, v => { _sort = v; RebuildAll(); });
            sort.style.width = 150;
            bar.Add(sort);
            var depT = Check("Deprecated", _showDeprecated, v => { _showDeprecated = v; RebuildAll(); });
            depT.style.marginLeft = 8;
            bar.Add(depT);
            var mode = Btn(_grid ? "☰" : "▦", () => { _grid = !_grid; RebuildAll(); }, "tp-icon-btn");
            mode.tooltip = _grid ? "Liste görünümü" : "Kart görünümü";
            bar.Add(mode);
            main.Add(bar);

            var sv = new ScrollView(ScrollViewMode.Vertical) { name = twiceOnly ? "lib-twice" : "lib-all" };
            sv.style.flexGrow = 1;
            if (list.Count == 0)
            {
                sv.Add(Empty(TpCatalog.Packages.Count == 0 ? "Kütüphane boş" : "Eşleşen paket yok",
                    TpCatalog.Packages.Count == 0
                        ? "İlk paketleri Yükle ya da Toplu yükle sekmesinden gönder. Asset Store kütüphaneni toplu yüklemeden tek seferde çekebilirsin."
                        : (twiceOnly ? "Twice köklü paket yok. Bir pakete köken olarak Twice ver (detay ▸ Düzenle)." : "Aramayı ya da kategoriyi değiştir.")));
            }
            else if (_grid)
            {
                var grid = El("tp-grid");
                foreach (var p in list) grid.Add(Card(p));
                sv.Add(grid);
            }
            else
            {
                var l = El("tp-list");
                foreach (var p in list) l.Add(ListRow(p));
                sv.Add(l);
            }
            main.Add(sv);
            body.Add(main);

            var sel = TpCatalog.Find(_selected);
            if (sel != null && (!twiceOnly || sel.IsTwice)) body.Add(BuildDetail(sel));
            return body;
        }

        string SideTitle()
        {
            switch (_cat)
            {
                case CatAll: return "Tüm paketler";
                case CatInstalled: return "Bu projede yüklü";
                case CatUpdates: return "Güncelleme var";
                case CatFav: return "Favoriler";
                case CatMine: return "Benim yüklediklerim";
                default: return string.IsNullOrEmpty(_sub) ? _cat : _cat + " › " + _sub;
            }
        }

        VisualElement BuildSidebar(bool twiceOnly)
        {
            var side = new ScrollView(ScrollViewMode.Vertical) { name = "side" };
            side.AddToClassList("tp-sidebar");
            var all = Scope(twiceOnly).ToList();
            var fav = TpSettings.Favorites;
            side.Add(SideItem(CatAll, "", "Tümü", all.Count));
            side.Add(SideItem(CatInstalled, "", "Bu projede yüklü", all.Count(p => TpInstaller.Installed(p.slug) != null)));
            int upd = all.Count(p => TpInstaller.HasUpdate(p));
            if (upd > 0) side.Add(SideItem(CatUpdates, "", "Güncelleme var", upd));
            side.Add(SideItem(CatFav, "", "Favoriler", all.Count(p => fav.Contains(p.slug))));
            if (!string.IsNullOrEmpty(TpCatalog.User)) side.Add(SideItem(CatMine, "", "Benim yüklediklerim", all.Count(p => p.createdBy == TpCatalog.User)));
            side.Add(L("KATEGORİLER", "tp-side-title"));
            // taxonomy order first, then anything the server still has under an older name
            var cats = new List<string>(TpCatalog.Categories);
            foreach (var x in all.Select(p => p.category).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                if (!cats.Any(c => string.Equals(c, x, StringComparison.OrdinalIgnoreCase))) cats.Add(x);
            foreach (var c in cats)
            {
                var inCat = all.Where(p => string.Equals(p.category, c, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inCat.Count == 0 && _cat != c) continue;
                side.Add(SideItem(c, "", c, inCat.Count));
                if (_cat != c) continue;
                // the open category lists its subcategories (only those that have packages)
                foreach (var g in inCat.Where(p => !string.IsNullOrEmpty(p.subcategory)).GroupBy(p => p.subcategory).OrderBy(g => g.Key))
                {
                    var it = SideItem(c, g.Key, g.Key, g.Count());
                    it.style.paddingLeft = 24;
                    side.Add(it);
                }
            }
            return side;
        }

        VisualElement SideItem(string key, string sub, string label, int count)
        {
            bool on = string.Equals(_cat, key, StringComparison.OrdinalIgnoreCase) && string.Equals(_sub ?? "", sub ?? "", StringComparison.OrdinalIgnoreCase);
            var e = El("tp-side-item" + (on ? " tp-side-item--active" : ""), L(label), L(count.ToString(), "tp-side-count"));
            e.RegisterCallback<ClickEvent>(_ => { _cat = key; _sub = sub ?? ""; _scroll.Remove("lib-all"); _scroll.Remove("lib-twice"); RebuildAll(); });
            return e;
        }

        VisualElement Empty(string title, string text)
        {
            return El("tp-empty", L(title, "tp-empty-title"), L(text, "tp-empty-text"));
        }

        VisualElement StatusChip(TpPackage p)
        {
            var inst = TpInstaller.Installed(p.slug);
            if (p.deprecated) return Chip("DEPRECATED", "dep");
            if (inst == null) return null;
            if (TpInstaller.HasUpdate(p)) return Chip("v" + inst.version + " → v" + p.latest, "warn");
            return Chip("yüklü", "ok");
        }

        VisualElement Card(TpPackage p)
        {
            var card = El("tp-card" + (p.slug == _selected ? " tp-card--selected" : "") + (p.deprecated ? " tp-card--deprecated" : ""));
            var img = El("tp-card-img");
            SetImage(img, p);
            card.Add(img);
            var body = El("tp-card-body");
            body.Add(L(p.DisplayName, "tp-card-title"));
            body.Add(L((string.IsNullOrEmpty(p.publisher) ? p.createdBy : p.publisher) + " · " + p.CategoryLabel, "tp-card-sub"));
            var foot = El("tp-card-foot");
            var left = Row();
            var def = p.Default;
            left.Add(Chip("v" + (def != null ? def.version : p.latest)));
            if (p.versions != null && p.versions.Count > 1) left.Add(Chip(p.versions.Count + " sürüm"));
            if (p.IsTwice) left.Add(Chip("TWICE", "twice"));
            foot.Add(left);
            var st = StatusChip(p);
            if (st != null) foot.Add(st);
            body.Add(foot);
            card.Add(body);
            card.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount == 2) { Install(p, p.Default); return; }
                Select(p);
            });
            card.tooltip = string.IsNullOrEmpty(p.description) ? p.DisplayName : p.description;
            return card;
        }

        VisualElement ListRow(TpPackage p)
        {
            var row = El("tp-list-row" + (p.deprecated ? " tp-card--deprecated" : ""));
            var th = El("tp-list-thumb");
            SetImage(th, p);
            row.Add(th);
            var mid = El("tp-grow");
            mid.Add(L(p.DisplayName, "tp-bold"));
            var def = p.Default;
            mid.Add(L((string.IsNullOrEmpty(p.publisher) ? p.createdBy : p.publisher) + " · " + p.CategoryLabel + " · " + TpFormat.Size(def != null ? def.size : 0), "tp-muted tp-small"));
            row.Add(mid);
            row.Add(Chip("v" + (def != null ? def.version : p.latest)));
            if (p.IsTwice) row.Add(Chip("TWICE", "twice"));
            var st = StatusChip(p);
            if (st != null) row.Add(st);
            var inst = TpInstaller.Installed(p.slug);
            var b = Btn(inst == null ? "İçe aktar" : TpInstaller.HasUpdate(p) ? "Güncelle" : "Yeniden", () => Install(p, p.Default), "tp-btn tp-btn--small");
            b.SetEnabled(!TpInstaller.Busy);
            row.Add(b);
            row.RegisterCallback<ClickEvent>(e => { if (e.target == b) return; Select(p); });
            return row;
        }

        string _pickedVersion;
        bool _editing;
        string _noteEditVersion;
        string _noteDraft;

        void Select(TpPackage p)
        {
            _selected = _selected == p.slug ? null : p.slug;
            _pickedVersion = null;
            _editing = false;
            _noteEditVersion = null;
            _scroll.Remove("detail");
            RebuildAll();
        }

        /* --------------------------------------------------------------- detail --- */

        VisualElement BuildDetail(TpPackage p)
        {
            var panel = El("tp-detail");
            var hero = El("tp-detail-hero");
            SetImage(hero, p);
            panel.Add(hero);
            panel.Add(Btn("✕", () => { _selected = null; RebuildAll(); }, "tp-close"));

            var sv = new ScrollView(ScrollViewMode.Vertical) { name = "detail" };
            sv.style.flexGrow = 1;
            var b = El("tp-detail-body");
            sv.Add(b);
            panel.Add(sv);

            if (_editing) { BuildEdit(b, p); return panel; }

            var chips = Row("tp-wrap");
            chips.Add(Chip(p.category ?? "Other", "accent"));
            if (!string.IsNullOrEmpty(p.subcategory)) chips.Add(Chip(p.subcategory));
            chips.Add(Chip(TpOrigin.Label(p.origin), p.IsTwice ? "twice" : ""));
            if (p.deprecated) chips.Add(Chip("DEPRECATED", "dep"));
            b.Add(chips);
            b.Add(Space(8));
            var titleRow = Row();
            titleRow.Add(L(p.DisplayName, "tp-h1 tp-grow"));
            bool fav = TpSettings.Favorites.Contains(p.slug);
            var favB = Btn(fav ? "★" : "☆", () => { TpSettings.SetFavorite(p.slug, !fav); RebuildAll(); }, "tp-icon-btn");
            favB.tooltip = fav ? "Favorilerden çıkar" : "Favorilere ekle";
            titleRow.Add(favB);
            b.Add(titleRow);
            b.Add(L((string.IsNullOrEmpty(p.publisher) ? "" : p.publisher + " · ") + p.slug, "tp-muted"));

            if (p.deprecated)
                b.Add(L("Deprecated" + (string.IsNullOrEmpty(p.deprecatedNote) ? " — yeni projelerde kullanma." : ": " + p.deprecatedNote), "tp-callout tp-callout--dep"));
            if (!string.IsNullOrEmpty(p.notes)) b.Add(L("📝  " + p.notes, "tp-callout"));

            // ---- one card per asset: default = recommended (else newest not broken), any other on demand
            var inst = TpInstaller.Installed(p.slug);
            var versions = p.versions ?? new List<TpVersion>();
            var pv = p.Find(_pickedVersion) ?? p.Default;
            b.Add(Space(14));
            if (versions.Count > 1)
            {
                var labels = versions.Select(v => VersionLabel(p, v, inst)).ToList();
                var dd = new DropdownField("Sürüm", labels, Math.Max(0, versions.FindIndex(v => pv != null && v.version == pv.version)));
                dd.AddToClassList("tp-field");
                dd.RegisterValueChangedCallback(e => { int i = labels.IndexOf(e.newValue); if (i >= 0) { _pickedVersion = versions[i].version; RebuildAll(); } });
                b.Add(dd);
            }
            if (pv != null && pv.IsBroken) b.Add(L("Bu sürüm bozuk işaretli" + (string.IsNullOrEmpty(pv.note) ? "." : ": " + pv.note), "tp-callout tp-callout--warn"));
            else if (pv != null && !string.IsNullOrEmpty(pv.note)) b.Add(L("v" + pv.version + ": " + pv.note, "tp-callout"));
            string action = pv == null ? "İçe aktar"
                : inst == null ? "İçe aktar  v" + pv.version
                : inst.version == pv.version ? "Yeniden içe aktar  v" + pv.version
                : TpSemVer.Compare(pv.version, inst.version) > 0 ? "Güncelle  v" + inst.version + " → v" + pv.version
                : "Bu sürüme geç  v" + pv.version;
            var main = Btn(action, () => Install(p, pv), "tp-btn tp-btn--primary tp-btn--block");
            main.style.height = 34;
            main.style.marginTop = 8;
            main.SetEnabled(pv != null && !TpInstaller.Busy);
            b.Add(main);
            if (inst != null)
            {
                b.Add(Space(6));
                b.Add(Btn("Projeden kaldır (v" + inst.version + ")", () =>
                {
                    string err = TpInstaller.Uninstall(p.slug);
                    SetStatus(err ?? (p.DisplayName + " kaldırıldı."), err != null && err != "Vazgeçildi.");
                }, "tp-btn tp-btn--block tp-btn--danger"));
            }
            string warn = TpInstaller.UnityWarning(pv);
            if (warn.Length > 0) b.Add(L(warn, "tp-callout tp-callout--warn"));

            if (!string.IsNullOrEmpty(p.description)) b.Add(Section("Açıklama", L(p.description, "tp-text")));
            if (p.tags != null && p.tags.Length > 0)
            {
                var t = Row("tp-wrap");
                foreach (var tag in p.tags) t.Add(Chip(tag));
                b.Add(Section("Etiketler", t));
            }

            var info = Section("Bilgi",
                KV("Kategori", p.CategoryLabel),
                KV("Yayıncı", p.publisher),
                KV("Yükleyen", p.createdBy),
                KV("Güncellendi", TpFormat.Date(p.updatedAt)),
                KV("Boyut", pv != null ? TpFormat.Size(pv.size) : null),
                KV("İndirme", p.downloads.ToString()),
                pv != null && !string.IsNullOrEmpty(pv.unity) ? KV("Unity", pv.unity + "+") : null);
            if (!string.IsNullOrEmpty(p.assetStoreUrl))
                info.Add(Btn("Asset Store sayfası ↗", () => Application.OpenURL(p.assetStoreUrl), "tp-link"));
            b.Add(info);

            if (pv != null && ((pv.dependencies != null && pv.dependencies.Length > 0) || (pv.upmDependencies != null && pv.upmDependencies.Length > 0)))
            {
                var deps = Section("Bağımlılıklar");
                if (pv.dependencies != null)
                    foreach (var d in pv.dependencies)
                    {
                        var ds = d.Split('@')[0];
                        var di = TpInstaller.Installed(ds);
                        deps.Add(L("• " + d + (di != null ? "   ✓ v" + di.version : TpCatalog.Find(ds) == null ? "   (katalogda yok)" : ""), "tp-text"));
                    }
                if (pv.upmDependencies != null) foreach (var d in pv.upmDependencies) deps.Add(L("• UPM  " + d, "tp-text"));
                b.Add(deps);
            }

            b.Add(BuildVersions(p, versions, inst));
            b.Add(BuildManage(p));
            return panel;
        }

        static string VersionLabel(TpPackage p, TpVersion v, TpInstalled inst)
        {
            string s = v.version;
            if (v.IsRecommended) s += "  ✓ önerilen";
            else if (v.version == p.latest) s += "  (en yeni)";
            if (v.IsBroken) s += "  ✗ bozuk";
            if (inst != null && inst.version == v.version) s += "  · yüklü";
            return s;
        }

        VisualElement BuildVersions(TpPackage p, List<TpVersion> versions, TpInstalled inst)
        {
            var vs = Section("Sürümler (" + versions.Count + ")");
            bool admin = CanEdit(p);
            foreach (var v in versions)
            {
                var vv = v;
                var card = El("tp-version" + (inst != null && inst.version == v.version ? " tp-version--current" : ""));
                var top = Row();
                top.Add(L("v" + v.version, "tp-bold"));
                if (v.IsRecommended) top.Add(Chip("önerilen", "ok"));
                if (v.IsBroken) top.Add(Chip("bozuk", "danger"));
                if (v.version == p.latest && !v.IsRecommended) top.Add(Chip("en yeni", "accent"));
                if (inst != null && inst.version == v.version) top.Add(Chip("yüklü"));
                top.Add(Flex());
                top.Add(L(TpFormat.Size(v.size), "tp-muted tp-small"));
                card.Add(top);
                card.Add(L(TpFormat.Date(v.uploadedAt) + " · " + v.uploadedBy, "tp-faint tp-small"));
                if (!string.IsNullOrEmpty(v.changelog)) card.Add(L(v.changelog, "tp-text tp-small"));
                if (!string.IsNullOrEmpty(v.note) && _noteEditVersion != v.version) card.Add(L("📝 " + v.note, "tp-text tp-small"));

                if (admin && _noteEditVersion == v.version)
                {
                    var f = Field(null, _noteDraft, s => _noteDraft = s, true);
                    card.Add(f);
                    card.Add(Row("", Btn("Notu kaydet", () => Run(UpdateVersion(p, vv, _noteDraft ?? "", null)), "tp-btn tp-btn--small tp-btn--primary"),
                        Btn("Vazgeç", () => { _noteEditVersion = null; RebuildAll(); }, "tp-btn tp-btn--small")));
                }
                else if (admin)
                {
                    var acts = Row("tp-wrap");
                    acts.style.marginTop = 4;
                    acts.Add(Btn(string.IsNullOrEmpty(v.note) ? "Not ekle" : "Notu düzenle", () => { _noteEditVersion = vv.version; _noteDraft = vv.note; RebuildAll(); }, "tp-link tp-small"));
                    acts.Add(L("  ·  ", "tp-faint tp-small"));
                    acts.Add(Btn(v.IsRecommended ? "Önerilen kaldır" : "Önerilen yap", () => Run(UpdateVersion(p, vv, null, vv.IsRecommended ? "" : "recommended")), "tp-link tp-small"));
                    acts.Add(L("  ·  ", "tp-faint tp-small"));
                    acts.Add(Btn(v.IsBroken ? "Bozuk değil" : "Bozuk işaretle", () => Run(UpdateVersion(p, vv, null, vv.IsBroken ? "" : "broken")), "tp-link tp-small"));
                    if (versions.Count > 1)
                    {
                        acts.Add(L("  ·  ", "tp-faint tp-small"));
                        acts.Add(Btn("Sil", () => Run(DeleteVersion(p, vv)), "tp-link tp-small"));
                    }
                    card.Add(acts);
                }
                vs.Add(card);
            }
            return vs;
        }

        VisualElement BuildManage(TpPackage p)
        {
            var manage = Section("Yönet");
            manage.Add(Row("tp-wrap", Btn("Yeni sürüm yükle", () => StartNewVersion(p), "tp-btn tp-btn--small"),
                CanEdit(p) ? Btn("Düzenle", () => { _editing = true; RebuildAll(); }, "tp-btn tp-btn--small") : null,
                CanEdit(p) ? Btn(p.deprecated ? "Deprecated kaldır" : "Deprecated yap", () => Run(ToggleDeprecated(p)), "tp-btn tp-btn--small") : null));
            if (!TpCatalog.IsSuper) return manage;
            var others = TpCatalog.Packages.Where(x => x.slug != p.slug).OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            if (others.Count > 0)
            {
                manage.Add(Space(10));
                manage.Add(L("Bu paket aslında başka bir paketin aynısıysa onunla birleştir (sürümler taşınır, bu paket silinir, adı takma ad olarak kalır):", "tp-text tp-muted tp-small"));
                var names = others.Select(x => x.DisplayName + "  (" + x.slug + ")").ToList();
                string target = null;
                var dd = new DropdownField(names, -1);
                dd.style.flexGrow = 1;
                dd.RegisterValueChangedCallback(e => { int i = names.IndexOf(e.newValue); target = i >= 0 ? others[i].slug : null; });
                manage.Add(Row("", dd, Btn("Birleştir", () => { if (target != null) Run(Merge(p, target)); }, "tp-btn tp-btn--small")));
            }
            manage.Add(Space(10));
            manage.Add(Btn("Paketi sunucudan sil", () => Run(DeletePackage(p)), "tp-btn tp-btn--small tp-btn--danger"));
            return manage;
        }

        /* ---- edit (in the detail panel) ---- */

        TpMetaUpdateRequest _edit;
        string _editTags;

        static TpMetaUpdateRequest MetaOf(TpPackage p)
        {
            return new TpMetaUpdateRequest
            {
                slug = p.slug, name = p.name, category = p.category, subcategory = p.subcategory,
                origin = string.IsNullOrEmpty(p.origin) ? TpOrigin.Other : p.origin,
                deprecated = p.deprecated, deprecatedNote = p.deprecatedNote, notes = p.notes,
                description = p.description, publisher = p.publisher, assetStoreUrl = p.assetStoreUrl, tags = p.tags
            };
        }

        void BuildEdit(VisualElement b, TpPackage p)
        {
            if (_edit == null || _edit.slug != p.slug)
            {
                _edit = MetaOf(p);
                _editTags = p.tags != null ? string.Join(", ", p.tags) : "";
            }
            b.Add(L("Düzenle", "tp-h1"));
            b.Add(L(p.slug, "tp-muted"));
            b.Add(Space(10));
            b.Add(Field("Ad", _edit.name, v => _edit.name = v));
            b.Add(Dropdown("Kategori", TpCatalog.Categories.ToList(), _edit.category, v => { _edit.category = v; _edit.subcategory = ""; RebuildAll(); }));
            var subs = new List<string> { "—" };
            subs.AddRange(TpCatalog.Subcategories(_edit.category));
            if (subs.Count > 1) b.Add(Dropdown("Alt kategori", subs, string.IsNullOrEmpty(_edit.subcategory) ? "—" : _edit.subcategory, v => _edit.subcategory = v == "—" ? "" : v));
            b.Add(Dropdown("Köken", TpOrigin.Labels.ToList(), TpOrigin.Label(_edit.origin), v => _edit.origin = TpOrigin.All[Array.IndexOf(TpOrigin.Labels, v)]));
            b.Add(Field("Yayıncı", _edit.publisher, v => _edit.publisher = v));
            b.Add(Field("Açıklama", _edit.description, v => _edit.description = v, true));
            b.Add(Field("Notlar", _edit.notes, v => _edit.notes = v, true));
            b.Add(Field("Etiketler", _editTags, v => _editTags = v));
            b.Add(Field("Asset Store", _edit.assetStoreUrl, v => _edit.assetStoreUrl = v));
            b.Add(Check("Deprecated", _edit.deprecated, v => _edit.deprecated = v));
            b.Add(Field("Deprecated notu", _edit.deprecatedNote, v => _edit.deprecatedNote = v));
            b.Add(Space(8));
            b.Add(Btn("Görseli değiştir…", () =>
            {
                string f = EditorUtility.OpenFilePanel("Önizleme görseli", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(f)) Run(ChangeImage(p, f));
            }, "tp-btn tp-btn--small"));
            b.Add(Space(12));
            var save = Btn("Kaydet", () => Run(SaveEdit()), "tp-btn tp-btn--primary");
            save.SetEnabled(!_busy);
            b.Add(Row("", save, Btn("Vazgeç", () => { _editing = false; _edit = null; RebuildAll(); })));
        }

        async Task SaveEdit()
        {
            _busy = true;
            _edit.tags = TpFormat.SplitList(_editTags);
            var r = await TpHub.PostJson<TpPackageResponse>("update", _edit);
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Upsert(r.Data.package);
            _editing = false;
            _edit = null;
            SetStatus("Kaydedildi.");
        }

        async Task UpdateVersion(TpPackage p, TpVersion v, string note, string status)
        {
            _busy = true;
            var req = new TpVersionUpdateRequest { slug = p.slug, version = v.version, note = note ?? v.note ?? "", status = status ?? v.status ?? "" };
            var r = await TpHub.PostJson<TpPackageResponse>("version", req);
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Upsert(r.Data.package);
            _noteEditVersion = null;
            _pickedVersion = null;
            SetStatus("v" + v.version + " güncellendi.");
        }

        async Task ToggleDeprecated(TpPackage p)
        {
            if (!p.deprecated && !EditorUtility.DisplayDialog("Twice Package Hub", p.DisplayName + " deprecated işaretlensin mi? Silinmez; listede karartılır ve toplu yüklemede tekrar önerilmez.", "Deprecated yap", "Vazgeç")) return;
            _busy = true;
            var req = MetaOf(p);
            req.deprecated = !p.deprecated;
            var r = await TpHub.PostJson<TpPackageResponse>("update", req);
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Upsert(r.Data.package);
            SetStatus(p.DisplayName + (req.deprecated ? " deprecated işaretlendi." : " artık deprecated değil."));
        }

        async Task Merge(TpPackage p, string into)
        {
            var target = TpCatalog.Find(into);
            if (target == null) return;
            if (!EditorUtility.DisplayDialog("Twice Package Hub — birleştir",
                    p.DisplayName + " → " + target.DisplayName + "\n\n" + p.DisplayName + " sürümleri " + target.DisplayName + " altına taşınır (aynı sürüm ikisinde de varsa hedefinki kalır), bu ad hedefin takma adı olur ve " + p.slug + " silinir.", "Birleştir", "Vazgeç")) return;
            _busy = true;
            var r = await TpHub.PostJson<TpPackageResponse>("merge", new TpMergeRequest { from = p.slug, into = into });
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Remove(p.slug);
            TpCatalog.Upsert(r.Data.package);
            _selected = into;
            SetStatus("Birleştirildi: " + (r.Data.moved != null ? r.Data.moved.Length : 0) + " sürüm taşındı" + (r.Data.skipped != null && r.Data.skipped.Length > 0 ? ", " + r.Data.skipped.Length + " aynı sürüm atlandı" : "") + ".");
        }

        async Task ChangeImage(TpPackage p, string file)
        {
            _busy = true;
            SetStatus("Görsel yükleniyor…");
            var r = await TpUploader.SetImage(p.slug, file);
            if (r.Key == null) { SetStatus(r.Value, true); return; }
            TpCatalog.Upsert(r.Key);
            SetStatus("Görsel güncellendi.");
        }

        async Task DeleteVersion(TpPackage p, TpVersion v)
        {
            if (!EditorUtility.DisplayDialog("Twice Package Hub", p.DisplayName + " v" + v.version + " sunucudan silinsin mi? Geri alınamaz.", "Sil", "Vazgeç")) return;
            _busy = true;
            var r = await TpHub.PostJson<TpPackageResponse>("delete_version", new TpSlugVersionRequest { slug = p.slug, version = v.version });
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Upsert(r.Data.package);
            _pickedVersion = null;
            SetStatus("v" + v.version + " silindi.");
        }

        async Task DeletePackage(TpPackage p)
        {
            if (!EditorUtility.DisplayDialog("Twice Package Hub", p.DisplayName + " ve TÜM sürümleri sunucudan silinsin mi? Projelerdeki kurulu dosyalara dokunulmaz. Geri alınamaz.\n\nSadece kullanılmasın istiyorsan Deprecated yap: silinen paket bir sonraki toplu yüklemede yeniden önerilir.", "Sil", "Vazgeç")) return;
            _busy = true;
            var r = await TpHub.PostJson<TpApiBase>("delete", new TpSlugVersionRequest { slug = p.slug });
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Remove(p.slug);
            _selected = null;
            SetStatus(p.DisplayName + " silindi.");
        }

        /* --------------------------------------------------------------- install --- */

        void Install(TpPackage p, TpVersion v)
        {
            if (p == null || v == null) return;
            if (p.deprecated && !EditorUtility.DisplayDialog("Twice Package Hub", p.DisplayName + " deprecated" + (string.IsNullOrEmpty(p.deprecatedNote) ? "." : ": " + p.deprecatedNote) + "\n\nYine de içe aktarılsın mı?", "İçe aktar", "Vazgeç")) return;
            if (v.IsBroken && !EditorUtility.DisplayDialog("Twice Package Hub", p.DisplayName + " v" + v.version + " bozuk işaretli" + (string.IsNullOrEmpty(v.note) ? "." : ": " + v.note) + "\n\nYine de içe aktarılsın mı?", "İçe aktar", "Vazgeç")) return;
            Run(InstallAsync(new List<KeyValuePair<TpPackage, TpVersion>> { new KeyValuePair<TpPackage, TpVersion>(p, v) }));
        }

        async Task InstallAsync(List<KeyValuePair<TpPackage, TpVersion>> roots)
        {
            _busy = true;
            SetStatus("Hazırlanıyor…");
            string err = await TpInstaller.InstallMany(roots);
            if (err == "Vazgeçildi.") SetStatus("");
            else SetStatus(err ?? "İçe aktarılıyor… (Unity derlemesi bitince tamamlanır)", err != null);
        }
    }
}
