# Twice SDK — Unity

Lightweight SDK for the Twice backend: **analytics** (events, sessions,
revenue) + **remote config** (typed key-value, PlayFab Title-Data style).
Works on **iOS, Android, WebGL, Windows, macOS and the Unity Editor**. No threads,
no PII — drop-in and privacy-safe (GDPR/KVKK). Requires **Unity 2021.3 LTS+** and
**Newtonsoft.Json** (`com.unity.nuget.newtonsoft-json`, installed automatically as a package dependency).

## Install (UPM via Git URL)
Unity → `Window → Package Manager → + → Add package from git URL…`:
```
https://github.com/Twice-Apps/TwiceSDK.git
```
Or add to `Packages/manifest.json`:
```json
"co.twiceapps.sdk": "https://github.com/Twice-Apps/TwiceSDK.git"
```
This pulls the latest commit. Once you tag releases you can pin a version (e.g. `#1.0.0`) for
reproducible builds.

## Setup
1. `Twice → Twice SDK Settings` (creates `Assets/Resources/TwiceSettings.asset` if missing and opens it), or `Assets → Create → Twice → SDK Settings`.
2. Move the asset into a `Resources` folder, keep the name `TwiceSettings`
   (e.g. `Assets/Resources/TwiceSettings.asset`) so it auto-initialises at boot.
3. Paste your project key (`X-App-Key`) into the `apiKey` field
   (Twice admin → **Projeler** → your project → API anahtarı).

> The settings asset (with your API key) lives in **your game**, never in this package.

## Analytics
```csharp
using TwiceSDK.Analytics;

TwiceAnalytics.SetConsent(true);
TwiceAnalytics.LevelCompleted("1-3", score: 1200, duration: 42.5f);
TwiceAnalytics.LogEvent("boss_defeated", new Dictionary<string, object> { { "boss", "golem" }, { "tries", 3 } });
TwiceAnalytics.Flush();
```
Events carry an `event_id` (GUID) for idempotent at-least-once delivery, and an `env`
(`sandbox`/`production`) tag derived automatically (Editor/Dev/TestFlight → sandbox).

### Event types (Debug / Warning / Error / Purchase / Ad)
Tag an event with a **type** the dashboard filters and splits by. Diagnostics use the typed
log helpers; gameplay/business events use `LogEvent` or the presets (already typed).
```csharp
TwiceAnalytics.DebugEvent("checkpoint", new Dictionary<string, object> { { "where", "boss_intro" } });
TwiceAnalytics.WarningEvent("low_memory");
TwiceAnalytics.ErrorEvent("save_failed", new Dictionary<string, object> { { "slot", 2 } });

try { Risky(); }
catch (Exception ex) { TwiceAnalytics.ErrorEvent("unhandled", ex); } // message + stack ride along

// Purchase / AdWatched / AdRevenue are auto-tagged "purchase" / "ad":
TwiceAnalytics.Purchase("com.game.coins", 4.99, "USD");

// Subscriptions: trials & cancellations carry NO revenue; only purchase/renewal do.
TwiceAnalytics.TrialStarted("com.game.pro.weekly", "Pro Weekly");
TwiceAnalytics.SubscriptionRenewed("com.game.pro.weekly", 4.99, "USD", "Pro Weekly");
TwiceAnalytics.SubscriptionCancelled("com.game.pro.weekly", "Pro Weekly");
```
Valid types: `debug`, `warning`, `error`, `purchase`, `ad`, `general` (default). Events sent
without an explicit type are categorised by the backend from their name, so existing data is
covered too.

## Remote Config
Reads a per-game typed key-value store from the backend, caches it (offline + instant next
launch), and bumps a `version` so the client only changes when the config does.

```csharp
using TwiceSDK.RemoteConfig;

// Auto-fetched at boot (toggle on the settings asset). Read with safe defaults:
bool   adsOn  = TwiceRemoteConfig.GetBool("ads_enabled", true);
int    coins  = TwiceRemoteConfig.GetInt("coins_per_level", 50);
float  price  = TwiceRemoteConfig.GetFloat("hint_price", 250f);
string minVer = TwiceRemoteConfig.GetString("min_version", "1.0.0");

// Nested json value → your POCO / class (parsed with Newtonsoft):
public class GameSettings { public int adFreeUntilLevel; public int adReward; }
GameSettings gs = TwiceRemoteConfig.GetJson<GameSettings>("GameSettings");

// Re-apply when a fresh config arrives:
TwiceRemoteConfig.OnUpdated += () => ApplyConfig();

// Manual refresh any time:
TwiceRemoteConfig.Fetch(ok => Debug.Log("config v" + TwiceRemoteConfig.Version));
```
Manage keys in Twice admin → **Projeler** → your project → **Remote Config**. Types: `string`,
`int`, `float`, `bool`, `json`.

### A/B experiments
Experiments are defined in the panel (Remote Config → **Deneyler**) as variant layers over the base
config: only the keys listed in the experiment differ per variant. The SDK identifies the player on
every config fetch (`X-User-Id`, `X-First-Open`), receives the variant's values merged into `config`,
and keeps the assignment with the cache. Nothing changes in how you read keys — `GetInt(...)` simply
returns the variant's value. The player's bucket is stamped on every analytics event as the
`ab_group` (`"experiment:variant"`), `experiment_id` and `variant_id` user properties, and an
`experiment_assigned` event is logged whenever the assignment changes, so the dashboard can split
retention, playtime and revenue by variant.

