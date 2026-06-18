# Changelog

All notable changes to the Twice SDK are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-06-18
### Added
- **Remote Config** module (`TwiceRemoteConfig`): pulls `GET {base}/sdk/config`, caches the
  result (offline + instant next launch), and exposes typed getters
  (`GetBool/GetInt/GetLong/GetFloat/GetDouble/GetString`), `GetRawJson` and
  `GetJson<T>` for nested objects, plus `Version`, `IsReady`, `HasKey`, `Keys` and an
  `OnUpdated` event. Dependency-free JSON scanner; auto-fetch at boot.
- `autoFetchRemoteConfig` toggle on `TwiceAnalyticsSettings`.
### Notes
- Analytics API (`TwiceAnalytics.*`), namespace and asmdef are unchanged — existing
  integrations keep working as-is.

## [1.0.0] - 2026-06-17
### Added
- Initial release.
- Core client `TwiceAnalytics` (batching, offline persistence, exponential-backoff retry).
- `TwiceAnalyticsSettings` ScriptableObject (`Create → Twice → Analytics Settings`).
- Dependency-free JSON writer (`TwiceJson`).
- Auto session tracking (`session_start` / `session_end`).
- Preset events: level lifecycle, tutorial, screen_view, purchase, ad_watched, reward_claimed.
- Per-event `event_id` (GUID) for backend de-duplication of resent events.
- Editor debugger window (`Twice → Analytics Debugger`).
- Quick Start sample.
