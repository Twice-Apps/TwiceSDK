using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// Asks the hub who an asset is: exact matches by Asset Store id / name key (rules),
    /// the rest by Claude Haiku on the server (with web search when it doesn't know the asset).
    /// Results are cached server-side per asset, so asking again costs nothing.
    /// </summary>
    public static class TpIdentify
    {
        const int Batch = 24;

        /// <summary>Returns (results in input order, null) or (partial results, error).</summary>
        public static async Task<KeyValuePair<List<TpIdentifyResult>, string>> Run(List<TpIdentifyItem> items, bool ai, Action<int, int> progress = null)
        {
            var all = new List<TpIdentifyResult>();
            for (int i = 0; i < items.Count; i += Batch)
            {
                var req = new TpIdentifyRequest { ai = ai, items = items.GetRange(i, Math.Min(Batch, items.Count - i)) };
                var r = await TpHub.PostJson<TpIdentifyResponse>("identify", req);
                if (!r.Ok) return new KeyValuePair<List<TpIdentifyResult>, string>(all, r.Error);
                all.AddRange(r.Data.items ?? new List<TpIdentifyResult>());
                if (progress != null) progress(Math.Min(items.Count, i + Batch), items.Count);
            }
            return new KeyValuePair<List<TpIdentifyResult>, string>(all, null);
        }
    }
}
