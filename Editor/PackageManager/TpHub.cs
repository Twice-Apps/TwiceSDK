using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace TwiceSDK.PackageManager
{
    public sealed class TpResult<T> where T : TpApiBase
    {
        public bool Ok;
        public string Error;
        public long Status;
        public bool NotModified;
        public T Data;
    }

    /// <summary>Cooperative cancel flag for long transfers (the request is aborted on the next tick).</summary>
    public sealed class TpCancel
    {
        public bool Requested;
    }

    /// <summary>
    /// Thin async client for hub/packages_api.php. Every request runs on the main
    /// thread (UnityWebRequest requires it); completion is detected on
    /// EditorApplication.update so awaiting code resumes on the main thread too.
    /// </summary>
    public static class TpHub
    {
        public static string Url(string op, string query = null)
        {
            return TpSettings.HubBase + "/packages_api.php?op=" + op + (string.IsNullOrEmpty(query) ? "" : "&" + query);
        }

        static void Prepare(UnityWebRequest req)
        {
            if (TpSettings.HasToken) req.SetRequestHeader("Authorization", "Bearer " + TpSettings.EffectiveToken);
            req.SetRequestHeader("X-Twice-Client", "TwiceSDK-PackageManager/1.9.0 Unity/" + Application.unityVersion);
        }

        /// <summary>Sends and resolves when done. progress receives upload or download progress (0-1).</summary>
        public static Task<UnityWebRequest> Send(UnityWebRequest req, Action<float> progress = null, TpCancel cancel = null, bool upload = false)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest>();
            Prepare(req);
            var op = req.SendWebRequest();
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (cancel != null && cancel.Requested && !op.isDone) req.Abort();
                if (progress != null) progress(upload ? req.uploadProgress : req.downloadProgress);
                if (!op.isDone) return;
                EditorApplication.update -= tick;
                tcs.TrySetResult(req);
            };
            EditorApplication.update += tick;
            return tcs.Task;
        }

        static TpResult<T> Read<T>(UnityWebRequest req) where T : TpApiBase
        {
            var r = new TpResult<T> { Status = req.responseCode };
            if (req.responseCode == 304) { r.Ok = true; r.NotModified = true; return r; }
            string text = req.downloadHandler != null ? req.downloadHandler.text : "";
            T data = null;
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
            {
                try { data = JsonUtility.FromJson<T>(text); } catch (Exception) { data = null; }
            }
            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                r.Error = "Sunucuya ulaşılamadı: " + req.error;
                return r;
            }
            if (req.responseCode == 401 && !string.IsNullOrEmpty(TpSettings.Token))
            {
                // revoked from the panel (or the account is gone): forget it, show the connect screen
                TpSettings.Token = "";
                TpSettings.ConnectedAs = "";
                TpCatalog.Forget();
            }
            if (data == null)
            {
                r.Error = "Sunucu yanıtı okunamadı (HTTP " + req.responseCode + ")" + (string.IsNullOrEmpty(req.error) ? "" : ": " + req.error);
                if (req.responseCode == 404) r.Error += ". Hub adresi doğru mu? Sunucuda packages_api.php yok olabilir.";
                return r;
            }
            r.Data = data;
            r.Ok = data.ok && req.result == UnityWebRequest.Result.Success;
            if (!r.Ok) r.Error = string.IsNullOrEmpty(data.error) ? ("HTTP " + req.responseCode) : data.error;
            return r;
        }

        public static async Task<TpResult<T>> Get<T>(string op, string query = null, Dictionary<string, string> headers = null) where T : TpApiBase
        {
            if (!TpSettings.HasToken) return new TpResult<T> { Error = "Bu bilgisayar bağlı değil: Twicehub ile bağlan." };
            using (var req = UnityWebRequest.Get(Url(op, query)))
            {
                req.timeout = 60;
                if (headers != null) foreach (var kv in headers) req.SetRequestHeader(kv.Key, kv.Value);
                await Send(req);
                return Read<T>(req);
            }
        }

        public static async Task<TpResult<T>> PostJson<T>(string op, object body, string query = null) where T : TpApiBase
        {
            bool anonymous = op == "connect_start" || op == "connect_poll";
            if (!anonymous && !TpSettings.HasToken) return new TpResult<T> { Error = "Bu bilgisayar bağlı değil: Twicehub ile bağlan." };
            byte[] bytes = Encoding.UTF8.GetBytes(body == null ? "{}" : JsonUtility.ToJson(body));
            using (var req = new UnityWebRequest(Url(op, query), UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(bytes) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 600;   // upload_finish joins + hashes up to 4 GB on the server
                await Send(req);
                return Read<T>(req);
            }
        }

        public static async Task<TpResult<T>> PostBytes<T>(string op, string query, byte[] bytes, Action<float> progress, TpCancel cancel) where T : TpApiBase
        {
            using (var req = new UnityWebRequest(Url(op, query), UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(bytes) { contentType = "application/octet-stream" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/octet-stream");
                req.timeout = 300;
                await Send(req, progress, cancel, true);
                if (cancel != null && cancel.Requested) return new TpResult<T> { Error = "İptal edildi." };
                return Read<T>(req);
            }
        }

        public static async Task<TpResult<T>> PostForm<T>(string op, List<IMultipartFormSection> form) where T : TpApiBase
        {
            using (var req = UnityWebRequest.Post(Url(op), form))
            {
                req.timeout = 120;
                await Send(req);
                return Read<T>(req);
            }
        }

        /// <summary>Streams a file to disk. Returns null on success, else an error message.</summary>
        public static async Task<string> DownloadFile(string op, string query, string destPath, Action<float> progress, TpCancel cancel)
        {
            if (!TpSettings.HasToken) return "Bu bilgisayar bağlı değil: Twicehub ile bağlan.";
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
            using (var req = new UnityWebRequest(Url(op, query), UnityWebRequest.kHttpVerbGET))
            {
                var dh = new DownloadHandlerFile(destPath) { removeFileOnAbort = true };
                req.downloadHandler = dh;
                await Send(req, progress, cancel);
                if (cancel != null && cancel.Requested) return "İptal edildi.";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string msg = req.error;
                    try
                    {
                        // Error bodies are small JSON; DownloadHandlerFile wrote them to disk.
                        if (File.Exists(destPath) && new FileInfo(destPath).Length < 4096)
                        {
                            var b = JsonUtility.FromJson<TpApiBase>(File.ReadAllText(destPath));
                            if (b != null && !string.IsNullOrEmpty(b.error)) msg = b.error;
                        }
                    }
                    catch (Exception) { }
                    try { if (File.Exists(destPath)) File.Delete(destPath); } catch (Exception) { }
                    return "İndirme başarısız (HTTP " + req.responseCode + "): " + msg;
                }
                return null;
            }
        }

        public static async Task<byte[]> DownloadBytes(string op, string query)
        {
            using (var req = UnityWebRequest.Get(Url(op, query)))
            {
                req.timeout = 60;
                await Send(req);
                return req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null;
            }
        }
    }
}
