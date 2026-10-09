using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// The user's Asset Store library through Unity's own Package Manager services
    /// (UnityEditor.PackageManager.UI.Internal): list every purchase ("My Assets") and
    /// download them with Unity's downloader, which handles auth, terms, decryption
    /// and writes into the normal Asset Store cache. There is no public API for this;
    /// the members used here were checked against Unity 6000.0 and 6000.3 (identical).
    /// Everything goes through reflection and fails with a readable message if a
    /// future Unity renames something.
    /// </summary>
    public static class TpAssetStore
    {
        public sealed class Owned
        {
            public long Id;
            public string Name;
            public string PurchasedAt;
            public bool Hidden;
        }

        const string Ns = "UnityEditor.PackageManager.UI.Internal.";
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        static Type T(string name) { return typeof(EditorWindow).Assembly.GetType(Ns + name); }

        /// <summary>null when this Unity exposes what we need, else why not.</summary>
        public static string Unsupported
        {
            get
            {
                foreach (var n in new[] { "ServicesContainer", "IAssetStoreRestAPI", "IAssetStoreDownloadManager", "PurchasesQueryArgs", "PageFilters", "AssetStorePurchases" })
                    if (T(n) == null) return "Bu Unity sürümünde Asset Store servisleri bulunamadı (" + n + "). Unity 6 ile dene ya da My Assets'ten elle indir.";
                return null;
            }
        }

        public static bool LoggedIn
        {
            get
            {
                string u = CloudProjectSettings.userName;
                return !string.IsNullOrEmpty(u) && u != "anonymous";
            }
        }

        static object Service(string iface)
        {
            var sc = T("ServicesContainer");
            var instProp = sc.GetProperty("instance", All) ?? sc.BaseType.GetProperty("instance", All);
            var inst = instProp.GetValue(null, null);
            var resolve = sc.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Instance).MakeGenericMethod(T(iface));
            return resolve.Invoke(inst, null);
        }

        /// <summary>Exact Action&lt;X&gt; delegate forwarding to an Action&lt;object&gt; (X is internal to Unity).</summary>
        static Delegate Wrap(Type actionType, Action<object> a)
        {
            var arg = actionType.GetGenericArguments()[0];
            var p = Expression.Parameter(arg, "x");
            var body = Expression.Invoke(Expression.Constant(a), Expression.Convert(p, typeof(object)));
            return Expression.Lambda(actionType, body, p).Compile();
        }

        static object Field(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, All);
            if (f != null) return f.GetValue(o);
            var p = o.GetType().GetProperty(name, All);
            return p != null ? p.GetValue(o, null) : null;
        }

        static string ErrorText(object uiError)
        {
            var m = Field(uiError, "message") ?? Field(uiError, "m_Message");
            return m != null ? m.ToString() : "bilinmeyen hata";
        }

        /* ---- purchases ------------------------------------------------------------ */

        /// <summary>Every asset on the account, paged 100 at a time. Returns (list, null) or (null, error).</summary>
        public static async Task<KeyValuePair<List<Owned>, string>> ListOwned(Action<int, long> progress)
        {
            string un = Unsupported;
            if (un != null) return new KeyValuePair<List<Owned>, string>(null, un);
            if (!LoggedIn) return new KeyValuePair<List<Owned>, string>(null, "Unity hesabına giriş yapılmamış (Unity editörünün sağ üstünden giriş yap).");

            object api;
            try { api = Service("IAssetStoreRestAPI"); }
            catch (Exception e) { return new KeyValuePair<List<Owned>, string>(null, "Asset Store servisine ulaşılamadı: " + Inner(e)); }

            var get = T("IAssetStoreRestAPI").GetMethod("GetPurchases");
            var ps = get.GetParameters();
            var queryCtor = T("PurchasesQueryArgs").GetConstructor(new[] { typeof(int), typeof(int), typeof(string), T("PageFilters") });
            var filters = Activator.CreateInstance(T("PageFilters"), true);

            var all = new List<Owned>();
            long total = -1;
            const int page = 100;
            for (int start = 0; total < 0 || start < total; start += page)
            {
                var tcs = new TaskCompletionSource<object>();
                string err = null;
                var ok = Wrap(ps[1].ParameterType, r => tcs.TrySetResult(r));
                var fail = Wrap(ps[2].ParameterType, e => { err = ErrorText(e); tcs.TrySetResult(null); });
                var query = queryCtor.Invoke(new object[] { start, page, "", filters });
                try { get.Invoke(api, new object[] { query, ok, fail }); }
                catch (Exception e) { return new KeyValuePair<List<Owned>, string>(null, "GetPurchases çağrılamadı: " + Inner(e)); }

                var done = await Task.WhenAny(tcs.Task, Task.Delay(90000));
                if (done != tcs.Task) return new KeyValuePair<List<Owned>, string>(null, "Asset Store yanıt vermedi (zaman aşımı).");
                if (err != null) return new KeyValuePair<List<Owned>, string>(null, "Asset Store: " + err);
                var res = tcs.Task.Result;
                total = Convert.ToInt64(Field(res, "total") ?? 0L);
                var list = Field(res, "list") as IEnumerable;
                int got = 0;
                if (list != null)
                    foreach (var it in list)
                    {
                        got++;
                        all.Add(new Owned
                        {
                            Id = Convert.ToInt64(Field(it, "productId") ?? 0L),
                            Name = (Field(it, "displayName") ?? "").ToString(),
                            PurchasedAt = (Field(it, "purchasedTime") ?? "").ToString(),
                            Hidden = Field(it, "isHidden") is bool b && b
                        });
                    }
                if (progress != null) progress(all.Count, total);
                if (got == 0) break;
            }
            return new KeyValuePair<List<Owned>, string>(all.Where(o => o.Id > 0).GroupBy(o => o.Id).Select(g => g.First()).ToList(), null);
        }

        /* ---- downloads ------------------------------------------------------------- */

        public sealed class Progress
        {
            public int Done, Failed, Total, Active;
            public string Current = "";
            public ulong Bytes, TotalBytes;
        }

        /// <summary>
        /// Downloads the ids with Unity's downloader, `parallel` at a time. The finished file
        /// lands in the Asset Store cache. Returns id → error for the ones that failed.
        /// </summary>
        public static async Task<Dictionary<long, string>> Download(IList<Owned> items, int parallel, Action<Progress> onProgress, TpCancel cancel)
        {
            var errors = new Dictionary<long, string>();
            object dm;
            try { dm = Service("IAssetStoreDownloadManager"); }
            catch (Exception e) { foreach (var i in items) errors[i.Id] = Inner(e); return errors; }

            var tDm = T("IAssetStoreDownloadManager");
            var download = tDm.GetMethod("Download", new[] { typeof(IEnumerable<long>) });
            var getOp = tDm.GetMethod("GetDownloadOperation");
            var abort = tDm.GetMethod("AbortDownload");

            var queue = new Queue<Owned>(items);
            var active = new Dictionary<long, KeyValuePair<Owned, DateTime>>();
            var pr = new Progress { Total = items.Count };

            while (queue.Count > 0 || active.Count > 0)
            {
                if (cancel != null && cancel.Requested)
                {
                    foreach (var id in active.Keys.ToList())
                    {
                        try { abort.Invoke(dm, new object[] { (long?)id }); } catch (Exception) { }
                        errors[id] = "İptal edildi.";
                    }
                    foreach (var q in queue) errors[q.Id] = "İptal edildi.";
                    break;
                }

                while (active.Count < parallel && queue.Count > 0)
                {
                    var next = queue.Dequeue();
                    bool started = false;
                    try { started = (bool)download.Invoke(dm, new object[] { new List<long> { next.Id } }); }
                    catch (Exception e) { errors[next.Id] = Inner(e); pr.Failed++; continue; }
                    if (!started && getOp.Invoke(dm, new object[] { (long?)next.Id }) == null)
                    {
                        errors[next.Id] = "Unity indirmeyi başlatmadı (kullanım şartları onayı ya da oturum gerekebilir; Package Manager'ı aç, bir asset'i elle indir, sonra tekrar dene).";
                        pr.Failed++;
                        continue;
                    }
                    active[next.Id] = new KeyValuePair<Owned, DateTime>(next, DateTime.UtcNow);
                }

                await Task.Delay(400);

                pr.Bytes = 0; pr.TotalBytes = 0;
                foreach (var kv in active.ToList())
                {
                    var op = getOp.Invoke(dm, new object[] { (long?)kv.Key });
                    if (op == null)
                    {
                        // finalized operations are removed from the manager
                        if ((DateTime.UtcNow - kv.Value.Value).TotalSeconds > 1.5) { active.Remove(kv.Key); pr.Done++; }
                        continue;
                    }
                    string state = (Field(op, "state") ?? "").ToString();
                    if (state == "Error" || state == "Aborted")
                    {
                        string msg = (Field(op, "errorMessage") ?? state).ToString();
                        errors[kv.Key] = string.IsNullOrEmpty(msg) ? state : msg;
                        active.Remove(kv.Key);
                        pr.Failed++;
                        continue;
                    }
                    if (state == "Completed") { active.Remove(kv.Key); pr.Done++; continue; }
                    pr.Bytes += Convert.ToUInt64(Field(op, "m_DownloadedBytes") ?? 0UL);
                    pr.TotalBytes += Convert.ToUInt64(Field(op, "m_TotalBytes") ?? 0UL);
                    pr.Current = kv.Value.Key.Name + " (" + state + ")";
                }
                pr.Active = active.Count;
                if (onProgress != null) onProgress(pr);
            }
            return errors;
        }

        static string Inner(Exception e)
        {
            while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            return e.Message;
        }
    }
}
