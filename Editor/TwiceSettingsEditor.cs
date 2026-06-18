using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using TwiceSDK;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="TwiceSettings"/>: modular foldout sections plus
    /// Remote Config test tools — fetch the live config JSON to the console, and log
    /// "how to read it" code snippets generated from the game's real keys.
    /// </summary>
    [CustomEditor(typeof(TwiceSettings))]
    public class TwiceSettingsEditor : UnityEditor.Editor
    {
        static bool _project = true, _analytics = true, _remote = true, _env, _debug;
        bool _fetching;
        string _lastResponse;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var s = (TwiceSettings)target;

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Twice SDK", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("analytics + remote config", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            Section(ref _project, "Project", () =>
            {
                Prop("apiKey", "API Key (X-App-Key)");
                Prop("endpointBaseUrl", "Endpoint Base URL");
            });

            Section(ref _analytics, "Analytics", () =>
            {
                Prop("autoTrackSessions");
                Prop("flushIntervalSeconds");
                Prop("maxBatchSize");
            });

            Section(ref _remote, "Remote Config", () =>
            {
                Prop("autoFetchRemoteConfig");
                EditorGUILayout.Space(4);

                using (new EditorGUI.DisabledScope(_fetching || string.IsNullOrEmpty(s.apiKey)))
                {
                    if (GUILayout.Button(_fetching ? "Fetching…" : "Fetch Remote Config → Console"))
                        FetchToConsole(s);
                }
                if (GUILayout.Button("How to use (log code snippets → Console)"))
                    LogHowTo();

                if (string.IsNullOrEmpty(s.apiKey))
                    EditorGUILayout.HelpBox("Set the API Key above to fetch remote config.", MessageType.Info);
            });

            Section(ref _env, "Environment", () => Prop("environment"));
            Section(ref _debug, "Debug", () => Prop("debugLogging"));

            serializedObject.ApplyModifiedProperties();
        }

        // ---- UI helpers -----------------------------------------------------

        void Section(ref bool open, string title, System.Action body)
        {
            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            if (open)
            {
                EditorGUI.indentLevel++;
                body();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void Prop(string name, string label = null)
        {
            var p = serializedObject.FindProperty(name);
            if (p == null) return;
            if (label == null) EditorGUILayout.PropertyField(p);
            else EditorGUILayout.PropertyField(p, new GUIContent(label));
        }

        // ---- Remote Config test tools --------------------------------------

        void FetchToConsole(TwiceSettings s)
        {
            _fetching = true;
            Repaint();
            string url = s.endpointBaseUrl.TrimEnd('/') + "/sdk/config";
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("X-App-Key", s.apiKey);
            req.SendWebRequest();

            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!req.isDone) return;
                EditorApplication.update -= poll;
                _fetching = false;
                if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                {
                    _lastResponse = req.downloadHandler.text;
                    Debug.Log("[Twice Remote Config] GET " + url + "\n" + Pretty(_lastResponse));
                }
                else
                {
                    Debug.LogError($"[Twice Remote Config] fetch failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}");
                }
                req.Dispose();
                Repaint();
            };
            EditorApplication.update += poll;
        }

        static string Pretty(string json)
        {
            try { return JToken.Parse(json).ToString(Newtonsoft.Json.Formatting.Indented); }
            catch { return json; }
        }

        void LogHowTo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("// === Twice Remote Config — how to read values in code ===");
            sb.AppendLine("using TwiceSDK.RemoteConfig;");
            sb.AppendLine();

            JObject cfg = null;
            int version = 0;
            if (!string.IsNullOrEmpty(_lastResponse))
            {
                try { var root = JObject.Parse(_lastResponse); cfg = root["config"] as JObject; version = (int?)root["version"] ?? 0; }
                catch { }
            }

            if (cfg != null && cfg.Count > 0)
            {
                sb.AppendLine($"// Generated from this game's live config (v{version}):");
                foreach (var p in cfg.Properties())
                    sb.AppendLine(Snippet(p.Name, p.Value));
            }
            else
            {
                sb.AppendLine("// No live config yet — example keys (press 'Fetch Remote Config' first for real ones):");
                sb.AppendLine(Snippet("ads_enabled", new JValue(true)));
                sb.AppendLine(Snippet("coins_per_level", new JValue(50)));
                sb.AppendLine(Snippet("GameSettings", new JObject()));
            }

            sb.AppendLine();
            sb.AppendLine("TwiceRemoteConfig.OnUpdated += () => { /* re-apply when config changes */ };");
            sb.AppendLine("TwiceRemoteConfig.Fetch(); // manual refresh (also auto-fetched at boot)");
            Debug.Log(sb.ToString());
        }

        static string Snippet(string key, JToken val)
        {
            string id = Ident(key);
            switch (val.Type)
            {
                case JTokenType.Boolean: return $"bool {id} = TwiceRemoteConfig.GetBool(\"{key}\", false);";
                case JTokenType.Integer: return $"int {id} = TwiceRemoteConfig.GetInt(\"{key}\", 0);";
                case JTokenType.Float:   return $"float {id} = TwiceRemoteConfig.GetFloat(\"{key}\", 0f);";
                case JTokenType.String:  return $"string {id} = TwiceRemoteConfig.GetString(\"{key}\", \"\");";
                case JTokenType.Object:
                case JTokenType.Array:   return $"var {id} = TwiceRemoteConfig.GetJson<My{Pascal(key)}>(\"{key}\");  // define a [Serializable] class, or use GetRawJson(\"{key}\")";
                default:                 return $"string {id} = TwiceRemoteConfig.GetString(\"{key}\", \"\");";
            }
        }

        static string Ident(string key)
        {
            var sb = new StringBuilder();
            foreach (char c in key)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            string r = sb.ToString();
            if (r.Length == 0 || char.IsDigit(r[0])) r = "_" + r;
            return char.ToLowerInvariant(r[0]) + (r.Length > 1 ? r.Substring(1) : "");
        }

        static string Pascal(string key)
        {
            string id = Ident(key);
            return char.ToUpperInvariant(id[0]) + (id.Length > 1 ? id.Substring(1) : "");
        }
    }
}
