using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// Keeps the <c>TWICE_FCM</c> scripting define in sync automatically, so the user never
    /// edits Player Settings by hand. The define (which gates the TwiceSDK.Push assembly) is
    /// added only when BOTH are true:
    ///   • Push is enabled in TwiceSettings (<c>enablePushNotifications</c>), and
    ///   • the Firebase Messaging SDK is actually imported in the project.
    /// If Firebase is missing the define is removed — so toggling Push on before importing
    /// Firebase never breaks compilation. Runs on editor load/recompile and after the
    /// settings inspector changes.
    /// </summary>
    [InitializeOnLoad]
    public static class TwicePushDefineSync
    {
        const string Define = "TWICE_FCM";
        static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.iOS, NamedBuildTarget.Android, NamedBuildTarget.Standalone,
        };

        static TwicePushDefineSync()
        {
            // Defer so asset DB + assemblies are ready when we read settings / detect Firebase.
            EditorApplication.delayCall += Sync;
        }

        public static void Sync()
        {
            bool want = WantDefine();
            foreach (var t in Targets)
            {
                string cur;
                try { cur = PlayerSettings.GetScriptingDefineSymbols(t); }
                catch { continue; }

                var list = new List<string>(cur.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                bool has = list.Contains(Define);
                if (want == has) { continue; } // already correct → no write (avoids recompile churn)
                if (want) { list.Add(Define); } else { list.RemoveAll(d => d == Define); }
                try { PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", list)); }
                catch { /* unsupported target group on this Unity install — ignore */ }
            }
        }

        static bool WantDefine()
        {
            if (!FirebasePresent()) { return false; }
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            return s != null && s.enablePushNotifications;
        }

        /// <summary>True if Firebase Messaging is imported (its type is loaded in the editor).</summary>
        static bool FirebasePresent()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (a.GetType("Firebase.Messaging.FirebaseMessaging") != null) { return true; } }
                catch { /* dynamic/reflection-only assembly — skip */ }
            }
            return false;
        }
    }
}
