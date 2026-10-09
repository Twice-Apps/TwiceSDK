using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace TwiceSDK.PackageManager
{
    /// <summary>
    /// Install / update / remove. What each package put into the project is recorded
    /// by guid in ProjectSettings/TwicePackages.json (versioned with the project, so
    /// teammates see the same state).
    ///
    /// Imports run through a queue persisted in SessionState: importing scripts or
    /// adding UPM packages reloads the domain, which kills any awaiting code. After a
    /// reload the [InitializeOnLoad] constructor picks the queue up and carries on.
    /// </summary>
    [InitializeOnLoad]
    public static class TpInstaller
    {
        public const string StatePath = "ProjectSettings/TwicePackages.json";
        const string QueueKey = "TwicePackages.ImportQueue";

        public static event Action Changed;

        static TpInstallState _state;

        static TpInstaller()
        {
            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageCancelled += OnImportCancelled;
            AssetDatabase.importPackageFailed += OnImportFailed;
            EditorApplication.delayCall += ResumeAfterReload;
        }

        /* ================================================== installed state === */

        public static TpInstallState State
        {
            get
            {
                if (_state == null) _state = LoadState();
                return _state;
            }
        }

        static TpInstallState LoadState()
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    var s = JsonUtility.FromJson<TpInstallState>(File.ReadAllText(StatePath));
                    if (s != null && s.installed != null) return s;
                }
            }
            catch (Exception e) { TpLog.Warn(StatePath + " okunamadı: " + e.Message); }
            return new TpInstallState();
        }

        static void SaveState()
        {
            try { File.WriteAllText(StatePath, JsonUtility.ToJson(State, true)); }
            catch (Exception e) { TpLog.Error(StatePath + " yazılamadı: " + e.Message); }
            if (Changed != null) Changed();
        }

        public static void ReloadState()
        {
            _state = null;
            if (Changed != null) Changed();
        }

        public static TpInstalled Installed(string slug)
        {
            foreach (var i in State.installed) if (i.slug == slug) return i;
            return null;
        }

        public static bool HasUpdate(TpPackage p)
        {
            var i = Installed(p.slug);
            return i != null && !string.IsNullOrEmpty(p.latest) && TpSemVer.Compare(p.latest, i.version) > 0;
        }

        /* ======================================================== planning === */

        public sealed class PlanItem
        {
            public TpPackage Package;
            public TpVersion Version;
            public bool IsDependency;
        }

        public sealed class Plan
        {
            public List<PlanItem> Items = new List<PlanItem>();
            public List<string> Upm = new List<string>();
            public List<string> Problems = new List<string>();
        }

        /// <summary>Dependencies first (post-order). Already-installed deps that satisfy the minimum are skipped.</summary>
        public static Plan Resolve(IList<KeyValuePair<TpPackage, TpVersion>> roots)
        {
            var plan = new Plan();
            var done = new HashSet<string>();
            var visiting = new HashSet<string>();
            foreach (var r in roots) Visit(plan, r.Key, r.Value, false, done, visiting);

            var manifest = ManifestText();
            var seen = new HashSet<string>();
            foreach (var it in plan.Items)
            {
                if (it.Version.upmDependencies == null) continue;
                foreach (var id in it.Version.upmDependencies)
                {
                    string name = id.Split('@')[0];
                    if (seen.Contains(name)) continue;
                    seen.Add(name);
                    if (!Regex.IsMatch(manifest, "\"" + Regex.Escape(name) + "\"\\s*:")) plan.Upm.Add(id);
                }
            }
            return plan;
        }

        static void Visit(Plan plan, TpPackage p, TpVersion v, bool isDep, HashSet<string> done, HashSet<string> visiting)
        {
            if (done.Contains(p.slug)) return;
            if (visiting.Contains(p.slug)) { plan.Problems.Add("Döngüsel bağımlılık: " + p.slug); return; }
            visiting.Add(p.slug);
            if (v.dependencies != null)
            {
                foreach (var d in v.dependencies)
                {
                    var parts = d.Split('@');
                    string slug = parts[0];
                    string min = parts.Length > 1 ? parts[1] : "";
                    var dp = TpCatalog.Find(slug);
                    if (dp == null) { plan.Problems.Add(p.DisplayName + " → '" + slug + "' katalogda yok."); continue; }
                    var inst = Installed(slug);
                    if (inst != null && (min.Length == 0 || TpSemVer.Compare(inst.version, min) >= 0)) continue;
                    var dv = dp.Latest;
                    if (dv == null) { plan.Problems.Add(slug + " için sürüm yok."); continue; }
                    if (min.Length > 0 && TpSemVer.Compare(dv.version, min) < 0)
                    {
                        plan.Problems.Add(p.DisplayName + " → " + slug + " en az " + min + " istiyor; katalogdaki en yeni " + dv.version + ".");
                        continue;
                    }
                    Visit(plan, dp, dv, true, done, visiting);
                }
            }
            visiting.Remove(p.slug);
            done.Add(p.slug);
            plan.Items.Add(new PlanItem { Package = p, Version = v, IsDependency = isDep });
        }

        static string ManifestText()
        {
            try { return File.ReadAllText("Packages/manifest.json"); } catch (Exception) { return ""; }
        }

        /// <summary>Unity version warning for a package version (empty when fine).</summary>
        public static string UnityWarning(TpVersion v)
        {
            if (v == null || string.IsNullOrEmpty(v.unity)) return "";
            string cur = Application.unityVersion;
            int f = cur.IndexOfAny(new[] { 'a', 'b', 'f', 'p', 'c', 'x' });
            if (f > 0) cur = cur.Substring(0, f);
            return TpSemVer.Compare(cur, v.unity) < 0 ? ("Bu sürüm Unity " + v.unity + "+ ile yüklendi; sen " + Application.unityVersion + " kullanıyorsun.") : "";
        }

        /* ========================================================= install === */

        public static bool Busy { get { var q = LoadQueue(); return q.current != null && !string.IsNullOrEmpty(q.current.slug) || q.jobs.Count > 0 || _downloading; } }
        static bool _downloading;

        public static Task<string> Install(TpPackage p, TpVersion v, bool interactive = true)
        {
            return InstallMany(new List<KeyValuePair<TpPackage, TpVersion>> { new KeyValuePair<TpPackage, TpVersion>(p, v) }, interactive);
        }

        /// <summary>Downloads everything in the plan, then imports it in order. interactive: confirmation dialogs.</summary>
        public static async Task<string> InstallMany(IList<KeyValuePair<TpPackage, TpVersion>> roots, bool interactive = true)
        {
            if (Busy) return "Başka bir kurulum sürüyor.";
            if (roots == null || roots.Count == 0) return "Kurulacak paket yok.";
            var plan = Resolve(roots);
            if (plan.Problems.Count > 0)
            {
                string msg = string.Join("\n", plan.Problems);
                if (!interactive || !EditorUtility.DisplayDialog("Twice Packages — bağımlılık sorunu", msg + "\n\nYine de devam edilsin mi?", "Devam", "Vazgeç"))
                    return msg;
            }
            string warn = string.Join("\n", roots.Select(r => UnityWarning(r.Value)).Where(w => w.Length > 0).ToArray());
            var deps = plan.Items.Where(x => x.IsDependency).ToList();
            if (interactive && (deps.Count > 0 || plan.Upm.Count > 0 || warn.Length > 0 || roots.Count > 1))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var r in roots) sb.AppendLine(r.Key.DisplayName + " " + r.Value.version + " kurulacak.");
                if (deps.Count > 0)
                {
                    sb.AppendLine("\nÖnce şu Twice paketleri:");
                    foreach (var d in deps) sb.AppendLine("  • " + d.Package.DisplayName + " " + d.Version.version);
                }
                if (plan.Upm.Count > 0)
                {
                    sb.AppendLine("\nUnity Package Manager'a eklenecek:");
                    foreach (var u in plan.Upm) sb.AppendLine("  • " + u);
                }
                if (warn.Length > 0) sb.AppendLine("\n⚠ " + warn);
                if (!EditorUtility.DisplayDialog("Twice Packages", sb.ToString(), "Kur", "Vazgeç")) return "Vazgeçildi.";
            }

            // 1) download (cancelable), verified against the catalog sha256
            var jobs = new List<Job>();
            var cancel = new TpCancel();
            _downloading = true;
            try
            {
                for (int i = 0; i < plan.Items.Count; i++)
                {
                    var it = plan.Items[i];
                    string title = "Twice Packages — indiriliyor (" + (i + 1) + "/" + plan.Items.Count + ")";
                    string info = it.Package.DisplayName + " " + it.Version.version + " · " + TpFormat.Size(it.Version.size);
                    var res = await TpCatalog.Ensure(it.Package, it.Version, pr =>
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(title, info, pr)) cancel.Requested = true;
                    }, cancel);
                    EditorUtility.ClearProgressBar();
                    if (res.Value != null) return res.Value;
                    var prev = Installed(it.Package.slug);
                    jobs.Add(new Job
                    {
                        slug = it.Package.slug,
                        version = it.Version.version,
                        path = res.Key,
                        interactive = TpSettings.InteractiveImport && !it.IsDependency,
                        previousVersion = prev != null ? prev.version : ""
                    });
                }
            }
            finally
            {
                _downloading = false;
                EditorUtility.ClearProgressBar();
            }

            // 2) queue the imports (survives domain reloads), 3) UPM deps in one resolve, 4) import
            var q = LoadQueue();
            q.jobs.AddRange(jobs);
            q.waitingUpm = plan.Upm.Count > 0;
            SaveQueue(q);
            if (plan.Upm.Count > 0)
            {
                TpLog.Note("UPM paketleri ekleniyor: " + string.Join(", ", plan.Upm));
                var req = Client.AddAndRemove(plan.Upm.ToArray(), null);
                while (!req.IsCompleted) await Task.Delay(100);
                if (req.Status == StatusCode.Failure) TpLog.Error("UPM eklenemedi: " + (req.Error != null ? req.Error.message : "?"));
                q = LoadQueue();
                q.waitingUpm = false;
                SaveQueue(q);
            }
            Pump();
            return null;
        }

        /* ---- persistent queue ------------------------------------------------- */

        [Serializable]
        public class Job
        {
            public string slug;
            public string version;
            public string path;
            public bool interactive;
            public string previousVersion;
        }

        [Serializable]
        public class Queue
        {
            public List<Job> jobs = new List<Job>();
            public Job current;
            public bool waitingUpm;
        }

        static Queue LoadQueue()
        {
            var json = SessionState.GetString(QueueKey, "");
            Queue q = null;
            if (json.Length > 0) { try { q = JsonUtility.FromJson<Queue>(json); } catch (Exception) { q = null; } }
            if (q == null) q = new Queue();
            if (q.jobs == null) q.jobs = new List<Job>();
            if (q.current != null && string.IsNullOrEmpty(q.current.slug)) q.current = null;   // JsonUtility never leaves it null
            return q;
        }

        static void SaveQueue(Queue q) { SessionState.SetString(QueueKey, JsonUtility.ToJson(q)); }

        static void Pump()
        {
            var q = LoadQueue();
            if (q.current != null || q.waitingUpm) return;
            if (q.jobs.Count == 0) { if (Changed != null) Changed(); return; }
            q.current = q.jobs[0];
            q.jobs.RemoveAt(0);
            SaveQueue(q);
            if (!File.Exists(q.current.path))
            {
                TpLog.Error("Önbellekte dosya yok: " + q.current.path);
                q.current = null; q.jobs.Clear(); SaveQueue(q);
                return;
            }
            TpLog.Note("İçe aktarılıyor: " + q.current.slug + " " + q.current.version);
            AssetDatabase.ImportPackage(q.current.path, q.current.interactive);
        }

        static void ResumeAfterReload()
        {
            var q = LoadQueue();
            if (q.waitingUpm) { q.waitingUpm = false; SaveQueue(q); }   // the reload means UPM resolved
            if (q.current != null)
            {
                // The import ran (the reload came from it) but its callback was lost.
                if (InstalledGuidCount(q.current) > 0) Record(q.current);
                q = LoadQueue();
                q.current = null;
                SaveQueue(q);
            }
            if (q.jobs.Count > 0) EditorApplication.delayCall += Pump;
        }

        static int InstalledGuidCount(Job j)
        {
            try
            {
                int n = 0;
                foreach (var e in TpUnityPackageReader.Read(j.path))
                    if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(e.Guid))) n++;
                return n;
            }
            catch (Exception) { return 0; }
        }

        static bool IsCurrent(string packageName, out Queue q)
        {
            q = LoadQueue();
            return q.current != null && Path.GetFileNameWithoutExtension(q.current.path) == packageName;
        }

        static void OnImportCompleted(string packageName)
        {
            Queue q;
            if (!IsCurrent(packageName, out q)) return;
            var job = q.current;
            Record(job);
            q = LoadQueue();
            q.current = null;
            SaveQueue(q);
            TpLog.Note("Kuruldu: " + job.slug + " " + job.version);
            EditorApplication.delayCall += Pump;
        }

        static void OnImportCancelled(string packageName)
        {
            Queue q;
            if (!IsCurrent(packageName, out q)) return;
            TpLog.Warn("İçe aktarma iptal edildi: " + q.current.slug + (q.jobs.Count > 0 ? " (kalan " + q.jobs.Count + " paket de durduruldu)" : ""));
            q.current = null;
            q.jobs.Clear();
            SaveQueue(q);
            if (Changed != null) Changed();
        }

        static void OnImportFailed(string packageName, string error)
        {
            Queue q;
            if (!IsCurrent(packageName, out q)) return;
            TpLog.Error("İçe aktarma başarısız: " + q.current.slug + " — " + error);
            q.current = null;
            q.jobs.Clear();
            SaveQueue(q);
            if (Changed != null) Changed();
        }

        /// <summary>Writes the install record; on an update, offers to delete what the old version had and the new one doesn't.</summary>
        static void Record(Job job)
        {
            List<TpUnityPackageReader.Entry> entries;
            try { entries = TpUnityPackageReader.Read(job.path); }
            catch (Exception e) { TpLog.Error("Paket içeriği okunamadı: " + e.Message); entries = new List<TpUnityPackageReader.Entry>(); }

            // Interactive import: the user may have unticked files — keep only what landed.
            var present = entries.Where(e => !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(e.Guid))).ToList();
            if (present.Count == 0) present = entries;

            var rec = new TpInstalled
            {
                slug = job.slug,
                version = job.version,
                installedAt = DateTime.UtcNow.ToString("o"),
                installedBy = TpCatalog.User ?? "",
                guids = present.Select(e => e.Guid).ToList(),
                folders = present.Where(e => e.IsFolder).Select(e => e.Guid).ToList()
            };

            var old = Installed(job.slug);
            if (old != null && TpSettings.CleanUpdate)
            {
                var keep = new HashSet<string>(entries.Select(e => e.Guid));
                var shared = OtherPackagesGuids(job.slug);
                var stale = old.guids.Where(g => !keep.Contains(g) && !shared.Contains(g) && !old.folders.Contains(g))
                    .Select(g => AssetDatabase.GUIDToAssetPath(g)).Where(pth => !string.IsNullOrEmpty(pth) && File.Exists(pth)).ToList();
                if (stale.Count > 0 && EditorUtility.DisplayDialog("Twice Packages — eski dosyalar",
                        job.slug + " " + old.version + " → " + job.version + ": yeni sürümde olmayan " + stale.Count + " dosya var.\n\n" +
                        string.Join("\n", stale.Take(12)) + (stale.Count > 12 ? "\n…" : "") + "\n\nSilinsinler mi?", "Sil", "Bırak"))
                {
                    var failed = new List<string>();
                    AssetDatabase.DeleteAssets(stale.ToArray(), failed);
                    DeleteEmptyFolders(old.folders.Where(g => !keep.Contains(g)));
                }
            }

            State.installed.RemoveAll(x => x.slug == job.slug);
            State.installed.Add(rec);
            State.installed.Sort((a, b) => string.CompareOrdinal(a.slug, b.slug));
            SaveState();
        }

        static HashSet<string> OtherPackagesGuids(string except)
        {
            var s = new HashSet<string>();
            foreach (var i in State.installed) if (i.slug != except) foreach (var g in i.guids) s.Add(g);
            return s;
        }

        /* ========================================================== remove === */

        /// <summary>Deletes every asset the package installed (files shared with another installed package stay).</summary>
        public static string Uninstall(string slug, bool interactive = true)
        {
            var inst = Installed(slug);
            if (inst == null) return "Bu projede kurulu değil.";

            var dependents = new List<string>();
            foreach (var other in State.installed)
            {
                if (other.slug == slug) continue;
                var op = TpCatalog.Find(other.slug);
                var ov = op != null ? op.Find(other.version) : null;
                if (ov != null && ov.dependencies != null && ov.dependencies.Any(d => d.Split('@')[0] == slug)) dependents.Add(other.slug);
            }

            var shared = OtherPackagesGuids(slug);
            var files = inst.guids.Where(g => !inst.folders.Contains(g) && !shared.Contains(g))
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p))).ToList();

            if (interactive)
            {
                string msg = slug + " " + inst.version + " kaldırılacak: " + files.Count + " dosya silinir.\n\n" +
                             string.Join("\n", files.Take(15)) + (files.Count > 15 ? "\n…" : "");
                if (dependents.Count > 0) msg += "\n\n⚠ Buna bağımlı kurulu paketler: " + string.Join(", ", dependents);
                if (!EditorUtility.DisplayDialog("Twice Packages — kaldır", msg, "Kaldır", "Vazgeç")) return "Vazgeçildi.";
            }

            var failed = new List<string>();
            if (files.Count > 0) AssetDatabase.DeleteAssets(files.ToArray(), failed);
            DeleteEmptyFolders(inst.folders.Where(g => !shared.Contains(g)));
            State.installed.RemoveAll(x => x.slug == slug);
            SaveState();
            AssetDatabase.Refresh();
            if (failed.Count > 0) return "Silinemeyenler: " + string.Join(", ", failed);
            TpLog.Note("Kaldırıldı: " + slug);
            return null;
        }

        /// <summary>Forget a package without touching files (e.g. it was deleted by hand).</summary>
        public static void Forget(string slug)
        {
            State.installed.RemoveAll(x => x.slug == slug);
            SaveState();
        }

        static void DeleteEmptyFolders(IEnumerable<string> folderGuids)
        {
            var paths = folderGuids.Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => !string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p))
                .OrderByDescending(p => p.Length).ToList();
            foreach (var p in paths)
            {
                if (p == "Assets") continue;
                bool empty = !Directory.EnumerateFileSystemEntries(p).Any(e => !e.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(e).StartsWith("."));
                if (empty) AssetDatabase.DeleteAsset(p);
            }
        }
    }
}
