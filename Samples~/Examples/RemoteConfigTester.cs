using System.Text;
using UnityEngine;
using TwiceSDK.RemoteConfig;

namespace TwiceSDKTest
{
    /// <summary>
    /// Remote Config tester. Press Play; values appear on-screen (OnGUI) and in the Console.
    /// Requires Assets/Resources/TwiceSettings with your game's X-App-Key set.
    /// </summary>
    public class RemoteConfigTester : MonoBehaviour
    {
        [System.Serializable]
        public class GameSettingsDemo
        {
            public bool analyticsEnabled;
            public int adFreeUntilLevel;
            public int adReward;
            public int hintPrice;
        }

        string _text = "Remote Config: waiting…";
        Vector2 _scroll;

        void Start()
        {
            TwiceRemoteConfig.OnUpdated += Refresh;
            Refresh(); // show cached immediately
            TwiceRemoteConfig.Fetch(ok =>
            {
                Debug.Log($"[RC Test] Fetch complete. ok={ok}, version={TwiceRemoteConfig.Version}");
                Refresh();
            });
        }

        void OnDestroy()
        {
            TwiceRemoteConfig.OnUpdated -= Refresh;
        }

        void Refresh()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<b>Remote Config</b>  v{TwiceRemoteConfig.Version}  (ready={TwiceRemoteConfig.IsReady})");
            sb.AppendLine($"keys: {string.Join(", ", TwiceRemoteConfig.Keys)}");
            sb.AppendLine();
            sb.AppendLine($"ads_enabled (bool)    = {TwiceRemoteConfig.GetBool("ads_enabled", true)}");
            sb.AppendLine($"ads (bool)            = {TwiceRemoteConfig.GetBool("ads", false)}");
            sb.AppendLine($"coins_per_level (int) = {TwiceRemoteConfig.GetInt("coins_per_level", 50)}  (shows default if key missing)");
            sb.AppendLine();

            var gs = TwiceRemoteConfig.GetJson<GameSettingsDemo>("GameSettings");
            if (gs != null)
            {
                sb.AppendLine("<b>GameSettings (parsed via GetJson&lt;T&gt;)</b>");
                sb.AppendLine($"  analyticsEnabled = {gs.analyticsEnabled}");
                sb.AppendLine($"  adFreeUntilLevel = {gs.adFreeUntilLevel}");
                sb.AppendLine($"  adReward         = {gs.adReward}");
                sb.AppendLine($"  hintPrice        = {gs.hintPrice}");
                sb.AppendLine();
            }
            sb.AppendLine("<b>GameSettings (raw json)</b>");
            sb.AppendLine(TwiceRemoteConfig.GetRawJson("GameSettings") ?? "(none)");

            _text = sb.ToString();
            Debug.Log("[RC Test]\n" + _text);
        }

        void OnGUI()
        {
            var area = new Rect(16, 16, Mathf.Min(740f, Screen.width - 32), Screen.height - 32);
            GUILayout.BeginArea(area, GUI.skin.box);
            if (GUILayout.Button("Fetch again", GUILayout.Width(140), GUILayout.Height(36)))
                TwiceRemoteConfig.Fetch(ok => { Debug.Log($"[RC Test] refetch ok={ok}"); Refresh(); });
            _scroll = GUILayout.BeginScrollView(_scroll);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
            GUILayout.Label(_text, style);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
