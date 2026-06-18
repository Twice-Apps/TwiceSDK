# Changelog

All notable changes to the Twice SDK are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-06-18
### Added
- **Analytics** (`TwiceSDK.Analytics.TwiceAnalytics`): batched event ingest with offline
  persistence and exponential-backoff retry, per-event `event_id` (GUID) for idempotent
  at-least-once delivery, auto session tracking (`session_start`/`session_end`), automatic
  sandbox/production tagging (Editor/Dev/TestFlight → sandbox), and preset events
  (level lifecycle, tutorial, screen_view, purchase, ad_watched, reward_claimed).
- **Remote Config** (`TwiceSDK.RemoteConfig.TwiceRemoteConfig`): pulls `GET {base}/sdk/config`,
  caches it (offline + instant next launch), bumps a `version`, and exposes typed getters
  (`GetBool/Int/Long/Float/Double/String`), `GetRawJson` and `GetJson<T>`, plus `OnUpdated`.
- **Settings** (`TwiceSDK.TwiceSettings`) ScriptableObject (`Create → Twice → SDK Settings`),
  auto-loaded from `Resources/TwiceSettings`. Editor auto-creates an empty one on import
  (paste your X-App-Key in the Inspector).
- Dependency-free JSON writer + scanner. Editor debugger window (`Twice → Analytics Debugger`).
  Quick Start sample.
