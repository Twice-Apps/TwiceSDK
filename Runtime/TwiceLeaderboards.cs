using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using TwiceSDK;
using TwiceSDK.Players;

namespace TwiceSDK.Leaderboards
{
    /// <summary>One ranked row returned by <see cref="TwiceLeaderboards.GetTop"/>.</summary>
    public struct LeaderboardEntry
    {
        public int Rank;
        public string UserId;
        public string Name;   // display name the game submitted ("" if none)
        public double Value;
    }

    /// <summary>The current player's own standing, from <see cref="TwiceLeaderboards.GetMyRank"/>.</summary>
    public struct LeaderboardRank
    {
        public bool Found;    // is the player on the board this period?
        public int Rank;      // 1-based; 0 when not found
        public double Value;  // the player's current value
        public int Total;     // total entries on the board (this period)
        public string Name;
    }

    /// <summary>
    /// Leaderboards client. Submits a score for the current player and fetches the
    /// ranked top of a board. The board's sort direction, aggregation (last/min/max/sum)
    /// and reset frequency are configured server-side per board — the client only sends
    /// the board id + score. Player id is taken from <see cref="TwicePlayers.UserId"/>.
    /// All calls are non-blocking and never throw into game code.
    /// </summary>
    public static class TwiceLeaderboards
    {
        /// <summary>Submit a score to a board for the current player.</summary>
        /// <param name="leaderboardId">Board id as defined in the panel (e.g. "high_score").</param>
        /// <param name="score">Raw value; the board's aggregation method decides how it is applied.</param>
        /// <param name="playerName">Optional display name shown on the board (falls back to the user id).</param>
        /// <param name="onDone">Optional callback: true on success.</param>
        public static void Submit(string leaderboardId, double score, string playerName = null, Action<bool> onDone = null)
        {
            try
            {
                if (!LeaderboardsEnabled()) { onDone?.Invoke(false); return; }
                if (string.IsNullOrEmpty(leaderboardId))
                {
                    Debug.LogWarning("[TwiceLeaderboards] Submit called with empty leaderboardId.");
                    onDone?.Invoke(false);
                    return;
                }
                string apiKey, baseUrl;
                ResolveConfig(out apiKey, out baseUrl);
                if (string.IsNullOrEmpty(apiKey))
                {
                    Debug.LogWarning("[TwiceLeaderboards] No API key — skipping submit.");
                    onDone?.Invoke(false);
                    return;
                }

                var body = new JObject
                {
                    ["leaderboard_id"] = leaderboardId,
                    ["score"]          = score,
                    ["user_id"]        = TwicePlayers.UserId,
                };
                if (!string.IsNullOrEmpty(playerName)) body["player_name"] = playerName;

                TwiceLeaderboardsRunner.EnsureExists();
                TwiceLeaderboardsRunner.Instance.Post(apiKey, baseUrl, "/sdk/leaderboard/submit", body.ToString(), ok => onDone?.Invoke(ok));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceLeaderboards] " + e);
                onDone?.Invoke(false);
            }
        }

