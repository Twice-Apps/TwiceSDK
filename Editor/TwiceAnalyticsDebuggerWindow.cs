using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using TwiceSDK.Analytics;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// Editor-only test/debug console for the Twice analytics SDK.
    /// Open via <c>Twice → Analytics Debugger</c>. Lets you compose and fire events
    /// (custom or preset) with typed params, toggle consent, flush, and watch the live
    /// queue + last server status while in Play Mode. Never ships with a build.
    /// </summary>
    public class TwiceAnalyticsDebuggerWindow : EditorWindow
    {
        enum ParamType { String, Number, Bool }

        class ParamRow
        {
            public string key = "";
            public ParamType type = ParamType.String;
            public string stringValue = "";
            public bool boolValue = true;
        }

        static readonly string[] Presets =
        {
            "level_started", "level_completed", "level_failed", "tutorial_completed",
            "screen_view", "purchase", "ad_watched", "reward_claimed",
        };

        // Event types the dashboard filters/splits by. "general" sends no explicit type.
        static readonly string[] Types = { "general", "debug", "warning", "error", "purchase", "ad" };

        string _eventName = "level_completed";
        int _typeIndex;
        readonly List<ParamRow> _params = new List<ParamRow>();
        Vector2 _scroll;
        bool _consentToggle = true;

        [MenuItem("Twice/Analytics Debugger")]
        public static void Open()
        {
            var w = GetWindow<TwiceAnalyticsDebuggerWindow>("Twice Analytics");
            w.minSize = new Vector2(360, 420);
            w.Show();
        }

        void OnEnable()
        {
            if (_params.Count == 0)
            {
                _params.Add(new ParamRow { key = "level", type = ParamType.String, stringValue = "1-3" });
                _params.Add(new ParamRow { key = "score", type = ParamType.Number, stringValue = "1200" });
                _params.Add(new ParamRow { key = "duration", type = ParamType.Number, stringValue = "42.5" });
            }
        }

        // Keep the live status fresh while playing.
        void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }

        void OnGUI()
        {
            var info = TwiceAnalytics.GetDebugInfo();

            DrawStatus(info);
            EditorGUILayout.Space(6);
            DrawComposer();
            EditorGUILayout.Space(6);
            DrawActions();
            EditorGUILayout.Space(6);
            DrawQueue(info);
        }

        void DrawStatus(TwiceAnalytics.DebugInfo info)
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!Application.isPlaying)
                    EditorGUILayout.HelpBox("SDK only runs in Play Mode. Enter Play Mode to send events.", MessageType.Info);

                Row("Initialized", info.initialized ? "Yes" : "No");
                Row("Consent", info.consent ? "Granted" : "Revoked");
                Row("Environment", string.IsNullOrEmpty(info.environment) ? "-" : info.environment);
                Row("Platform", string.IsNullOrEmpty(info.platform) ? "-" : info.platform);
                Row("App version", string.IsNullOrEmpty(info.appVersion) ? "-" : info.appVersion);
                Row("Endpoint", string.IsNullOrEmpty(info.endpoint) ? "-" : info.endpoint);
                Row("API key", info.apiKeyMasked);
                Row("User id", string.IsNullOrEmpty(info.userId) ? "-" : info.userId);
                Row("Session id", string.IsNullOrEmpty(info.sessionId) ? "-" : info.sessionId);
                Row("Pending", info.pending.ToString());
                Row("Last status", string.IsNullOrEmpty(info.lastStatus) ? "-" : info.lastStatus);
            }
        }

        void DrawComposer()
        {
            EditorGUILayout.LabelField("Compose event", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _eventName = EditorGUILayout.TextField("Event name", _eventName);

                // Preset quick-fill
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Presets", GUILayout.Width(60));
                    int picked = EditorGUILayout.Popup(-1, Presets);
                    if (picked >= 0) _eventName = Presets[picked];
                }

                _typeIndex = EditorGUILayout.Popup("Type", _typeIndex, Types);

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Params", EditorStyles.miniBoldLabel);

                int removeIndex = -1;
                for (int i = 0; i < _params.Count; i++)
                {
                    var p = _params[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        p.key = EditorGUILayout.TextField(p.key, GUILayout.MinWidth(70));
                        p.type = (ParamType)EditorGUILayout.EnumPopup(p.type, GUILayout.Width(70));
                        if (p.type == ParamType.Bool)
                            p.boolValue = EditorGUILayout.Toggle(p.boolValue, GUILayout.Width(40));
                        else
                            p.stringValue = EditorGUILayout.TextField(p.stringValue, GUILayout.MinWidth(70));
                        if (GUILayout.Button("✕", GUILayout.Width(22))) removeIndex = i;
                    }
                }
                if (removeIndex >= 0) _params.RemoveAt(removeIndex);

                if (GUILayout.Button("+ Add param")) _params.Add(new ParamRow());
            }
        }

        void DrawActions()
        {
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Log Event", GUILayout.Height(28))) LogComposed();
                    if (GUILayout.Button("Log + Flush", GUILayout.Height(28))) { LogComposed(); TwiceAnalytics.Flush(); }
                    if (GUILayout.Button("Flush", GUILayout.Width(70), GUILayout.Height(28))) TwiceAnalytics.Flush();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Consent", GUILayout.Width(60));
                bool newConsent = EditorGUILayout.ToggleLeft(_consentToggle ? "Granted" : "Revoked", _consentToggle);
                if (newConsent != _consentToggle)
                {
                    _consentToggle = newConsent;
                    TwiceAnalytics.SetConsent(newConsent);
                }
            }
        }

        void DrawQueue(TwiceAnalytics.DebugInfo info)
        {
            EditorGUILayout.LabelField($"Queue (showing up to 20 of {info.pending})", EditorStyles.boldLabel);
            using (var sv = new EditorGUILayout.ScrollViewScope(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(80)))
            {
                _scroll = sv.scrollPosition;
                if (info.pendingEvents == null || info.pendingEvents.Length == 0)
                    EditorGUILayout.LabelField(Application.isPlaying ? "(empty)" : "(enter Play Mode)");
                else
                    foreach (var e in info.pendingEvents)
                        EditorGUILayout.LabelField("• " + e);
            }
        }

        void LogComposed()
        {
            var dict = new Dictionary<string, object>();
            foreach (var p in _params)
            {
                if (string.IsNullOrEmpty(p.key)) continue;
                switch (p.type)
                {
                    case ParamType.Bool:
                        dict[p.key] = p.boolValue;
                        break;
                    case ParamType.Number:
                        if (long.TryParse(p.stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                            dict[p.key] = l;
                        else if (double.TryParse(p.stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                            dict[p.key] = d;
                        else
                            dict[p.key] = 0; // unparseable number defaults to 0
                        break;
                    default:
                        dict[p.key] = p.stringValue;
                        break;
                }
            }
            var composed = dict.Count > 0 ? dict : null;
            string type = Types[_typeIndex];
            if (type == "general") TwiceAnalytics.LogEvent(_eventName, composed);
            else TwiceAnalytics.LogEvent(_eventName, type, composed);
        }

        static void Row(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90));
                EditorGUILayout.SelectableLabel(value, GUILayout.Height(16));
            }
        }
    }
}