```csharp
string exp = TwiceRemoteConfig.ExperimentId;   // "" when not enrolled
string var = TwiceRemoteConfig.Variant;        // "A", "B", …
TwiceRemoteConfig.OnExperimentChanged += a => Debug.Log(a == null ? "left experiment" : a.Group);
```
Do not set `ab_group` yourself any more — the SDK owns it.

## Wallet (panel gives / takes currency)
The panel (Wallet module, or Players → player) queues grants — "+100 coin", "−50 gem" — for a
player. The game owns the balance, so it registers each currency once; the SDK pulls the player's
grants, applies them through your callback and reports back.

```csharp
using TwiceSDK.Wallet;

TwiceWallet.Register("coin",
    () => CurrencyManager.Instance.Coins,                 // current balance
    delta => {                                            // + give / − take; save it
        if (delta < 0 && CurrencyManager.Instance.Coins < -delta) return false; // refuse → "rejected"
        CurrencyManager.Instance.AddMoney(delta);
        return true;
    },
    "Coins");                                             // optional: name shown in the panel (+ decimals)
TwiceWallet.OnGrantApplied += g => Toast($"+{g.Amount} {g.Currency}");
TwiceWallet.Sync();   // optional: e.g. when the shop opens (also automatic on start/resume)
```
The panel's currency list comes from these `Register` calls (key, display name, decimals); there is
nothing to define in the panel. Grants for a currency this build did not register stay queued (a later
build can apply them). Acks and balances travel as analytics
events (`wallet_grant_applied`, `wallet_grant_rejected`, `wallet_balance`).

## Namespaces
- `TwiceSDK` — shared settings (`TwiceSettings`, `EnvironmentMode`).
- `TwiceSDK.Analytics` — `TwiceAnalytics`.
- `TwiceSDK.RemoteConfig` — `TwiceRemoteConfig`.
- `TwiceSDK.Wallet` — `TwiceWallet`.

## Adapting to a game (bridge pattern)
This package is **game-agnostic** — it only exposes the `TwiceAnalytics.*` / `TwiceRemoteConfig.*`
API. Each game writes a small **bridge** (in the game project, *not* here) that forwards that
game's own events (level system, IAP, ads) to `TwiceAnalytics` and applies config values from
`TwiceRemoteConfig`. Game-specific dependencies (RevenueCat, AppLovin, etc.) stay in the game.

## Editor debugger
`Twice → Analytics Debugger` — compose/fire events, toggle consent, watch the live queue and last
server status while in Play Mode. Editor-only; never ships with a build.

## Twice Package Hub (team asset store)
`Twice → Twice Package Hub` — the team's own `.unitypackage` library, hosted on twicehub
(`hub.twiceapps.co`), not GitHub. Editor-only, its own assembly (`TwiceSDK.PackageManager.Editor`,
references nothing in the runtime); never ships with a build.

- **Admins only.** The window shows nothing but *Twicehub ile bağlan* until this machine is
  connected: it opens the panel's approval page, an admin presses *Onayla*, the token arrives by
  itself (per person, per machine, EditorPrefs only — never in the project). Panel ▸ Twice Packages ▸
  *Bağlı bilgisayarlarım* revokes a machine; a revoked or demoted user drops back to the connect screen.
- **Göz at / Yüklü** — catalog with categories, versions, changelogs; import any version with its
  dependencies (Twice packages + UPM ids); every asset a package installed is recorded by guid in
  `ProjectSettings/TwicePackages.json`, so *Kaldır* and clean updates remove exactly that.
- **Yükle** — new package or new version, exported straight from project folders or from a
  `.unitypackage`; 8 MB chunks, sha256 verified on the server, up to 4 GB per version.
- **Toplu yükle** — *Sahip olduklarımı listele* reads the whole Asset Store library of the signed-in
  Unity account; *Eksikleri indir ve yükle* downloads what is neither on the server nor on disk with
  Unity's own downloader, then uploads it with name / version / publisher / category read from the
  package. Anything a teammate already uploaded is skipped. Also scans any folder of `.unitypackage`s.
  Uses Package Manager internals (verified on Unity 6000.0 and 6000.3); on a Unity where they differ
  the section says so and the folder scan still works.
- Downloads are cached machine-wide (`%LOCALAPPDATA%/TwicePackages`, macOS `~/Library/Caches/TwicePackages`).

Server side lives in twicehub-web: `includes/packages.php`, `hub/packages_api.php`, `hub/packages.php`.

## Backend
- `POST {endpointBaseUrl}/sdk/events` — headers `X-App-Key` + `Content-Type: application/json`.
- `GET  {endpointBaseUrl}/sdk/config`  — headers `X-App-Key` (+ `X-User-Id`, `X-First-Open` when analytics
  is on); returns `{ ok, version, config }`, plus `experiment` (`{ id, variant, rev, source }` or `null`)
  for identified players.
- `GET  {endpointBaseUrl}/sdk/wallet/grants?user_id=` — header `X-App-Key`; returns `{ ok, grants:
  [{ id, currency, amount, note, created_at }] }` (pending + delivered-but-unacknowledged).

## Versioning
Public `TwiceAnalytics.*` / `TwiceRemoteConfig.*` methods are the API contract. Changes follow
[SemVer](https://semver.org/); see `CHANGELOG.md`.
