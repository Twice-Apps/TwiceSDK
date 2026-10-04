using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using TwiceSDK;
using TwiceSDK.Analytics;
using TwiceSDK.Players;

namespace TwiceSDK.Wallet
{
    /// <summary>A grant (or deduction, when <see cref="Amount"/> is negative) queued in the Twice panel.</summary>
    public struct WalletGrant
    {
        public string Id;        // "g_…" — stable, use it to de-duplicate if you show a receipt
        public string Currency;  // the key you passed to TwiceWallet.Register (e.g. "coin")
        public double Amount;    // positive = give, negative = take away
        public string Note;      // free text the operator typed in the panel ("" if none)
        public long CreatedAt;   // unix seconds
    }

    /// <summary>
    /// Wallet: lets the Twice panel give or take a player's in-game currency, and shows the
    /// player's balances in the panel (Players → player, Wallet module).
    ///
    /// The GAME stays the owner of the balance — the panel can't reach into a save file. The
    /// game registers each currency once with a getter and an apply callback; the SDK pulls the
    /// grants queued for this player, applies them through the callback and reports the result
    /// back as analytics events (so the acknowledgement rides the offline-safe event queue).
    ///
    /// <code>
    /// TwiceWallet.Register("coin", () => Coins.Balance, delta => { Coins.Add(delta); return true; }, "Coins");
    /// </code>
    ///
    /// The currency list in the panel comes from these calls: every sync reports the registered
    /// keys (with the display name and decimals), so nothing has to be defined in the panel.
    ///
    /// Nothing happens until the first Register call. Pending grants are fetched shortly after
    /// that, on every resume (at most once a minute) and whenever you call <see cref="Sync"/>.
    /// All calls are non-blocking and never throw into game code.
    /// </summary>
    public static class TwiceWallet
    {
        internal struct Currency
        {
            public Func<double> Get;
            public Func<double, bool> Apply;
            public string Name;   // shown in the panel ("" = derived from the key)
            public int Decimals;  // 0 = whole numbers; the panel validates amounts with it
        }

        internal static readonly Dictionary<string, Currency> Currencies = new Dictionary<string, Currency>();

        /// <summary>
        /// Raised on the main thread after a grant was applied — e.g. to show
        /// "You received 100 coins". Not raised for rejected grants.
        /// </summary>
        public static event Action<WalletGrant> OnGrantApplied;

        /// <summary>
        /// Register a currency the panel may grant/remove and whose balance the panel shows.
        /// </summary>
        /// <param name="currency">Key as defined in the panel's Wallet module (e.g. "coin", "gem").</param>
        /// <param name="getBalance">Returns the current balance.</param>
        /// <param name="apply">
        /// Adds <c>delta</c> to the balance (negative = remove) and saves. Return true when
        /// applied; return false to refuse (e.g. not enough to remove) — the panel then shows the
        /// grant as rejected. Called on the main thread.
        /// </param>
        /// <param name="displayName">Name shown in the panel (e.g. "Gems"). Optional.</param>
        /// <param name="decimals">Decimal places the currency uses (0-4). 0 = whole numbers.</param>
        public static void Register(string currency, Func<double> getBalance, Func<double, bool> apply,
                                    string displayName = null, int decimals = 0)
        {
            try
            {
                if (!ValidKey(currency))
                {
                    Debug.LogWarning("[TwiceWallet] Register: currency key must be 1-32 chars of a-z, 0-9, _ — got '" + currency + "'.");
                    return;
                }
                if (getBalance == null || apply == null)
                {
                    Debug.LogWarning("[TwiceWallet] Register('" + currency + "'): getBalance and apply are required.");
                    return;
                }
                Currencies[currency] = new Currency
                {
                    Get = getBalance,
                    Apply = apply,
                    Name = displayName ?? "",
                    Decimals = Mathf.Clamp(decimals, 0, 4),
                };
                TwiceWalletRunner.EnsureExists();
                TwiceWalletRunner.Instance.ScheduleFirstSync();
            }
            catch (Exception e) { Debug.LogWarning("[TwiceWallet] " + e); }
        }

        /// <summary>Stop handling a currency (its grants stay queued in the panel).</summary>
        public static void Unregister(string currency)
        {
            if (!string.IsNullOrEmpty(currency)) Currencies.Remove(currency);
        }

        /// <summary>
        /// Fetch the grants queued for this player and apply them now (e.g. when the shop opens).
        /// <paramref name="onDone"/> receives the number of grants applied (0 on failure).
        /// </summary>
        public static void Sync(Action<int> onDone = null)
        {
            try
            {
                if (Currencies.Count == 0) { onDone?.Invoke(0); return; }
                TwiceWalletRunner.EnsureExists();
                TwiceWalletRunner.Instance.RequestSync(onDone);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceWallet] " + e);
                onDone?.Invoke(0);
            }
        }

        /// <summary>
        /// Send the current balances of every registered currency to the panel now. Normally
        /// automatic after each sync; call it after a big change if you want the panel fresh.
        /// </summary>
        public static void ReportBalances()
        {
            try
            {
                TwiceWalletRunner.EnsureExists();
                TwiceWalletRunner.Instance.ReportBalances(force: true);
            }
            catch (Exception e) { Debug.LogWarning("[TwiceWallet] " + e); }
        }

