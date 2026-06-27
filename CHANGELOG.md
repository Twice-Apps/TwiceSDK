# Changelog — Twice SDK (React Native / Expo)

This is the changelog for the React Native / Expo package (`@twiceapps/react-native`,
branch `react-expo`). The Unity SDK has its own changelog on `main`. Adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-06-27
### Added
- **Analytics** (`TwiceAnalytics`): batched event ingest with offline persistence
  (AsyncStorage when available, in-memory fallback), per-event `event_id` for idempotent
  at-least-once delivery, exponential-backoff retry, auto session tracking
  (`session_start` / `session_end`, new session after 30 min in background via `AppState`),
  and automatic sandbox/production tagging (`__DEV__` → sandbox).
- **Typed events**: `debugEvent`, `warningEvent`, `errorEvent` (+ `Error` overload that folds
  message/name/stack into params) and `logEvent(name, type, params)`. Presets `purchase` /
  `adWatched` / `adRevenue` auto-tag `purchase` / `ad`. Matches the Unity SDK + backend
  event-type contract (`debug|warning|error|purchase|ad|general`).
- **Presets**: `levelStarted`, `levelCompleted`, `levelFailed`, `tutorialCompleted`,
  `screenView`, `purchase`, `adWatched`, `adRevenue`, `rewardClaimed`.
- **Remote Config** (`TwiceRemoteConfig`): cached typed key-value with `getBool/getInt/getFloat/
  getNumber/getString/getJson`, `fetch()`, `onUpdated()`, and a `version`.
- **Consent** (`setConsent`), runtime sandbox override (`setSandbox`), user properties
  (`setUserProperty`), display name (`setDisplayName`), and `Twice.init()` one-call setup.
- Ships TypeScript source (Metro/Expo transpiles it); full type definitions; `AGENTS.md`
  integration guide for AI assistants.
