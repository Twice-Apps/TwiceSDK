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
  Each event envelope also carries the platform build number (from the Version Checker).
- **Version Checker** (`TwiceSDK.VersionCheck.TwiceVersionChecker`): `Check(cb)` asks the backend
  whether the running build is behind the configured latest (→ `Optional`) or minimum (→ `Forced`)
  version and returns the decision + `StoreUrl` (the prompt UI is the game's job). Owns build-number
  resolution — iOS `CFBundleVersion` / Android `versionCode` — so store/TestFlight builds are
  distinguished (e.g. `1.0 (1)` vs `1.0 (2)`).
- **Remote Config** (`TwiceSDK.RemoteConfig.TwiceRemoteConfig`): pulls `GET {base}/sdk/config`,
  caches it (offline + instant next launch), bumps a `version`, and exposes typed getters
  (`GetBool/Int/Long/Float/Double/String`), `GetRawJson` and `GetJson<T>`, plus `OnUpdated`.
- **Version prompt** (`TwiceSDK.VersionCheck.TwiceUpdatePrompt`): drop the `VersionChecker` prefab
  (full-screen blocking Canvas: black tint + "Update Required" + Update button) into the first scene.
  It bootstraps the SDK, lets analytics report this build's version, runs the version check, then
  reveals the prompt when an update is needed or destroys itself otherwise. The Update button opens
  the store link built on-device from the ids the backend returns (iOS App ID → `itms-apps://`,
  Android bundle id → `market://`). Singleton + survives scene loads. Verbose debug logs.
- **`TwiceSDK.Twice`** facade with `Twice.Initialize()` — optional explicit SDK entry point for future
  module wiring.
- **Settings** (`TwiceSDK.TwiceSettings`) ScriptableObject (`Create → Twice → SDK Settings`),
  auto-loaded from `Resources/TwiceSettings`. Editor auto-creates an empty one on import
  (paste your X-App-Key in the Inspector).
- JSON handled by Newtonsoft.Json (`com.unity.nuget.newtonsoft-json`, auto-installed as a
  package dependency). Editor debugger window (`Twice → Analytics Debugger`). Quick Start sample.
