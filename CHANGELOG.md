# Changelog — Twice SDK (React Native / Expo)

This is the changelog for the React Native / Expo package (`@twiceapps/react-native`,
branch `react-expo`). The Unity SDK has its own changelog on `main`. Adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.1] - 2026-09-11
### Fixed
- **Session duration was wrong and mostly missing.** `session_end` was emitted only when the
  app returned to the foreground after 30+ minutes in the background, so a session the user
  never came back to was never closed at all (measured on a live project: 1 of 17 sessions had
  a duration). The one that did arrive counted the background wait as app time, because the
  duration was session start to resume.
  Sessions now measure FOREGROUND time only: the clock stops on background and resumes on
  return. The open session is written to storage and closed on the next launch, so a session
  that ends with the app being killed still reports its duration. The record is refreshed on
  every flush, so a crash loses at most one flush interval.
  Playtime in the panel will read HIGHER coverage and LOWER durations than before; the old
  numbers were a small sample inflated by background time.

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
- **Players** (`TwicePlayers`): `userId`, `displayName`, `setDisplayName`.
- **Leaderboards** (`TwiceLeaderboards`): `submit`, `getTop`, `getMyRank`, `getEntryCount`, and the
  archived-period variants `getTopBeforeReset` / `getMyRankBeforeReset` (Promise-based).
- **Version check** (`TwiceVersionCheck`): `check()` → `{ action: none|optional|forced, appId,
  bundleId, … }` for update gating, plus `storeUrl(status)`.
- **Push** (`TwicePush`): `register(token)` / `unregister(token)` — register a device push token
  (from `expo-notifications`) for operator/segment pushes.
- **Consent** (`setConsent`), runtime sandbox override (`setSandbox`), user properties
  (`setUserProperty`), display name (`setDisplayName`), and `Twice.init()` one-call setup.
- Ships TypeScript source (Metro/Expo transpiles it); full type definitions; `AGENTS.md`
  integration guide for AI assistants. Client-module parity with the Unity SDK.
