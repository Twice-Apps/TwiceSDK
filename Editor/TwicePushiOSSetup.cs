using UnityEditor;
using UnityEngine;
using Unity.Notifications;

namespace TwiceSDK.Editor
{
    /// <summary>
    /// Auto-configures the iOS push capability so the user never edits Project Settings →
    /// Mobile Notifications by hand. When push is enabled in TwiceSettings, this turns on
    /// the Mobile Notifications package's "Add Remote Notification Capability" — which adds
    /// the Push Notifications capability + aps-environment entitlement to the Xcode build
    /// (required for APNs device-token registration). Runs on editor load and after the
    /// settings inspector changes. We do NOT enable "request authorization on launch" —
    /// TwicePushiOS issues the AuthorizationRequest itself so it can capture the token.
    /// </summary>
    [InitializeOnLoad]
    public static class TwicePushiOSSetup
    {
        static TwicePushiOSSetup()
        {
            EditorApplication.delayCall += Sync;
        }

        public static void Sync()
        {
            var s = Resources.Load<TwiceSettings>(TwiceSettings.ResourceName);
            if (s == null || !s.enablePushNotifications) { return; }
            try
            {
                if (!NotificationSettings.iOSSettings.AddRemoteNotificationCapability)
                {
                    NotificationSettings.iOSSettings.AddRemoteNotificationCapability = true;
                    Debug.Log("[TwiceSDK] iOS push capability enabled automatically (Mobile Notifications).");
                }
            }
            catch
            {
                // Mobile Notifications API shape changed / not ready — ignore; user can toggle manually.
            }
        }
    }
}
