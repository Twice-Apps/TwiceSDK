# Changelog

All notable changes to the Twice SDK are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-06-19
### Added
- **Leaderboards** (`TwiceSDK.Leaderboards.TwiceLeaderboards`): `Submit`, `GetTop`, `GetEntryCount`,
  `GetMyRank`, and the archived-period variants `GetTopBeforeReset` / `GetMyRankBeforeReset`.
- **Ad revenue** (`TwiceAnalytics.AdRevenue`): impression-level revenue with network / placement /
  format, for the Monetization dashboard. Added `TwiceAnalytics.UserId` accessor.
- **Initialization gate** (`TwiceSettings.initialization`, `InitializationMode`): `RequireBootstrap`
  makes the SDK initialize **only** when a `TwiceBootstrap` (the TwiceSDK prefab) calls
  `Twice.Initialize()`. In that mode the boot-time auto-inits do nothing — no runner, no
  `session_start`, no version check / remote config. `Auto` (default) preserves the existing
  prefab-less auto-init behaviour.
- **Per-module toggles** (`TwiceSettings`): `enableAnalytics`, `enableRemoteConfig`,
  `enableVersionCheck`, `enableLeaderboards`. A disabled module never initializes / makes no
  network calls.
- **Scene-presence notice**: entering Play mode with no `TwiceBootstrap` in the scene logs a
  coloured console line (Editor / development builds), adapted to the chosen init mode.

### Changed
- `Twice.Initialize()` is now the single init authority: it **configures** analytics and remote
  config from the settings asset (not just flush/fetch) before running them, so `RequireBootstrap`
  works end-to-end. Each ordered step is skipped when its module toggle is off. Re-configuring an
  already-started module stays idempotent.
- **Editable content auto-installs to `Assets/TwiceSDK/` — no "Import Sample" step.** The bootstrap
  prefab (with the per-game VersionChecker prompt) and the Examples / QuickStart demos moved out of
  the immutable package into a hidden `Distributables~` folder; the `samples` manifest entries were
  removed. An editor installer copies the content to `Assets/TwiceSDK/` (Prefabs / Examples /
  QuickStart) on load, once per package version. Copy is non-destructive (existing files / your edits
  are never overwritten) and metas are copied so GUIDs and scene references stay stable.

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
- **Bootstrap** (`TwiceSDK.TwiceBootstrap` + `Twice.Initialize()`): drag the `TwiceSDK` prefab into
  your first / preloader scene — one object, nothing else to wire. `Twice.Initialize()` runs the
  modules **in order, one after the previous finishes**: 1) version check (→ reveals the child
  update prompt when needed), 2) analytics (flush session_start), 3) remote config fetch. Singleton;
  survives scene loads. (Boot-time network calls of analytics/remote config are deferred to this
  ordered sequence; cached remote config is still available offline immediately.)
- **Version prompt** (`TwiceSDK.VersionCheck.TwiceUpdatePrompt`): display-only component on a child
  Canvas of the `Twice` prefab — full-screen, input-blocking, and fully restylable per game. Starts
  hidden; `Show()` reveals it and wires the Update button to the store (iOS App ID → `itms-apps://`,
  Android bundle id → `market://`). Only an Update button is required in the prefab.
- **Editor platform override** (`TwiceSettings`): Editor-only setting to report iOS/Android instead of
  "Editor", so version discovery + the update prompt can be tested in Play mode without a device build.
- **`TwiceSDK.Twice`** facade with `Twice.Initialize()` — optional explicit SDK entry point for future
  module wiring.
- **Settings** (`TwiceSDK.TwiceSettings`) ScriptableObject (`Create → Twice → SDK Settings`),
  auto-loaded from `Resources/TwiceSettings`. Editor auto-creates an empty one on import
  (paste your X-App-Key in the Inspector).
- JSON handled by Newtonsoft.Json (`com.unity.nuget.newtonsoft-json`, auto-installed as a
  package dependency). Editor debugger window (`Twice → Analytics Debugger`). Quick Start sample.
