using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace TwiceSDK.PackageManager
{
    /// <summary>Twice ▸ Twice Package Hub — browse, install, publish.</summary>
    public sealed partial class TpWindow : EditorWindow
    {
        enum Tab { Browse = 0, Installed = 1, Upload = 2, Settings = 3, Bulk = 4 }
        enum Sort { Name = 0, Newest = 1, Downloads = 2 }
        static readonly string[] SortLabels = { "Ad", "En yeni", "En çok indirilen" };

        // sidebar pseudo-categories
        const string CatAll = "\u0001all", CatInstalled = "\u0001inst", CatUpdates = "\u0001upd", CatFav = "\u0001fav", CatMine = "\u0001mine";

        const float SideW = 190f, DetailW = 340f, CardW = 176f, CardH = 214f;

        Tab _tab;
        string _search = "";
        string _cat = CatAll;
        Sort _sort;
        bool _grid = true;
        Vector2 _sideScroll, _listScroll, _detailScroll, _tabScroll;
        string _selected;
        string _status = "";
        bool _statusError;
        bool _busy;
        int _versionPick;
        bool _showVersions;

        // edit
        bool _editing;
        TpMetaUpdateRequest _edit;
        string _editTags;

        // upload form
        bool _upNew = true;
        int _upTarget;
        bool _upFromFile;
        string _upFile = "";
        List<string> _upPaths = new List<string>();
        bool _upIncludeDeps;
        string _upName = "", _upSlug = "", _upVersion = "1.0.0", _upCategory = "", _upPublisher = "", _upDesc = "",
            _upTags = "", _upStoreUrl = "", _upUnity = "", _upChangelog = "", _upDeps = "", _upUpm = "", _upImage = "";
        bool _upSlugTouched;
        Texture2D _upImagePreview;

        // settings
        string _tokenField;
        string _me = "";

        GUIStyle _h1, _h2, _wrap, _mini, _miniWrap, _card, _cardSel, _badge, _side, _sideSel, _center;

        [MenuItem("Twice/Twice Package Hub", false, 0)]
        public static void Open()
        {
            var w = GetWindow<TpWindow>();
            w.titleContent = new GUIContent("Twice Package Hub", EditorGUIUtility.IconContent("Prefab Icon").image);
            w.minSize = new Vector2(900, 520);
            w.Show();
        }

        void OnEnable()
        {
            _tab = (Tab)TpSettings.GetInt("Tab", 0);
            _sort = (Sort)TpSettings.GetInt("Sort", 0);
            _grid = TpSettings.GetInt("Grid", 1) == 1;
            _tokenField = TpSettings.Token;
            if (string.IsNullOrEmpty(_upUnity)) _upUnity = UnityMajorMinor();
            TpCatalog.Changed += Repaint;
            TpInstaller.Changed += Repaint;
            if (TpSettings.HasToken) Run(RefreshCatalog(false));
            else _tab = Tab.Settings;
        }

        void OnDisable()
        {
            TpCatalog.Changed -= Repaint;
            TpInstaller.Changed -= Repaint;
            TpSettings.SetInt("Tab", (int)_tab);
            TpSettings.SetInt("Sort", (int)_sort);
            TpSettings.SetInt("Grid", _grid ? 1 : 0);
        }

        /* ============================================================ helpers === */

        static string UnityMajorMinor()
        {
            var p = Application.unityVersion.Split('.');
            return p.Length >= 2 ? p[0] + "." + p[1] : Application.unityVersion;
        }

        void SetStatus(string msg, bool error = false)
        {
            _status = msg ?? "";
            _statusError = error;
            if (error && !string.IsNullOrEmpty(msg)) TpLog.Warn(msg);
            Repaint();
        }

        /// <summary>Runs an async UI action; exceptions end up in the status bar instead of vanishing.</summary>
        async void Run(Task t)
        {
            try { await t; }
            catch (Exception e) { SetStatus(e.Message, true); Debug.LogException(e); }
            finally { _busy = false; Repaint(); }
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

        void EnsureStyles()
        {
            if (_h1 != null) return;
            _h1 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, wordWrap = true };
            _h2 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _wrap = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
            _mini = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            _miniWrap = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            _card = new GUIStyle("HelpBox") { padding = new RectOffset(8, 8, 8, 8), margin = new RectOffset(4, 4, 4, 4) };
            _cardSel = new GUIStyle(_card);
            _cardSel.normal.background = MakeTex(new Color(0.24f, 0.45f, 0.75f, 0.35f));
            _badge = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(5, 5, 1, 1) };
            _badge.normal.textColor = Color.white;
            _side = new GUIStyle(EditorStyles.label) { padding = new RectOffset(10, 6, 3, 3), fixedHeight = 22 };
            _sideSel = new GUIStyle(_side) { fontStyle = FontStyle.Bold };
            _sideSel.normal.background = MakeTex(new Color(0.24f, 0.45f, 0.75f, 0.45f));
            _center = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
        }

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void Badge(string text, Color c, params GUILayoutOption[] opts)
        {
            var r = GUILayoutUtility.GetRect(new GUIContent(text), _badge, opts);
            EditorGUI.DrawRect(r, c);
            GUI.Label(r, text, _badge);
        }

        static readonly Color Green = new Color(0.18f, 0.55f, 0.3f), Orange = new Color(0.8f, 0.45f, 0.1f), Blue = new Color(0.24f, 0.45f, 0.75f), Grey = new Color(0.35f, 0.35f, 0.35f);

        /* ============================================================== GUI === */

        void OnGUI()
        {
            EnsureStyles();
            if (!TpSettings.HasToken)
            {
                // Not connected: nothing else is reachable — no catalog, no tabs.
                DrawConnect();
                DrawStatusBar();
                return;
            }
            DrawToolbar();
            switch (_tab)
            {
                case Tab.Browse: DrawBrowse(); break;
                case Tab.Installed: DrawInstalled(); break;
                case Tab.Upload: DrawUpload(); break;
                case Tab.Bulk: DrawBulk(); break;
                default: DrawSettings(); break;
            }
            DrawStatusBar();
        }

        /* =========================================================== connect === */

        string _connCode, _connPoll, _connUrl;
        DateTime _connUntil;
        TpCancel _connCancel;
        bool _connManual;

        void DrawConnect()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.Width(460));
            GUILayout.Label("Twice Packages", _h1);
            GUILayout.Label("Bu bilgisayar twicehub'a bağlı değil. Paketleri görmek için twicehub hesabınla bağla — tarayıcıda onay ekranı açılır, Onayla'ya basınca bağlantı bu bilgisayara kendiliğinden gelir.", _wrap);
            GUILayout.Space(10);

            if (_connCode == null)
            {
                var bg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
                GUI.enabled = !_busy;
                if (GUILayout.Button("Twicehub ile bağlan", GUILayout.Height(34))) Run(Connect());
                GUI.enabled = true;
                GUI.backgroundColor = bg;
            }
            else
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("Tarayıcıda onay bekleniyor. Panelde gördüğün kod bununla aynı olmalı:", _miniWrap);
                var big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label(_connCode, big, GUILayout.Height(36));
                int left = Math.Max(0, (int)(_connUntil - DateTime.UtcNow).TotalSeconds);
                GUILayout.Label("Kalan süre: " + (left / 60) + ":" + (left % 60).ToString("00"), _center);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Onay sayfasını yeniden aç")) Application.OpenURL(_connUrl);
                if (GUILayout.Button("Vazgeç") && _connCancel != null) _connCancel.Requested = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                Repaint();
            }

            GUILayout.Space(14);
            string hub = EditorGUILayout.TextField("Hub adresi", TpSettings.HubUrl);
            if (hub != TpSettings.HubUrl) { TpSettings.HubUrl = hub; TpCatalog.ResetForHubChange(); }
            _connManual = EditorGUILayout.Foldout(_connManual, "Otomatik bağlanma çalışmazsa: token'ı elle yapıştır", true);
            if (_connManual)
            {
                GUILayout.Label("Panel ▸ Diğer ▸ Twice Packages ▸ 'Otomatik bağlanma çalışmazsa' ▸ Token üret.", _miniWrap);
                EditorGUILayout.BeginHorizontal();
                _tokenField = EditorGUILayout.PasswordField(_tokenField ?? "");
                if (GUILayout.Button("Bağlan", GUILayout.Width(70)) && !string.IsNullOrEmpty(_tokenField))
                {
                    TpSettings.Token = _tokenField.Trim();
                    _tokenField = "";
                    GUI.FocusControl(null);
                    Run(TestConnection());
                }
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Paneli aç ↗", EditorStyles.linkLabel)) Application.OpenURL(TpSettings.HubBase + "/packages.php");
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
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
                        await TestConnection();
                        _tab = Tab.Browse;
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

        void Disconnect()
        {
            if (!EditorUtility.DisplayDialog("Twice Packages", "Bu bilgisayarın bağlantısı kesilsin mi? Tekrar bağlanmak için panel onayı gerekir. (Sunucudaki kaydı tamamen silmek için: panel ▸ Twice Packages ▸ Bağlı bilgisayarlarım.)", "Bağlantıyı kes", "Vazgeç")) return;
            TpSettings.Token = "";
            TpSettings.ConnectedAs = "";
            TpCatalog.Forget();
            _selected = null;
            SetStatus("Bağlantı kesildi.");
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            int updates = TpCatalog.Packages.Count(p => TpInstaller.HasUpdate(p));
            string inst = "Yüklü (" + TpInstaller.State.installed.Count + ")" + (updates > 0 ? " • " + updates + " güncelleme" : "");
            if (GUILayout.Toggle(_tab == Tab.Browse, "Göz at", EditorStyles.toolbarButton, GUILayout.Width(70))) _tab = Tab.Browse;
            if (GUILayout.Toggle(_tab == Tab.Installed, inst, EditorStyles.toolbarButton)) _tab = Tab.Installed;
            if (GUILayout.Toggle(_tab == Tab.Upload, "Yükle", EditorStyles.toolbarButton, GUILayout.Width(60))) _tab = Tab.Upload;
            if (GUILayout.Toggle(_tab == Tab.Bulk, "Toplu yükle", EditorStyles.toolbarButton, GUILayout.Width(80))) _tab = Tab.Bulk;
            if (GUILayout.Toggle(_tab == Tab.Settings, "Ayarlar", EditorStyles.toolbarButton, GUILayout.Width(66))) _tab = Tab.Settings;
            GUILayout.FlexibleSpace();
            if (_tab == Tab.Browse)
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(230));
            GUI.enabled = !_busy && TpSettings.HasToken;
            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Kataloğu yenile"), EditorStyles.toolbarButton, GUILayout.Width(30)))
                Run(RefreshCatalog(true));
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var c = GUI.color;
            if (_statusError) GUI.color = new Color(1f, 0.55f, 0.5f);
            GUILayout.Label((_busy || TpInstaller.Busy ? "⏳ " : "") + _status, EditorStyles.miniLabel);
            GUI.color = c;
            GUILayout.FlexibleSpace();
            GUILayout.Label(TpSettings.HubBase.Replace("https://", ""), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /* ============================================================ browse === */

        void DrawBrowse()
        {
            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            EditorGUILayout.BeginVertical();
            DrawListHeader();
            DrawList();
            EditorGUILayout.EndVertical();
            var sel = TpCatalog.Find(_selected);
            if (sel != null)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(DetailW));
                DrawDetails(sel);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(SideW), GUILayout.ExpandHeight(true));
            _sideScroll = EditorGUILayout.BeginScrollView(_sideScroll);
            var all = TpCatalog.Packages;
            SideItem(CatAll, "Tümü", all.Count);
            SideItem(CatInstalled, "Bu projede yüklü", all.Count(p => TpInstaller.Installed(p.slug) != null));
            int upd = all.Count(p => TpInstaller.HasUpdate(p));
            if (upd > 0) SideItem(CatUpdates, "Güncelleme var", upd);
            var fav = TpSettings.Favorites;
            SideItem(CatFav, "★ Favoriler", all.Count(p => fav.Contains(p.slug)));
            if (!string.IsNullOrEmpty(TpCatalog.User)) SideItem(CatMine, "Benim yüklediklerim", all.Count(p => p.createdBy == TpCatalog.User));
            GUILayout.Space(8);
            GUILayout.Label("Kategoriler", EditorStyles.miniBoldLabel);
            foreach (var g in all.Where(p => !string.IsNullOrEmpty(p.category)).GroupBy(p => p.category, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
                SideItem(g.Key, g.Key, g.Count());
            int none = all.Count(p => string.IsNullOrEmpty(p.category));
            if (none > 0) SideItem("", "Kategorisiz", none);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void SideItem(string key, string label, int count)
        {
            bool on = string.Equals(_cat, key, StringComparison.OrdinalIgnoreCase);
            if (GUILayout.Button(label + "  (" + count + ")", on ? _sideSel : _side)) { _cat = key; _listScroll = Vector2.zero; }
        }

        List<TpPackage> Filtered()
        {
            var fav = TpSettings.Favorites;
            IEnumerable<TpPackage> q = TpCatalog.Packages;
            switch (_cat)
            {
                case CatAll: break;
                case CatInstalled: q = q.Where(p => TpInstaller.Installed(p.slug) != null); break;
                case CatUpdates: q = q.Where(p => TpInstaller.HasUpdate(p)); break;
                case CatFav: q = q.Where(p => fav.Contains(p.slug)); break;
                case CatMine: q = q.Where(p => p.createdBy == TpCatalog.User); break;
                default: q = q.Where(p => string.Equals(p.category ?? "", _cat, StringComparison.OrdinalIgnoreCase)); break;
            }
            string s = (_search ?? "").Trim();
            if (s.Length > 0)
            {
                q = q.Where(p => Has(p.name, s) || Has(p.slug, s) || Has(p.publisher, s) || Has(p.description, s) || Has(p.category, s) ||
                                 (p.tags != null && p.tags.Any(t => Has(t, s))));
            }
            switch (_sort)
            {
                case Sort.Newest: q = q.OrderByDescending(p => p.updatedAt ?? ""); break;
                case Sort.Downloads: q = q.OrderByDescending(p => p.downloads); break;
                default: q = q.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase); break;
            }
            return q.ToList();
        }

        static bool Has(string hay, string needle) { return !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0; }

        void DrawListHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Sırala", EditorStyles.miniLabel, GUILayout.Width(36));
            _sort = (Sort)EditorGUILayout.Popup((int)_sort, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(130));
            GUILayout.FlexibleSpace();
            if (GUILayout.Toggle(_grid, new GUIContent("▦", "Izgara"), EditorStyles.toolbarButton, GUILayout.Width(28))) _grid = true;
            if (GUILayout.Toggle(!_grid, new GUIContent("☰", "Liste"), EditorStyles.toolbarButton, GUILayout.Width(28))) _grid = false;
            EditorGUILayout.EndHorizontal();
        }

        void DrawList()
        {
            var list = Filtered();
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            if (list.Count == 0)
            {
                GUILayout.Space(40);
                GUILayout.Label(TpCatalog.Packages.Count == 0 ? "Katalog boş. İlk paketi Yükle sekmesinden gönder." : "Eşleşen paket yok.", _center);
            }
            else if (_grid)
            {
                float avail = position.width - SideW - (TpCatalog.Find(_selected) != null ? DetailW : 0f) - 30f;
                int cols = Mathf.Max(1, Mathf.FloorToInt(avail / (CardW + 8f)));
                for (int i = 0; i < list.Count; i += cols)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = i; j < Mathf.Min(i + cols, list.Count); j++) DrawCard(list[j]);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                foreach (var p in list) DrawRow(p);
            }
            EditorGUILayout.EndScrollView();
        }

        void Select(TpPackage p)
        {
            _selected = _selected == p.slug ? null : p.slug;
            _editing = false;
            _versionPick = 0;
            _detailScroll = Vector2.zero;
            GUI.FocusControl(null);
        }

        void DrawCard(TpPackage p)
        {
            bool sel = p.slug == _selected;
            var r = EditorGUILayout.BeginVertical(sel ? _cardSel : _card, GUILayout.Width(CardW), GUILayout.Height(CardH));
            var img = GUILayoutUtility.GetRect(CardW - 16, 104, GUILayout.ExpandWidth(true));
            DrawImage(img, p, ScaleMode.ScaleAndCrop);
            if (TpSettings.Favorites.Contains(p.slug)) GUI.Label(new Rect(img.xMax - 20, img.y + 2, 18, 18), "★", EditorStyles.boldLabel);
            GUILayout.Space(4);
            GUILayout.Label(p.DisplayName, EditorStyles.boldLabel, GUILayout.MaxWidth(CardW - 16));
            GUILayout.Label((string.IsNullOrEmpty(p.publisher) ? p.createdBy : p.publisher) + " · v" + p.latest, _mini, GUILayout.MaxWidth(CardW - 16));
            GUILayout.Label(TpFormat.Size(p.Latest != null ? p.Latest.size : 0) + " · " + p.downloads + " indirme", _mini, GUILayout.MaxWidth(CardW - 16));
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            StatusBadge(p);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                Select(p);
                if (Event.current.clickCount == 2) Install(p, p.Latest);
                Event.current.Use();
            }
        }

        void DrawRow(TpPackage p)
        {
            bool sel = p.slug == _selected;
            var r = EditorGUILayout.BeginHorizontal(sel ? _cardSel : _card, GUILayout.Height(52));
            var img = GUILayoutUtility.GetRect(64, 40, GUILayout.Width(64), GUILayout.Height(40));
            DrawImage(img, p, ScaleMode.ScaleAndCrop);
            EditorGUILayout.BeginVertical();
            GUILayout.Label(p.DisplayName + "   v" + p.latest, EditorStyles.boldLabel);
            GUILayout.Label((string.IsNullOrEmpty(p.category) ? "" : p.category + " · ") + (string.IsNullOrEmpty(p.publisher) ? p.createdBy : p.publisher) +
                            " · " + TpFormat.Size(p.Latest != null ? p.Latest.size : 0) + " · " + p.downloads + " indirme", _mini);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            StatusBadge(p);
            GUI.enabled = !TpInstaller.Busy;
            var inst = TpInstaller.Installed(p.slug);
            string label = inst == null ? "İçe aktar" : TpInstaller.HasUpdate(p) ? "Güncelle" : "Yeniden";
            if (GUILayout.Button(label, GUILayout.Width(80), GUILayout.Height(22))) Install(p, p.Latest);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                Select(p);
                Event.current.Use();
            }
        }

        void StatusBadge(TpPackage p)
        {
            var inst = TpInstaller.Installed(p.slug);
            if (inst == null) return;
            if (TpInstaller.HasUpdate(p)) Badge("v" + inst.version + " → v" + p.latest, Orange);
            else Badge("yüklü v" + inst.version, Green);
        }

        void DrawImage(Rect r, TpPackage p, ScaleMode mode)
        {
            var tex = TpCatalog.Image(p);
            if (tex != null) GUI.DrawTexture(r, tex, mode);
            else
            {
                EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.8f, 0.8f, 0.8f));
                var icon = EditorGUIUtility.IconContent("Prefab Icon");
                if (icon != null && icon.image != null)
                {
                    float s = Mathf.Min(32, r.height - 8);
                    GUI.DrawTexture(new Rect(r.center.x - s / 2, r.center.y - s / 2, s, s), icon.image, ScaleMode.ScaleToFit, true, 0, new Color(1, 1, 1, 0.35f), 0, 0);
                }
            }
        }

        /* ----------------------------------------------------------- details --- */

        void DrawDetails(TpPackage p)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool fav = TpSettings.Favorites.Contains(p.slug);
            if (GUILayout.Button(new GUIContent(fav ? "★" : "☆", fav ? "Favorilerden çıkar" : "Favorilere ekle"), EditorStyles.label, GUILayout.Width(18)))
                TpSettings.SetFavorite(p.slug, !fav);
            if (GUILayout.Button("×", EditorStyles.label, GUILayout.Width(14))) { _selected = null; EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); return; }
            EditorGUILayout.EndHorizontal();

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            var img = GUILayoutUtility.GetRect(DetailW - 30, 170, GUILayout.ExpandWidth(true));
            DrawImage(img, p, ScaleMode.ScaleToFit);
            GUILayout.Space(6);

            if (_editing) DrawEditForm(p);
            else DrawInfo(p);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawInfo(TpPackage p)
        {
            GUILayout.Label(p.DisplayName, _h1);
            GUILayout.Label(p.slug + " · v" + p.latest, EditorStyles.miniLabel);
            GUILayout.Space(6);

            var inst = TpInstaller.Installed(p.slug);
            var latest = p.Latest;
            GUI.enabled = !TpInstaller.Busy && latest != null;
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
            string primary = inst == null ? "İçe aktar  v" + p.latest
                : TpInstaller.HasUpdate(p) ? "Güncelle  v" + inst.version + " → v" + p.latest
                : "Yeniden içe aktar  v" + p.latest;
            if (GUILayout.Button(primary, GUILayout.Height(28))) Install(p, latest);
            GUI.backgroundColor = bg;
            if (inst != null)
            {
                if (GUILayout.Button("Projeden kaldır (v" + inst.version + ")", GUILayout.Height(22)))
                {
                    string err = TpInstaller.Uninstall(p.slug);
                    SetStatus(err ?? (p.DisplayName + " kaldırıldı."), err != null && err != "Vazgeçildi.");
                }
            }
            if (p.versions != null && p.versions.Count > 1)
            {
                EditorGUILayout.BeginHorizontal();
                var labels = p.versions.Select(v => v.version + (inst != null && inst.version == v.version ? "  (yüklü)" : "")).ToArray();
                _versionPick = Mathf.Clamp(_versionPick, 0, labels.Length - 1);
                _versionPick = EditorGUILayout.Popup(_versionPick, labels);
                if (GUILayout.Button("Bu sürümü kur", GUILayout.Width(100))) Install(p, p.versions[_versionPick]);
                EditorGUILayout.EndHorizontal();
            }
            GUI.enabled = true;
            string warn = TpInstaller.UnityWarning(latest);
            if (warn.Length > 0) EditorGUILayout.HelpBox(warn, MessageType.Warning);

            GUILayout.Space(8);
            KV("Kategori", string.IsNullOrEmpty(p.category) ? "-" : p.category);
            KV("Yayıncı", string.IsNullOrEmpty(p.publisher) ? "-" : p.publisher);
            KV("Yükleyen", p.createdBy);
            KV("Güncellendi", TpFormat.Date(p.updatedAt));
            KV("Boyut", latest != null ? TpFormat.Size(latest.size) : "-");
            KV("İndirme", p.downloads.ToString());
            if (latest != null && !string.IsNullOrEmpty(latest.unity)) KV("Unity", latest.unity + "+");
            if (!string.IsNullOrEmpty(p.assetStoreUrl) && GUILayout.Button("Asset Store sayfası ↗", EditorStyles.linkLabel)) Application.OpenURL(p.assetStoreUrl);

            if (!string.IsNullOrEmpty(p.description))
            {
                GUILayout.Space(8);
                GUILayout.Label(p.description, _wrap);
            }
            if (p.tags != null && p.tags.Length > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label(string.Join("  ·  ", p.tags), _miniWrap);
            }

            if (latest != null && ((latest.dependencies != null && latest.dependencies.Length > 0) || (latest.upmDependencies != null && latest.upmDependencies.Length > 0)))
            {
                GUILayout.Space(8);
                GUILayout.Label("Bağımlılıklar", _h2);
                if (latest.dependencies != null)
                    foreach (var d in latest.dependencies)
                    {
                        var ds = d.Split('@')[0];
                        var di = TpInstaller.Installed(ds);
                        GUILayout.Label("• " + d + (di != null ? "  ✓ v" + di.version : TpCatalog.Find(ds) == null ? "  (katalogda yok)" : ""), _miniWrap);
                    }
                if (latest.upmDependencies != null)
                    foreach (var d in latest.upmDependencies) GUILayout.Label("• UPM: " + d, _miniWrap);
            }

            GUILayout.Space(8);
            _showVersions = EditorGUILayout.Foldout(_showVersions, "Sürümler (" + (p.versions != null ? p.versions.Count : 0) + ")", true);
            if (_showVersions && p.versions != null)
            {
                foreach (var v in p.versions)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("v" + v.version, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(TpFormat.Size(v.size), EditorStyles.miniLabel);
                    if (CanEdit(p) && p.versions.Count > 1 && GUILayout.Button(new GUIContent("Sil", "Bu sürümü sunucudan sil"), EditorStyles.miniButton, GUILayout.Width(32)))
                        Run(DeleteVersion(p, v));
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Label(TpFormat.Date(v.uploadedAt) + " · " + v.uploadedBy + " · " + v.downloads + " indirme", _mini);
                    if (!string.IsNullOrEmpty(v.changelog)) GUILayout.Label(v.changelog, _miniWrap);
                    EditorGUILayout.EndVertical();
                }
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Yeni sürüm yükle")) StartNewVersion(p);
            if (CanEdit(p))
            {
                GUILayout.Space(4);
                GUILayout.Label("Yönet", _h2);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Düzenle")) BeginEdit(p);
                EditorGUILayout.EndHorizontal();
                var bg2 = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.5f, 0.45f);
                if (GUILayout.Button("Paketi sunucudan sil")) Run(DeletePackage(p));
                GUI.backgroundColor = bg2;
            }
        }

        void KV(string k, string v)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(k, EditorStyles.miniBoldLabel, GUILayout.Width(80));
            GUILayout.Label(v ?? "-", _miniWrap);
            EditorGUILayout.EndHorizontal();
        }

        void Install(TpPackage p, TpVersion v)
        {
            if (p == null || v == null) return;
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

        async Task DeleteVersion(TpPackage p, TpVersion v)
        {
            if (!EditorUtility.DisplayDialog("Twice Packages", p.DisplayName + " v" + v.version + " sunucudan silinsin mi? Geri alınamaz.", "Sil", "Vazgeç")) return;
            _busy = true;
            var r = await TpHub.PostJson<TpPackageResponse>("delete_version", new TpSlugVersionRequest { slug = p.slug, version = v.version });
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Upsert(r.Data.package);
            SetStatus("v" + v.version + " silindi.");
        }

        async Task DeletePackage(TpPackage p)
        {
            if (!EditorUtility.DisplayDialog("Twice Packages", p.DisplayName + " ve TÜM sürümleri sunucudan silinsin mi? Projelerdeki kurulu dosyalara dokunulmaz. Geri alınamaz.", "Sil", "Vazgeç")) return;
            _busy = true;
            var r = await TpHub.PostJson<TpApiBase>("delete", new TpSlugVersionRequest { slug = p.slug });
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Remove(p.slug);
            _selected = null;
            SetStatus(p.DisplayName + " silindi.");
        }

        /* ------------------------------------------------------------- edit --- */

        void BeginEdit(TpPackage p)
        {
            _edit = new TpMetaUpdateRequest
            {
                slug = p.slug, name = p.name, category = p.category, description = p.description,
                publisher = p.publisher, assetStoreUrl = p.assetStoreUrl
            };
            _editTags = p.tags != null ? string.Join(", ", p.tags) : "";
            _editing = true;
        }

        void DrawEditForm(TpPackage p)
        {
            GUILayout.Label("Düzenle — " + p.slug, _h2);
            _edit.name = EditorGUILayout.TextField("Ad", _edit.name);
            _edit.category = CategoryField(_edit.category);
            _edit.publisher = EditorGUILayout.TextField("Yayıncı", _edit.publisher);
            GUILayout.Label("Açıklama", EditorStyles.miniBoldLabel);
            _edit.description = EditorGUILayout.TextArea(_edit.description ?? "", _wrapArea, GUILayout.MinHeight(70));
            _editTags = EditorGUILayout.TextField("Etiketler", _editTags);
            _edit.assetStoreUrl = EditorGUILayout.TextField("Asset Store", _edit.assetStoreUrl);
            GUILayout.Space(4);
            if (GUILayout.Button("Görseli değiştir…"))
            {
                string f = EditorUtility.OpenFilePanel("Önizleme görseli", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(f)) Run(ChangeImage(p, f));
            }
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Vazgeç")) _editing = false;
            GUI.enabled = !_busy && !string.IsNullOrEmpty(_edit.name);
            if (GUILayout.Button("Kaydet")) Run(SaveEdit());
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        GUIStyle _wrapAreaStyle;
        GUIStyle _wrapArea { get { return _wrapAreaStyle ?? (_wrapAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true }); } }

        async Task SaveEdit()
        {
            _busy = true;
            _edit.tags = TpFormat.SplitList(_editTags);
            var r = await TpHub.PostJson<TpPackageResponse>("update", _edit);
            if (!r.Ok) { SetStatus(r.Error, true); return; }
            TpCatalog.Upsert(r.Data.package);
            _editing = false;
            SetStatus("Kaydedildi.");
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

        string CategoryField(string current)
        {
            var cats = TpCatalog.Packages.Where(x => !string.IsNullOrEmpty(x.category)).Select(x => x.category)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            EditorGUILayout.BeginHorizontal();
            current = EditorGUILayout.TextField("Kategori", current ?? "");
            if (cats.Count > 0)
            {
                int pick = EditorGUILayout.Popup(-1, cats.ToArray(), GUILayout.Width(22));
                if (pick >= 0) { current = cats[pick]; GUI.FocusControl(null); }
            }
            EditorGUILayout.EndHorizontal();
            return current;
        }

        /* ========================================================= installed === */

        void DrawInstalled()
        {
            var state = TpInstaller.State.installed;
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Bu projede " + state.Count + " Twice paketi · kayıt: " + TpInstaller.StatePath, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            var updates = TpCatalog.Packages.Where(p => TpInstaller.HasUpdate(p)).ToList();
            GUI.enabled = updates.Count > 0 && !TpInstaller.Busy;
            if (GUILayout.Button("Hepsini güncelle (" + updates.Count + ")", EditorStyles.toolbarButton))
                Run(InstallAsync(updates.Select(p => new KeyValuePair<TpPackage, TpVersion>(p, p.Latest)).ToList()));
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            _tabScroll = EditorGUILayout.BeginScrollView(_tabScroll);
            if (state.Count == 0)
            {
                GUILayout.Space(40);
                GUILayout.Label("Bu projeye henüz Twice Packages üzerinden paket kurulmadı.", _center);
            }
            foreach (var i in state.ToList())
            {
                var p = TpCatalog.Find(i.slug);
                EditorGUILayout.BeginHorizontal(_card, GUILayout.Height(46));
                if (p != null)
                {
                    var img = GUILayoutUtility.GetRect(56, 34, GUILayout.Width(56), GUILayout.Height(34));
                    DrawImage(img, p, ScaleMode.ScaleAndCrop);
                }
                EditorGUILayout.BeginVertical();
                GUILayout.Label((p != null ? p.DisplayName : i.slug) + "   v" + i.version, EditorStyles.boldLabel);
                GUILayout.Label(TpFormat.Date(i.installedAt) + (string.IsNullOrEmpty(i.installedBy) ? "" : " · " + i.installedBy) + " · " + i.guids.Count + " varlık", _mini);
                EditorGUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (p == null) Badge("katalogda yok", Grey);
                else if (TpInstaller.HasUpdate(p)) Badge("v" + p.latest + " var", Orange);
                else Badge("güncel", Green);
                GUI.enabled = !TpInstaller.Busy;
                if (p != null && TpInstaller.HasUpdate(p) && GUILayout.Button("Güncelle", GUILayout.Width(74))) Install(p, p.Latest);
                if (p != null && GUILayout.Button("Detay", GUILayout.Width(52))) { _tab = Tab.Browse; _selected = p.slug; _cat = CatAll; }
                if (GUILayout.Button("Kaldır", GUILayout.Width(56)))
                {
                    string err = TpInstaller.Uninstall(i.slug);
                    SetStatus(err ?? (i.slug + " kaldırıldı."), err != null && err != "Vazgeçildi.");
                }
                if (GUILayout.Button(new GUIContent("Unut", "Kaydı siler, dosyalara dokunmaz"), GUILayout.Width(44)))
                {
                    if (EditorUtility.DisplayDialog("Twice Packages", i.slug + " kaydı silinsin mi? Dosyalar projede kalır.", "Unut", "Vazgeç")) TpInstaller.Forget(i.slug);
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        /* ============================================================ upload === */

        void StartNewVersion(TpPackage p)
        {
            _tab = Tab.Upload;
            _upNew = false;
            var editable = EditablePackages();
            _upTarget = Mathf.Max(0, editable.FindIndex(x => x.slug == p.slug));
            PrefillFrom(p);
        }

        /// <summary>Any package can get a new version (server rule); meta of others' packages is left untouched.</summary>
        List<TpPackage> EditablePackages()
        {
            return TpCatalog.Packages.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        void PrefillFrom(TpPackage p)
        {
            var l = p.Latest;
            _upName = p.name;
            _upSlug = p.slug;
            _upVersion = TpSemVer.BumpPatch(p.latest);
            _upCategory = p.category;
            _upPublisher = p.publisher;
            _upDesc = p.description;
            _upTags = p.tags != null ? string.Join(", ", p.tags) : "";
            _upStoreUrl = p.assetStoreUrl;
            _upDeps = l != null && l.dependencies != null ? string.Join(", ", l.dependencies) : "";
            _upUpm = l != null && l.upmDependencies != null ? string.Join(", ", l.upmDependencies) : "";
            _upChangelog = "";
            _upUnity = UnityMajorMinor();
        }

        void DrawUpload()
        {
            _tabScroll = EditorGUILayout.BeginScrollView(_tabScroll);
            EditorGUILayout.BeginVertical(GUILayout.MaxWidth(720));
            GUILayout.Space(6);

            int mode = GUILayout.Toolbar(_upNew ? 0 : 1, new[] { "Yeni paket", "Mevcut pakete yeni sürüm" });
            bool newMode = mode == 0;
            var editable = EditablePackages();
            if (newMode != _upNew)
            {
                _upNew = newMode;
                if (!_upNew && editable.Count > 0) PrefillFrom(editable[Mathf.Clamp(_upTarget, 0, editable.Count - 1)]);
                if (_upNew) { _upName = ""; _upSlug = ""; _upVersion = "1.0.0"; _upSlugTouched = false; _upChangelog = ""; }
            }
            GUILayout.Space(8);

            TpPackage target = null;
            if (!_upNew)
            {
                if (editable.Count == 0)
                {
                    EditorGUILayout.HelpBox("Katalogda henüz paket yok.", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndScrollView();
                    return;
                }
                int t = EditorGUILayout.Popup("Paket", Mathf.Clamp(_upTarget, 0, editable.Count - 1), editable.Select(x => x.DisplayName + "  (v" + x.latest + ")").ToArray());
                if (t != _upTarget) { _upTarget = t; PrefillFrom(editable[t]); }
                target = editable[Mathf.Clamp(_upTarget, 0, editable.Count - 1)];
            }

            // ---- source
            GUILayout.Label("Kaynak", _h2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _upFromFile = GUILayout.Toolbar(_upFromFile ? 1 : 0, new[] { "Projeden dışa aktar", ".unitypackage dosyası" }) == 1;
            if (_upFromFile)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.TextField(string.IsNullOrEmpty(_upFile) ? "(seçilmedi)" : _upFile);
                if (GUILayout.Button("Seç…", GUILayout.Width(60)))
                {
                    string f = EditorUtility.OpenFilePanel("Unity paketi", "", "unitypackage");
                    if (!string.IsNullOrEmpty(f)) _upFile = f;
                }
                EditorGUILayout.EndHorizontal();
                if (File.Exists(_upFile)) GUILayout.Label(TpFormat.Size(new FileInfo(_upFile).Length), EditorStyles.miniLabel);
            }
            else
            {
                DrawExportPaths();
            }
            EditorGUILayout.EndVertical();

            // ---- metadata
            GUILayout.Space(6);
            GUILayout.Label("Bilgiler", _h2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (_upNew)
            {
                string n = EditorGUILayout.TextField("Ad *", _upName);
                if (n != _upName) { _upName = n; if (!_upSlugTouched) _upSlug = TpFormat.Slugify(n); }
                string s = EditorGUILayout.TextField(new GUIContent("Kimlik (slug) *", "Kalıcı kimlik; bağımlılıklarda kullanılır, sonra değişmez."), _upSlug);
                if (s != _upSlug) { _upSlug = s.ToLowerInvariant(); _upSlugTouched = true; }
                if (_upSlug.Length > 0 && TpCatalog.Find(_upSlug) != null)
                    EditorGUILayout.HelpBox("Bu kimlikte bir paket zaten var. Yeni sürüm için üstten 'Mevcut pakete yeni sürüm'ü seç.", MessageType.Warning);
            }
            EditorGUILayout.BeginHorizontal();
            _upVersion = EditorGUILayout.TextField("Sürüm *", _upVersion);
            if (target != null) GUILayout.Label("şu an v" + target.latest, EditorStyles.miniLabel, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();
            if (!TpSemVer.IsValid(_upVersion)) EditorGUILayout.HelpBox("Sürüm 1.0.0 biçiminde olmalı.", MessageType.Warning);
            else if (target != null && TpSemVer.Compare(_upVersion, target.latest) <= 0) EditorGUILayout.HelpBox("Bu sürüm mevcut en yeniden (" + target.latest + ") büyük değil; yine de yüklenebilir ama 'en yeni' sayılmaz.", MessageType.None);
            _upCategory = CategoryField(_upCategory);
            _upPublisher = EditorGUILayout.TextField(new GUIContent("Yayıncı", "Asset'in asıl yapımcısı (ör. Asset Store yayıncısı)"), _upPublisher);
            GUILayout.Label("Açıklama", EditorStyles.miniBoldLabel);
            _upDesc = EditorGUILayout.TextArea(_upDesc ?? "", _wrapArea, GUILayout.MinHeight(54));
            _upTags = EditorGUILayout.TextField("Etiketler (virgülle)", _upTags);
            _upStoreUrl = EditorGUILayout.TextField("Asset Store URL", _upStoreUrl);
            _upUnity = EditorGUILayout.TextField(new GUIContent("En düşük Unity", "Örn. 2021.3 — daha eski Unity'de kurarken uyarı çıkar"), _upUnity);
            GUILayout.Label("Değişiklik notu (bu sürüm)", EditorStyles.miniBoldLabel);
            _upChangelog = EditorGUILayout.TextArea(_upChangelog ?? "", _wrapArea, GUILayout.MinHeight(40));
            EditorGUILayout.EndVertical();

            // ---- dependencies
            GUILayout.Space(6);
            GUILayout.Label("Bağımlılıklar", _h2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _upDeps = EditorGUILayout.TextField(new GUIContent("Twice paketleri", "slug ya da slug@1.2.0 (en az), virgülle"), _upDeps);
            var catSlugs = TpCatalog.Packages.Where(x => x.slug != _upSlug).Select(x => x.slug).OrderBy(x => x).ToArray();
            if (catSlugs.Length > 0)
            {
                int add = EditorGUILayout.Popup("  + ekle", -1, catSlugs);
                if (add >= 0) { _upDeps = string.Join(", ", TpFormat.SplitList(_upDeps).Concat(new[] { catSlugs[add] }).Distinct()); GUI.FocusControl(null); }
            }
            _upUpm = EditorGUILayout.TextField(new GUIContent("UPM paketleri", "com.unity.textmeshpro ya da com.unity.textmeshpro@3.0.6, virgülle"), _upUpm);
            EditorGUILayout.EndVertical();

            // ---- image
            GUILayout.Space(6);
            GUILayout.Label("Önizleme görseli", _h2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            var pr = GUILayoutUtility.GetRect(160, 90, GUILayout.Width(160), GUILayout.Height(90));
            if (_upImagePreview != null) GUI.DrawTexture(pr, _upImagePreview, ScaleMode.ScaleToFit);
            else if (target != null) DrawImage(pr, target, ScaleMode.ScaleToFit);
            else EditorGUI.DrawRect(pr, new Color(0.2f, 0.2f, 0.2f));
            EditorGUILayout.BeginVertical();
            if (GUILayout.Button("Görsel seç…", GUILayout.Width(110)))
            {
                string f = EditorUtility.OpenFilePanel("Önizleme görseli", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(f))
                {
                    _upImage = f;
                    if (_upImagePreview != null) DestroyImmediate(_upImagePreview);
                    _upImagePreview = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    _upImagePreview.LoadImage(File.ReadAllBytes(f));
                }
            }
            if (!string.IsNullOrEmpty(_upImage) && GUILayout.Button("Kaldır", GUILayout.Width(110))) { _upImage = ""; _upImagePreview = null; }
            GUILayout.Label(target != null ? "Boş bırakırsan mevcut görsel kalır." : "İsteğe bağlı. 640 px'e küçültülür.", _miniWrap);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            // ---- submit
            GUILayout.Space(10);
            string problem = UploadProblem();
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.None);
            GUI.enabled = problem == null && !TpUploader.Running && !_busy;
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
            if (GUILayout.Button(_upNew ? "Paketi yükle" : "v" + _upVersion + " sürümünü yükle", GUILayout.Height(32))) Run(DoUpload());
            GUI.backgroundColor = bg;
            GUI.enabled = true;
            GUILayout.Space(20);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        void DrawExportPaths()
        {
            GUILayout.Label("Dışa aktarılacak klasör/dosyalar (Project penceresinden sürükle ya da seçip ekle):", _miniWrap);
            var drop = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Buraya sürükle", _center);
            var ev = Event.current;
            if ((ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && drop.Contains(ev.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (ev.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var o in DragAndDrop.objectReferences) AddExportPath(AssetDatabase.GetAssetPath(o));
                }
                ev.Use();
            }
            if (GUILayout.Button("Seçili olanları ekle", GUILayout.Width(150)))
                foreach (var o in Selection.objects) AddExportPath(AssetDatabase.GetAssetPath(o));
            for (int i = 0; i < _upPaths.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(_upPaths[i], EditorStyles.miniLabel);
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) { _upPaths.RemoveAt(i); i--; }
                EditorGUILayout.EndHorizontal();
            }
            _upIncludeDeps = EditorGUILayout.ToggleLeft(new GUIContent("Bağımlı varlıkları da dahil et", "Seçilenlerin klasör dışındaki referansları (materyal, shader, script…) da pakete girer"), _upIncludeDeps);
        }

        void AddExportPath(string p)
        {
            if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets", StringComparison.Ordinal) || _upPaths.Contains(p)) return;
            _upPaths.Add(p);
            if (_upNew && string.IsNullOrEmpty(_upName)) { _upName = Path.GetFileNameWithoutExtension(p); if (!_upSlugTouched) _upSlug = TpFormat.Slugify(_upName); }
        }

        string UploadProblem()
        {
            if (_upFromFile ? !File.Exists(_upFile) : _upPaths.Count == 0) return "Kaynak seç: proje klasörü ya da .unitypackage dosyası.";
            if (_upNew && string.IsNullOrEmpty(_upName)) return "Ad gerekli.";
            if (_upNew && !System.Text.RegularExpressions.Regex.IsMatch(_upSlug ?? "", "^[a-z0-9][a-z0-9\\-]{1,62}$")) return "Kimlik: küçük harf, rakam ve tire (2-63).";
            if (_upNew && TpCatalog.Find(_upSlug) != null) return "Bu kimlik dolu.";
            if (!TpSemVer.IsValid(_upVersion)) return "Sürüm geçersiz.";
            return null;
        }

        async Task DoUpload()
        {
            _busy = true;
            var editable = EditablePackages();
            string slug = _upNew ? _upSlug : editable[Mathf.Clamp(_upTarget, 0, editable.Count - 1)].slug;
            SetStatus("Yükleniyor: " + slug + " " + _upVersion);
            var req = new TpUploader.Request
            {
                IsNewPackage = _upNew,
                Slug = slug,
                Name = _upName,
                Version = _upVersion.Trim(),
                Category = _upCategory,
                Description = _upDesc,
                Publisher = _upPublisher,
                Tags = TpFormat.SplitList(_upTags),
                AssetStoreUrl = _upStoreUrl,
                Unity = _upUnity,
                Changelog = _upChangelog,
                Dependencies = TpFormat.SplitList(_upDeps),
                UpmDependencies = TpFormat.SplitList(_upUpm),
                SourceFile = _upFromFile ? _upFile : null,
                ExportPaths = _upFromFile ? null : new List<string>(_upPaths),
                IncludeDependencies = _upIncludeDeps,
                ImagePath = _upImage
            };
            string err = await TpUploader.Upload(req);
            if (err != null) { SetStatus(err, true); EditorUtility.DisplayDialog("Twice Packages — yükleme", err, "Tamam"); return; }
            SetStatus(slug + " v" + req.Version + " yüklendi.");
            _upImage = "";
            _upImagePreview = null;
            _upChangelog = "";
            _selected = slug;
            _tab = Tab.Browse;
            _cat = CatAll;
        }

        /* ========================================================== settings === */

        void DrawSettings()
        {
            _tabScroll = EditorGUILayout.BeginScrollView(_tabScroll);
            EditorGUILayout.BeginVertical(GUILayout.MaxWidth(680));
            GUILayout.Space(8);
            GUILayout.Label("Bağlantı", _h2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string hub = EditorGUILayout.TextField("Hub adresi", TpSettings.HubUrl);
            if (hub != TpSettings.HubUrl) { TpSettings.HubUrl = hub; TpCatalog.ResetForHubChange(); }
            string who = !string.IsNullOrEmpty(_me) ? _me : !string.IsNullOrEmpty(TpSettings.ConnectedAs) ? "Bağlı: " + TpSettings.ConnectedAs : "Bağlı";
            GUILayout.Label(who + (TpSettings.UsingPlayTwiceToken ? " (bu projenin PlayTwice token'ı)" : " · " + TpSettings.MachineLabel), EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Bağlantıyı test et", GUILayout.Width(130))) Run(TestConnection());
            GUI.enabled = !TpSettings.UsingPlayTwiceToken;
            if (GUILayout.Button("Bağlantıyı kes", GUILayout.Width(110))) Disconnect();
            GUI.enabled = true;
            if (GUILayout.Button("Bağlı bilgisayarlarım ↗", GUILayout.Width(160))) Application.OpenURL(TpSettings.HubBase + "/packages.php");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("İçe aktarma", _h2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            TpSettings.InteractiveImport = EditorGUILayout.ToggleLeft(new GUIContent("Unity'nin içe aktarma penceresini göster", "Dosya dosya seçmek için. Kapalıyken paketin tamamı doğrudan içe aktarılır."), TpSettings.InteractiveImport);
            TpSettings.CleanUpdate = EditorGUILayout.ToggleLeft(new GUIContent("Güncellemede eski sürümden kalan dosyaları silmeyi öner", "Yeni sürümde olmayan dosyalar listelenir, onaylarsan silinir."), TpSettings.CleanUpdate);
            TpSettings.Verbose = EditorGUILayout.ToggleLeft("Ayrıntılı log", TpSettings.Verbose);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Kurulum kaydı: " + TpInstaller.StatePath + " (projeyle birlikte commit'le)", _miniWrap);
            if (GUILayout.Button("Yeniden oku", GUILayout.Width(90))) TpInstaller.ReloadState();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("Asset Store", _h2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            string asc = EditorGUILayout.TextField(new GUIContent("İndirme klasörü", "Unity'nin My Assets indirmelerini koyduğu yer (Preferences ▸ Package Manager'da değiştirildiyse burayı da değiştir)"), TpLocalScan.AssetStoreCache);
            if (asc != TpLocalScan.AssetStoreCache) TpSettings.SetString("AssetStoreCache", asc == TpLocalScan.DefaultAssetStoreCache ? "" : asc);
            if (GUILayout.Button("…", GUILayout.Width(24)))
            {
                string d = EditorUtility.OpenFolderPanel("Asset Store indirme klasörü", TpLocalScan.AssetStoreCache, "");
                if (!string.IsNullOrEmpty(d)) TpSettings.SetString("AssetStoreCache", d);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("Önbellek", _h2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("İndirilen paketler projeler arasında paylaşılır; aynı sürüm ikinci kez indirilmez.", _miniWrap);
            EditorGUILayout.BeginHorizontal();
            string custom = EditorGUILayout.TextField("Klasör", string.IsNullOrEmpty(TpSettings.CustomCacheDir) ? TpSettings.DefaultCacheDir : TpSettings.CustomCacheDir);
            if (custom != TpSettings.CacheDir) TpSettings.CustomCacheDir = custom == TpSettings.DefaultCacheDir ? "" : custom;
            if (GUILayout.Button("…", GUILayout.Width(24)))
            {
                string d = EditorUtility.OpenFolderPanel("Önbellek klasörü", TpSettings.CacheDir, "");
                if (!string.IsNullOrEmpty(d)) TpSettings.CustomCacheDir = d;
            }
            if (GUILayout.Button("Aç", GUILayout.Width(36))) { Directory.CreateDirectory(TpSettings.CacheDir); EditorUtility.RevealInFinder(TpSettings.CacheDir); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Boyutu hesapla", GUILayout.Width(120))) SetStatus("Önbellek: " + TpFormat.Size(TpCatalog.CacheSize()));
            if (GUILayout.Button("Önbelleği temizle", GUILayout.Width(130)) &&
                EditorUtility.DisplayDialog("Twice Packages", "İndirilmiş tüm paketler ve görseller silinsin mi? (Projelere dokunulmaz.)", "Temizle", "Vazgeç"))
            {
                TpCatalog.ClearFileCache();
                SetStatus("Önbellek temizlendi.");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
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
            if (_tab == Tab.Settings && TpCatalog.Packages.Count > 0) _tab = Tab.Browse;
        }
    }
}
