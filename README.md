# Twice SDK — React Native & Expo

Lightweight SDK for the [Twice](https://www.twiceapps.co) backend: **analytics**
(events, sessions, revenue, typed Debug/Warning/Error events) + **remote config**
(typed key-value). Works in **Expo** (managed + dev client) and **bare React Native**
on iOS / Android / Web. Dependency-light, no PII by default — privacy-safe (GDPR/KVKK).

> This is the React Native / Expo SDK. The Unity SDK lives on the `main` branch of the same repo.

## Install

```bash
npm install @twiceapps/react-native
# or: yarn add @twiceapps/react-native
# or: npx expo install @twiceapps/react-native
```

Recommended (so queued events survive an app restart):

```bash
npx expo install @react-native-async-storage/async-storage
```

If AsyncStorage is not installed the SDK still works — it just keeps the queue in memory
until the app is killed.

## Quick start

Initialise once, as early as possible (e.g. in your root `App` component):

```tsx
import { useEffect } from 'react';
import { Twice, TwiceAnalytics } from '@twiceapps/react-native';

export default function App() {
  useEffect(() => {
    Twice.init({ apiKey: 'tw_xxxxxxxx' }); // Twice admin → Projeler → your project → API anahtarı
  }, []);

  // ...later, anywhere:
  // TwiceAnalytics.logEvent('app_open');
  // TwiceAnalytics.screenView('Home');
}
```

That's it. The SDK auto-tracks `session_start` / `session_end`, batches events, retries with
backoff, and tags events `sandbox` in dev (`__DEV__`) / `production` in release builds.

## Logging events

```ts
import { TwiceAnalytics } from '@twiceapps/react-native';

// Custom event with flat params:
TwiceAnalytics.logEvent('boss_defeated', { boss: 'golem', tries: 3 });

// Presets:
TwiceAnalytics.levelCompleted('1-3', { score: 1200, duration: 42.5 });
TwiceAnalytics.screenView('Shop');
TwiceAnalytics.purchase('com.game.coins', 4.99, 'USD');      // auto type: "purchase"
TwiceAnalytics.adRevenue(0.012, 'applovin', { adFormat: 'rewarded' }); // auto type: "ad"
```

Keep params **flat** (string / number / boolean) — like GA4 / GameAnalytics.

## Event types (Debug / Warning / Error / Purchase / Ad)

Every event has a **type** the Twice dashboard filters and splits by. Diagnostics use the typed
helpers; gameplay/business events use `logEvent` or the presets (already typed).

```ts
TwiceAnalytics.debugEvent('checkpoint', { where: 'boss_intro' });
TwiceAnalytics.warningEvent('low_memory');
TwiceAnalytics.errorEvent('save_failed', { slot: 2 });

try {
  risky();
} catch (e) {
  TwiceAnalytics.errorEvent('unhandled', e as Error); // message + stack ride along
}

// Explicit type on a custom event:
TwiceAnalytics.logEvent('payment_declined', 'error', { code: 'insufficient_funds' });
```

Valid types: `debug`, `warning`, `error`, `purchase`, `ad`, `general` (default). Events sent
without an explicit type are categorised by the backend from their name, so nothing is lost.

## Remote config

Per-project typed key-value, cached offline (instant next launch), with a `version` bump.

```ts
import { TwiceRemoteConfig } from '@twiceapps/react-native';

const adsOn = TwiceRemoteConfig.getBool('ads_enabled', true);
const coins = TwiceRemoteConfig.getInt('coins_per_level', 50);
const title = TwiceRemoteConfig.getString('promo_title', '');

const unsub = TwiceRemoteConfig.onUpdated(() => applyConfig()); // re-apply on fresh config
TwiceRemoteConfig.fetch();                                      // manual refresh
```

`Twice.init()` initialises remote config automatically (pass `remoteConfig: false` to skip).
Manage keys in Twice admin → **Projeler** → your project → **Remote Config**.

## Players

```ts
import { TwicePlayers } from '@twiceapps/react-native';

TwicePlayers.userId;                 // stable anonymous id (also stamped on every event)
TwicePlayers.setDisplayName('Ada');  // shown on leaderboards + admin; rides the next batch
TwicePlayers.displayName;            // "" if none
```

## Leaderboards

Sort direction, aggregation (last/min/max/sum) and reset frequency are configured per board
in the panel; the client just submits a score and reads the ranking. All methods return Promises.

```ts
import { TwiceLeaderboards } from '@twiceapps/react-native';

await TwiceLeaderboards.submit('high_score', 1200, 'Ada');
const top = await TwiceLeaderboards.getTop('high_score', 50);     // LeaderboardEntry[]
const me  = await TwiceLeaderboards.getMyRank('high_score');      // { found, rank, value, total, name }
const n   = await TwiceLeaderboards.getEntryCount('high_score');

// Last archived period (after a reset):
const prevTop = await TwiceLeaderboards.getTopBeforeReset('high_score', 50);
```

## Version check (forced / optional updates)

```ts
import { TwiceVersionCheck } from '@twiceapps/react-native';
import { Linking } from 'react-native';

const s = await TwiceVersionCheck.check(); // { action: 'none'|'optional'|'forced', appId, bundleId, ... }
if (s.updateAvailable) {
  // Show YOUR prompt (block input if s.isForced), then open the store:
  Linking.openURL(TwiceVersionCheck.storeUrl(s));
}
```

Configure the latest/minimum versions in Twice admin → **Projeler** → project → **Version Checker**.

## Push registration

Your app obtains the device token (e.g. `expo-notifications`) and hands it to the SDK, which
stores it against the current user for operator/segment pushes.

```ts
import * as Notifications from 'expo-notifications';
import { TwicePush } from '@twiceapps/react-native';

const { data: token } = await Notifications.getDevicePushTokenAsync();
await TwicePush.register(token);
// on logout: await TwicePush.unregister(token);
```

## Consent (GDPR / KVKK)

```ts
TwiceAnalytics.setConsent(false); // pause + clear the queue
TwiceAnalytics.setConsent(true);  // resume
```

Start opted-out with `Twice.init({ apiKey, consent: false })` and flip it once the user agrees.

## Options

| Option | Default | Description |
|---|---|---|
| `apiKey` (required) | — | Project key (`X-App-Key`). |
| `endpointBaseUrl` | `https://www.twiceapps.co/api/v1` | Backend base URL. |
| `appVersion` | auto (expo-constants) | Marketing version. |
| `build` | — | iOS CFBundleVersion / Android versionCode. |
| `platform` | `Platform.OS` mapped | `iOS` / `Android` / `Web` / … |
| `sandbox` | `__DEV__` | Force sandbox/production tagging. |
| `flushIntervalSeconds` | `15` | Auto-flush cadence. |
| `maxBatchSize` | `20` | Events per network batch. |
| `autoTrackSessions` | `true` | Auto `session_start` / `session_end`. |
| `consent` | `true` | Start collecting (set `false` to require opt-in). |
| `debug` | `false` | Verbose SDK console logs. |
| `userId` | persisted random id | Override the anonymous user id. |

## Identity & privacy

The user id is an anonymous random id persisted on the device (via AsyncStorage). The SDK
does **not** collect the IDFA/advertising id or raw IP. Country (2-letter) is derived
server-side from the request and stored without the IP. Disclose analytics use in your
privacy text. Pass your own `userId` to align with your auth system.

## For AI assistants

Adding Twice to a project? See **[AGENTS.md](./AGENTS.md)** — copy-paste integration steps,
patterns, and pitfalls written for AI coding agents.

## Backend / wire format

`POST {endpointBaseUrl}/sdk/events` (header `X-App-Key`):

```jsonc
{
  "session_id": "…", "user_id": "…", "platform": "iOS", "app_version": "1.2.0",
  "env": "production",
  "events": [
    { "event_id": "hex", "name": "level_completed", "type": "general", "ts": 1718450000,
      "params": { "level": "1-3", "score": 1200 } }
  ]
}
```

`GET {endpointBaseUrl}/sdk/config` (header `X-App-Key`) → `{ ok, version, config }`.
Identical contract to the Unity SDK, so both feed the same dashboards.

## License

MIT.