        /// <summary>Fetch the ranked top of a board (current period). count is clamped 1..1000.</summary>
        public static void GetTop(string leaderboardId, int count, Action<LeaderboardEntry[]> onResult)
        {
            try
            {
                if (!LeaderboardsEnabled()) { onResult?.Invoke(Array.Empty<LeaderboardEntry>()); return; }
                if (string.IsNullOrEmpty(leaderboardId)) { onResult?.Invoke(Array.Empty<LeaderboardEntry>()); return; }
                string apiKey, baseUrl;
                ResolveConfig(out apiKey, out baseUrl);
                if (string.IsNullOrEmpty(apiKey)) { onResult?.Invoke(Array.Empty<LeaderboardEntry>()); return; }

                count = Mathf.Clamp(count, 1, 1000);
                string url = baseUrl + "/sdk/leaderboard?board=" + UnityWebRequest.EscapeURL(leaderboardId)
                           + "&limit=" + count.ToString(CultureInfo.InvariantCulture);

                TwiceLeaderboardsRunner.EnsureExists();
                TwiceLeaderboardsRunner.Instance.GetJson(apiKey, url, onResult);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceLeaderboards] " + e);
                onResult?.Invoke(Array.Empty<LeaderboardEntry>());
            }
        }

        /// <summary>Total number of entries (players) on a board for the current period.</summary>
        public static void GetEntryCount(string leaderboardId, Action<int> onResult)
        {
            try
            {
                if (!LeaderboardsEnabled()) { onResult?.Invoke(0); return; }
                if (string.IsNullOrEmpty(leaderboardId)) { onResult?.Invoke(0); return; }
                string apiKey, baseUrl;
                ResolveConfig(out apiKey, out baseUrl);
                if (string.IsNullOrEmpty(apiKey)) { onResult?.Invoke(0); return; }

                // limit=1 keeps the payload tiny; we only read the "total" field.
                string url = baseUrl + "/sdk/leaderboard?board=" + UnityWebRequest.EscapeURL(leaderboardId) + "&limit=1";
                TwiceLeaderboardsRunner.EnsureExists();
                TwiceLeaderboardsRunner.Instance.GetObject(apiKey, url, j => onResult?.Invoke(j != null ? ((int?)j["total"] ?? 0) : 0));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceLeaderboards] " + e);
                onResult?.Invoke(0);
            }
        }

        /// <summary>The current player's own rank/value on a board (current period).</summary>
        public static void GetMyRank(string leaderboardId, Action<LeaderboardRank> onResult)
        {
            try
            {
                if (!LeaderboardsEnabled()) { onResult?.Invoke(default); return; }
                if (string.IsNullOrEmpty(leaderboardId)) { onResult?.Invoke(default); return; }
                string apiKey, baseUrl;
                ResolveConfig(out apiKey, out baseUrl);
                if (string.IsNullOrEmpty(apiKey)) { onResult?.Invoke(default); return; }

                string url = baseUrl + "/sdk/leaderboard/rank?board=" + UnityWebRequest.EscapeURL(leaderboardId)
                           + "&user_id=" + UnityWebRequest.EscapeURL(TwicePlayers.UserId);
                TwiceLeaderboardsRunner.EnsureExists();
                TwiceLeaderboardsRunner.Instance.GetObject(apiKey, url, j => onResult?.Invoke(ParseRank(j)));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceLeaderboards] " + e);
                onResult?.Invoke(default);
            }
        }

        // ---- "Before reset" variants (read the last archived period) --------

        /// <summary>Like <see cref="GetTop"/> but reads the most recently reset (archived) period.
        /// Lets you keep showing the previous standings after a reset.</summary>
        public static void GetTopBeforeReset(string leaderboardId, int count, Action<LeaderboardEntry[]> onResult)
        {
            try
            {
                if (!LeaderboardsEnabled()) { onResult?.Invoke(Array.Empty<LeaderboardEntry>()); return; }
                if (string.IsNullOrEmpty(leaderboardId)) { onResult?.Invoke(Array.Empty<LeaderboardEntry>()); return; }
                string apiKey, baseUrl;
                ResolveConfig(out apiKey, out baseUrl);
                if (string.IsNullOrEmpty(apiKey)) { onResult?.Invoke(Array.Empty<LeaderboardEntry>()); return; }

                count = Mathf.Clamp(count, 1, 1000);
                string url = baseUrl + "/sdk/leaderboard?board=" + UnityWebRequest.EscapeURL(leaderboardId)
                           + "&limit=" + count.ToString(CultureInfo.InvariantCulture) + "&period=previous";
                TwiceLeaderboardsRunner.EnsureExists();
                TwiceLeaderboardsRunner.Instance.GetJson(apiKey, url, onResult);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceLeaderboards] " + e);
                onResult?.Invoke(Array.Empty<LeaderboardEntry>());
            }
        }

        /// <summary>Like <see cref="GetMyRank"/> but for the most recently reset (archived) period.</summary>
        public static void GetMyRankBeforeReset(string leaderboardId, Action<LeaderboardRank> onResult)
        {
            try
            {
                if (!LeaderboardsEnabled()) { onResult?.Invoke(default); return; }
                if (string.IsNullOrEmpty(leaderboardId)) { onResult?.Invoke(default); return; }
                string apiKey, baseUrl;
                ResolveConfig(out apiKey, out baseUrl);
                if (string.IsNullOrEmpty(apiKey)) { onResult?.Invoke(default); return; }

                string url = baseUrl + "/sdk/leaderboard/rank?board=" + UnityWebRequest.EscapeURL(leaderboardId)
                           + "&user_id=" + UnityWebRequest.EscapeURL(TwicePlayers.UserId) + "&period=previous";
                TwiceLeaderboardsRunner.EnsureExists();
                TwiceLeaderboardsRunner.Instance.GetObject(apiKey, url, j => onResult?.Invoke(ParseRank(j)));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TwiceLeaderboards] " + e);
                onResult?.Invoke(default);
            }
        }

        static LeaderboardRank ParseRank(JObject j)
        {
            var r = new LeaderboardRank { Found = false, Rank = 0, Value = 0d, Total = 0, Name = "" };
            if (j != null)
            {
                r.Found = (bool?)j["found"] ?? false;
                r.Rank  = (int?)j["rank"] ?? 0;
                r.Value = (double?)j["value"] ?? 0d;
                r.Total = (int?)j["total"] ?? 0;
                r.Name  = (string)j["name"] ?? "";
            }
            return r;
        }

        /// <summary>Leaderboards module toggle from the settings asset (default: enabled).</summary>
        static bool LeaderboardsEnabled()
        {
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            return s == null || s.enableLeaderboards;
        }

        static void ResolveConfig(out string apiKey, out string baseUrl)
        {
            apiKey = null; baseUrl = "https://www.twiceapps.co/api/v1";
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s != null)
            {
                apiKey = string.IsNullOrEmpty(s.apiKey) ? null : s.apiKey.Trim();
                if (!string.IsNullOrEmpty(s.endpointBaseUrl)) baseUrl = s.endpointBaseUrl.Trim();
            }
            baseUrl = baseUrl.TrimEnd('/');
        }
    }

    /// <summary>Internal coroutine host for leaderboard web requests.</summary>
    internal class TwiceLeaderboardsRunner : MonoBehaviour
    {
        internal static TwiceLeaderboardsRunner Instance { get; private set; }

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("[TwiceLeaderboards]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            Instance = go.AddComponent<TwiceLeaderboardsRunner>();
        }

        internal void Post(string apiKey, string baseUrl, string path, string json, Action<bool> cb)
        {
            StartCoroutine(CoPost(apiKey, baseUrl + path, json, cb));
        }

        internal void GetJson(string apiKey, string url, Action<LeaderboardEntry[]> cb)
        {
            StartCoroutine(CoGet(apiKey, url, cb));
        }

        internal void GetObject(string apiKey, string url, Action<JObject> cb)
        {
            StartCoroutine(CoGetObject(apiKey, url, cb));
        }

        IEnumerator CoPost(string apiKey, string url, string json, Action<bool> cb)
        {
            bool ok = false;
            using (var req = new UnityWebRequest(url, "POST"))
            {
                byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-App-Key", apiKey);
                req.timeout = 15;
                yield return req.SendWebRequest();
                ok = req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
                if (!ok) Debug.Log("[TwiceLeaderboards] submit failed (code=" + req.responseCode + ", err=" + req.error + ").");
            }
            try { cb?.Invoke(ok); } catch (Exception e) { Debug.LogWarning("[TwiceLeaderboards] callback threw: " + e); }
        }

        IEnumerator CoGet(string apiKey, string url, Action<LeaderboardEntry[]> cb)
        {
            LeaderboardEntry[] entries = Array.Empty<LeaderboardEntry>();
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("X-App-Key", apiKey);
                req.timeout = 15;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                {
                    try
                    {
                        var j = JObject.Parse(req.downloadHandler.text);
                        var arr = j["entries"] as JArray;
                        if (arr != null)
                        {
                            entries = new LeaderboardEntry[arr.Count];
                            for (int i = 0; i < arr.Count; i++)
                            {
                                var row = arr[i];
                                entries[i] = new LeaderboardEntry
                                {
                                    Rank   = (int?)row["rank"] ?? (i + 1),
                                    UserId = (string)row["user_id"] ?? "",
                                    Name   = (string)row["name"] ?? "",
                                    Value  = (double?)row["value"] ?? 0d,
                                };
                            }
                        }
                    }
                    catch (Exception e) { Debug.LogWarning("[TwiceLeaderboards] parse failed: " + e); }
                }
                else
                {
                    Debug.Log("[TwiceLeaderboards] top failed (code=" + req.responseCode + ", err=" + req.error + ").");
                }
            }
            try { cb?.Invoke(entries); } catch (Exception e) { Debug.LogWarning("[TwiceLeaderboards] callback threw: " + e); }
        }

        IEnumerator CoGetObject(string apiKey, string url, Action<JObject> cb)
        {
            JObject result = null;
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("X-App-Key", apiKey);
                req.timeout = 15;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                {
                    try { result = JObject.Parse(req.downloadHandler.text); }
                    catch (Exception e) { Debug.LogWarning("[TwiceLeaderboards] parse failed: " + e); }
                }
                else
                {
                    Debug.Log("[TwiceLeaderboards] request failed (code=" + req.responseCode + ", err=" + req.error + ").");
                }
            }
            try { cb?.Invoke(result); } catch (Exception e) { Debug.LogWarning("[TwiceLeaderboards] callback threw: " + e); }
        }
    }
}