        internal static void RaiseApplied(WalletGrant g)
        {
            try { OnGrantApplied?.Invoke(g); }
            catch (Exception e) { Debug.LogWarning("[TwiceWallet] OnGrantApplied handler threw: " + e); }
        }

        static bool ValidKey(string k)
        {
            if (string.IsNullOrEmpty(k) || k.Length > 32) return false;
            foreach (char c in k)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }
    }

    /// <summary>Internal coroutine host: fetches grants, applies them, remembers what was done.</summary>
    internal class TwiceWalletRunner : MonoBehaviour
    {
        internal static TwiceWalletRunner Instance { get; private set; }

        const string DoneKey = "twice_wallet_done";        // {grantId: {s:"a"|"r", t:unix}}
        const string LastBalancesKey = "twice_wallet_last"; // {currency: value, "_t": unix}
        const int MinResyncSeconds = 60;
        const int DoneKeepDays = 45;
        const int DoneCap = 300;
        // A grant we already acknowledged but the server still lists (the ack event has not
        // reached it yet, or was dropped): after this long the ack is sent again.
        const int ReackAfterSeconds = 600;

        bool _syncing;
        bool _firstSyncScheduled;
        DateTime _lastSyncUtc = DateTime.MinValue;
        readonly List<Action<int>> _waiters = new List<Action<int>>();
        JObject _done;

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("[TwiceWallet]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            Instance = go.AddComponent<TwiceWalletRunner>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) return;
            if ((DateTime.UtcNow - _lastSyncUtc).TotalSeconds >= MinResyncSeconds) RequestSync(null);
        }

        internal void ScheduleFirstSync()
        {
            if (_firstSyncScheduled) return;
            _firstSyncScheduled = true;
            StartCoroutine(CoFirstSync());
        }

        IEnumerator CoFirstSync()
        {
            // Let the game register all its currencies in the same frame / startup sequence,
            // and wait for the analytics engine to resolve the player id.
            yield return new WaitForSecondsRealtime(1f);
            float waited = 0f;
            while (string.IsNullOrEmpty(TwicePlayers.UserId) && waited < 60f)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                waited += 0.5f;
            }
            RequestSync(null);
        }

        internal void RequestSync(Action<int> onDone)
        {
            if (onDone != null) _waiters.Add(onDone);
            if (_syncing) return;
            StartCoroutine(CoSync());
        }

        IEnumerator CoSync()
        {
            _syncing = true;
            int applied = 0;
            string uid = TwicePlayers.UserId;
            string apiKey, baseUrl;
            ResolveConfig(out apiKey, out baseUrl);

            if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(apiKey))
            {
                _lastSyncUtc = DateTime.UtcNow;
                // The registered currencies ride along: the panel's currency list is built from them.
                var cur = new JArray();
                foreach (var kv in TwiceWallet.Currencies)
                {
                    var o = new JObject { ["key"] = kv.Key, ["decimals"] = kv.Value.Decimals };
                    if (!string.IsNullOrEmpty(kv.Value.Name)) o["name"] = kv.Value.Name;
                    cur.Add(o);
                }
                string url = baseUrl + "/sdk/wallet/grants?user_id=" + UnityWebRequest.EscapeURL(uid)
                           + "&currencies=" + UnityWebRequest.EscapeURL(cur.ToString(Newtonsoft.Json.Formatting.None));
                JArray grants = null;
                using (var req = UnityWebRequest.Get(url))
                {
                    req.SetRequestHeader("X-App-Key", apiKey);
                    req.timeout = 15;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                    {
                        try { grants = JObject.Parse(req.downloadHandler.text)["grants"] as JArray; }
                        catch (Exception e) { Debug.LogWarning("[TwiceWallet] parse failed: " + e); }
                    }
                    else
                    {
                        Debug.Log("[TwiceWallet] grants fetch failed (code=" + req.responseCode + ", err=" + req.error + ").");
                    }
                }
                if (grants != null)
                {
                    foreach (var row in grants)
                    {
                        if (ApplyOne(row as JObject)) applied++;
                    }
                }
                ReportBalances(force: false);
            }

            _syncing = false;
            var ws = _waiters.ToArray();
            _waiters.Clear();
            foreach (var w in ws)
            {
                try { w(applied); } catch (Exception e) { Debug.LogWarning("[TwiceWallet] callback threw: " + e); }
            }
        }

        /// <summary>Applies one grant through the game's callback; true when applied now.</summary>
        bool ApplyOne(JObject row)
        {
            if (row == null) return false;
            var g = new WalletGrant
            {
                Id = (string)row["id"] ?? "",
                Currency = (string)row["currency"] ?? "",
                Amount = (double?)row["amount"] ?? 0d,
                Note = (string)row["note"] ?? "",
                CreatedAt = (long?)row["created_at"] ?? 0L,
            };
            if (g.Id == "" || g.Currency == "") return false;

            var done = LoadDone();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var prev = done[g.Id] as JObject;
            if (prev != null)
            {
                // Already handled on this device. Never apply twice; only repeat the ack if the
                // server still hasn't heard about it after a while.
                long t = (long?)prev["t"] ?? 0L;
                if (now - t >= ReackAfterSeconds)
                {
                    bool wasApplied = (string)prev["s"] == "a";
                    LogAck(g, wasApplied, wasApplied ? null : "rejected_by_game", BalanceOf(g.Currency));
                    prev["t"] = now;
                    SaveDone(done);
                }
                return false;
            }

            TwiceWallet.Currency c;
            if (!TwiceWallet.Currencies.TryGetValue(g.Currency, out c))
            {
                // This build doesn't know the currency (yet). Leave it queued: a later build — or a
                // Register call later in this session — can still apply it.
                return false;
            }

            bool ok;
            try { ok = c.Apply(g.Amount); }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceWallet] apply('" + g.Currency + "') threw — grant left pending: " + e);
                return false;
            }

            // Remember BEFORE acknowledging, so a crash between the two can't apply it twice.
            done[g.Id] = new JObject { ["s"] = ok ? "a" : "r", ["t"] = now };
            SaveDone(done);
            LogAck(g, ok, ok ? null : "rejected_by_game", BalanceOf(g.Currency));
            if (ok) TwiceWallet.RaiseApplied(g);
            return ok;
        }

        static void LogAck(WalletGrant g, bool applied, string reason, double? balance)
        {
            var p = new Dictionary<string, object>
            {
                { "grant_id", g.Id },
                { "currency", g.Currency },
                { "amount", g.Amount },
            };
            if (balance.HasValue) p["balance_after"] = balance.Value;
            if (!applied && reason != null) p["reason"] = reason;
            TwiceAnalytics.LogEvent(applied ? "wallet_grant_applied" : "wallet_grant_rejected", p);
        }

        static double? BalanceOf(string currency)
        {
            TwiceWallet.Currency c;
            if (!TwiceWallet.Currencies.TryGetValue(currency, out c)) return null;
            try { return c.Get(); }
            catch (Exception e) { Debug.LogWarning("[TwiceWallet] getBalance('" + currency + "') threw: " + e); return null; }
        }

        /// <summary>
        /// Logs a wallet_balance event ({currency: balance}). Unforced calls skip it when nothing
        /// changed since the last report (less than a day ago) — one event per change, not per resume.
        /// </summary>
        internal void ReportBalances(bool force)
        {
            if (TwiceWallet.Currencies.Count == 0) return;
            var p = new Dictionary<string, object>();
            var now = new JObject();
            foreach (var kv in TwiceWallet.Currencies)
            {
                double? v = BalanceOf(kv.Key);
                if (!v.HasValue) continue;
                p[kv.Key] = v.Value;
                now[kv.Key] = v.Value;
            }
            if (p.Count == 0) return;

            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!force)
            {
                try
                {
                    var last = JObject.Parse(PlayerPrefs.GetString(LastBalancesKey, "{}"));
                    long lastT = (long?)last["_t"] ?? 0L;
                    last.Remove("_t");
                    if (ts - lastT < 86400 && JToken.DeepEquals(last, now)) return;
                }
                catch { /* corrupt cache → just report */ }
            }
            TwiceAnalytics.LogEvent("wallet_balance", p);
            now["_t"] = ts;
            PlayerPrefs.SetString(LastBalancesKey, now.ToString(Newtonsoft.Json.Formatting.None));
            PlayerPrefs.Save();
        }

        JObject LoadDone()
        {
            if (_done != null) return _done;
            try { _done = JObject.Parse(PlayerPrefs.GetString(DoneKey, "{}")); }
            catch { _done = new JObject(); }
            return _done;
        }

        void SaveDone(JObject done)
        {
            // Prune: entries older than DoneKeepDays, then the oldest beyond DoneCap.
            long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - DoneKeepDays * 86400L;
            var rows = new List<KeyValuePair<string, long>>();
            foreach (var p in done.Properties())
            {
                long t = (long?)(p.Value as JObject)?["t"] ?? 0L;
                rows.Add(new KeyValuePair<string, long>(p.Name, t));
            }
            rows.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < rows.Count; i++)
            {
                if (i >= DoneCap || rows[i].Value < cutoff) done.Remove(rows[i].Key);
            }
            _done = done;
            PlayerPrefs.SetString(DoneKey, done.ToString(Newtonsoft.Json.Formatting.None));
            PlayerPrefs.Save();
        }

        static void ResolveConfig(out string apiKey, out string baseUrl)
        {
            apiKey = null; baseUrl = "https://api.twiceapps.co/v1";
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s != null)
            {
                apiKey = string.IsNullOrEmpty(s.apiKey) ? null : s.apiKey.Trim();
                if (!string.IsNullOrEmpty(s.endpointBaseUrl)) baseUrl = s.endpointBaseUrl.Trim();
            }
            baseUrl = baseUrl.TrimEnd('/');
        }
    }
}
