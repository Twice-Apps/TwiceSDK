# Twice SDK — Unity

Lightweight, **dependency-free** SDK for the Twice backend: **analytics** (events, sessions,
revenue) + **remote config** (typed key-value, PlayFab Title-Data style).
Works on **iOS, Android, WebGL, Windows, macOS and the Unity Editor**. No Newtonsoft, no threads,
no PII — drop-in and privacy-safe (GDPR/KVKK). Requires **Unity 2021.3 LTS+**.

## Install (UPM via Git URL)
Unity → `Window → Package Manager → + → Add package from git URL…`:
```
https://github.com/Twice-Apps/TwiceSDK.git#1.1.0
```
Or add to `Packages/manifest.json`:
```json
"co.twiceapps.analytics": "https://github.com/Twice-Apps/TwiceSDK.git#1.1.0"
```
Pin a version tag (`#1.1.0`) for reproducible builds. Bump the tag to update.

## Setup
1. `Assets → Create → Twice → Analytics Settings`.
2. Move the asset into a `Resources` folder, keep the name `TwiceAnalyticsSettings`
   (e.g. `Assets/Resources/TwiceAnalyticsSettings.asset`) so it auto-initialises at boot.
3. Paste your game key (`X-App-Key`) into the `apiKey` field
   (Twice admin → **Oyunlar** → your game → API anahtarı).

> The settings asset (with your API key) lives in **your game**, never in this package.

## Analytics
```csharp
using Twice.Analytics;

TwiceAnalytics.SetConsent(true);
TwiceAnalytics.LevelCompleted("1-3", score: 1200, duration: 42.5f);
TwiceAnalytics.LogEvent("boss_defeated", new Dictionary<string, object> { { "boss", "golem" }, { "tries", 3 } });
TwiceAnalytics.Flush();
```
Events carry an `event_id` (GUID) for idempotent at-least-once delivery, and an `env`
(`sandbox`/`production`) tag derived automatically (Editor/Dev/TestFlight → sandbox).

## Remote Config
Reads a per-game typed key-value store from the backend, caches it (offline + instant next
launch), and bumps a `version` so the client only changes when the config does.

```csharp
using Twice.Analytics;

// Auto-fetched at boot (toggle on the settings asset). Read with safe defaults:
bool   adsOn  = TwiceRemoteConfig.GetBool("ads_enabled", true);
int    coins  = TwiceRemoteConfig.GetInt("coins_per_level", 50);
float  price  = TwiceRemoteConfig.GetFloat("hint_price", 250f);
string minVer = TwiceRemoteConfig.GetString("min_version", "1.0.0");

// Nested json value → your [Serializable] class (Unity JsonUtility):
[System.Serializable] public class GameSettings { public int adFreeUntilLevel; public int adReward; }
GameSettings gs = TwiceRemoteConfig.GetJson<GameSettings>("GameSettings");

// Re-apply when a fresh config arrives:
TwiceRemoteConfig.OnUpdated += () => ApplyConfig();

// Manual refresh any time:
TwiceRemoteConfig.Fetch(ok => Debug.Log("config v" + TwiceRemoteConfig.Version));
```
Manage keys in Twice admin → **Oyunlar** → your game → **Remote Config**. Types: `string`,
`int`, `float`, `bool`, `json`.

## Adapting to a game (bridge pattern)
This package is **game-agnostic** — it only exposes the `TwiceAnalytics.*` / `TwiceRemoteConfig.*`
API. Each game writes a small **bridge** (in the game project, *not* here) that forwards that
game's own events (level system, IAP, ads) to `TwiceAnalytics` and applies config values from
`TwiceRemoteConfig`. Game-specific dependencies (RevenueCat, AppLovin, etc.) stay in the game.

## Editor debugger
`Twice → Analytics Debugger` — compose/fire events, toggle consent, watch the live queue and last
server status while in Play Mode. Editor-only; never ships with a build.

## Backend
- `POST {endpointBaseUrl}/sdk/events` — headers `X-App-Key` + `Content-Type: application/json`.
- `GET  {endpointBaseUrl}/sdk/config`  — header `X-App-Key`; returns `{ ok, version, config }`.

## Versioning
Public `TwiceAnalytics.*` / `TwiceRemoteConfig.*` methods are the API contract. Changes follow
[SemVer](https://semver.org/); see `CHANGELOG.md`.
