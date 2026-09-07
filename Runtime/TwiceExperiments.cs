using System;
using UnityEngine;
using Newtonsoft.Json;
using TwiceSDK.Analytics;

namespace TwiceSDK.RemoteConfig
{
    /// <summary>
    /// The A/B experiment assignment the backend attached to the last remote-config response:
    /// which experiment, which variant, and why (hash bucket or a manual pick in the panel).
    /// <see cref="TwiceRemoteConfig.Experiment"/> is null when the player is not in an experiment.
    /// </summary>
    [Serializable]
    public class ExperimentAssignment
    {
        /// <summary>Experiment id (slug) as defined in the panel.</summary>
        public string id = "";
        /// <summary>Variant key: "A", "B", …</summary>
        public string variant = "";
        /// <summary>Backend revision of the experiment; bumps when its values or assignments change.</summary>
        public int rev;
        /// <summary>"auto" (deterministic hash bucket) or "manual" (assigned from the panel).</summary>
        public string source = "";

        /// <summary>"experiment:variant" — the value stamped on every event as the <c>ab_group</c> user property.</summary>
        public string Group => string.IsNullOrEmpty(id) ? "" : id + ":" + variant;

        public bool SameAs(ExperimentAssignment other) => other != null && other.id == id && other.variant == variant;
    }

    /// <summary>
    /// Persisted experiment state shared by the remote-config and analytics runners. The
    /// remote-config runner writes it after each fetch; the analytics runner reads it at boot so
    /// <c>app_open</c> / <c>session_start</c> already carry the variant on the next launch,
    /// before any network round-trip.
    /// </summary>
    internal static class TwiceExperimentState
    {
        const string PrefsKey = "twice_rc_experiment";   // JSON of the last assignment; absent = none
        const string FirstOpenKey = "twice_first_open";  // unix seconds of the very first launch

        internal const string PropGroup = "ab_group";
        internal const string PropExperiment = "experiment_id";
        internal const string PropVariant = "variant_id";

        /// <summary>
        /// Unix time (seconds) of the first launch on this device. Stamped once, on first access;
        /// sent as <c>X-First-Open</c> so "new users only" experiments can tell who installed after
        /// they started.
        /// </summary>
        internal static long FirstOpenUnix
        {
            get
            {
                string s = PlayerPrefs.GetString(FirstOpenKey, "");
                long v;
                if (!string.IsNullOrEmpty(s) && long.TryParse(s, out v) && v > 0) return v;
                v = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                PlayerPrefs.SetString(FirstOpenKey, v.ToString());
                PlayerPrefs.Save();
                return v;
            }
        }

        internal static ExperimentAssignment Load()
        {
            string raw = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                var a = JsonConvert.DeserializeObject<ExperimentAssignment>(raw);
                return (a != null && !string.IsNullOrEmpty(a.id) && !string.IsNullOrEmpty(a.variant)) ? a : null;
            }
            catch { return null; }
        }

        internal static void Save(ExperimentAssignment a)
        {
            if (a == null) PlayerPrefs.DeleteKey(PrefsKey);
            else PlayerPrefs.SetString(PrefsKey, JsonConvert.SerializeObject(a));
            PlayerPrefs.Save();
        }

        /// <summary>Stamp (or clear) the experiment user properties on the analytics runner, if it exists yet.</summary>
        internal static void ApplyUserProps(ExperimentAssignment a)
        {
            var r = TwiceAnalyticsRunner.Instance;
            if (r == null) return;
            if (a == null)
            {
                r.RemoveUserProperty(PropGroup);
                r.RemoveUserProperty(PropExperiment);
                r.RemoveUserProperty(PropVariant);
                return;
            }
            r.SetUserProperty(PropGroup, a.Group);
            r.SetUserProperty(PropExperiment, a.id);
            r.SetUserProperty(PropVariant, a.variant);
        }
    }
}
