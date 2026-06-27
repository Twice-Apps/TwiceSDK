# Twice SDK — guide for AI coding agents

Instructions for an AI assistant integrating **`@twiceapps/react-native`** into an Expo /
React Native app. Follow these steps; they are deterministic and safe.

## What this SDK does
Analytics (events, sessions, revenue) + remote config for the Twice backend. One init call,
then `TwiceAnalytics.logEvent(...)` anywhere. Events are queued, batched, retried, and persisted
offline. Works in Expo (managed + dev client) and bare RN on iOS/Android/Web.

## Install
```bash
npm install @twiceapps/react-native
# offline persistence (recommended):
npx expo install @react-native-async-storage/async-storage
```
If the project uses plain RN (not Expo), install async-storage with `npm install` and run pods
on iOS (`cd ios && pod install`). The SDK works without async-storage (in-memory queue).

## Step 1 — Initialise once, early
Put a single `Twice.init({ apiKey })` at app startup. The API key is the project's `X-App-Key`
from Twice admin → Projeler → project → API anahtarı. If you don't have it, ask the user; do NOT
invent one.

```tsx
// App.tsx (or app/_layout.tsx for expo-router)
import { useEffect } from 'react';
import { Twice } from '@twiceapps/react-native';

export default function App() {
  useEffect(() => {
    Twice.init({ apiKey: process.env.EXPO_PUBLIC_TWICE_KEY ?? 'tw_xxx' });
  }, []);
  // ...rest of the app
}
```
- Prefer reading the key from an env var (`EXPO_PUBLIC_TWICE_KEY`) over hardcoding.
- `init` is idempotent and async; you do NOT need to await it before logging events.
- For `expo-router`, init in the root `app/_layout.tsx`.

## Step 2 — Log events
```ts
import { TwiceAnalytics } from '@twiceapps/react-native';

TwiceAnalytics.logEvent('screen_view', { screen: 'Home' });   // or TwiceAnalytics.screenView('Home')
TwiceAnalytics.logEvent('button_tap', { id: 'checkout' });
TwiceAnalytics.purchase('com.app.pro', 9.99, 'USD');          // revenue, auto type "purchase"
TwiceAnalytics.adRevenue(0.01, 'admob', { adFormat: 'rewarded' });
```

### Typed diagnostic events
```ts
TwiceAnalytics.debugEvent('cache_miss', { key });
TwiceAnalytics.warningEvent('slow_request', { ms: 1900 });
TwiceAnalytics.errorEvent('checkout_failed', { code });
try { risky(); } catch (e) { TwiceAnalytics.errorEvent('unhandled', e as Error); }
```
Types: `debug | warning | error | purchase | ad | general`. The dashboard filters/splits by them.

## Step 3 — Remote config (optional)
```ts
import { TwiceRemoteConfig } from '@twiceapps/react-native';
const enabled = TwiceRemoteConfig.getBool('feature_x', false);
TwiceRemoteConfig.onUpdated(() => rerenderOrReapply());
```
`Twice.init()` already initialises remote config (skip with `remoteConfig: false`).

## Step 4 — Other modules (use as needed)
All read config/identity from the initialised SDK — no separate init. Leaderboards / version
check / push methods return Promises; await them.
```ts
import { TwicePlayers, TwiceLeaderboards, TwiceVersionCheck, TwicePush } from '@twiceapps/react-native';

TwicePlayers.setDisplayName('Ada');                       // shows on leaderboards + admin
await TwiceLeaderboards.submit('high_score', 1200);
const top = await TwiceLeaderboards.getTop('high_score', 50);

const s = await TwiceVersionCheck.check();                // forced/optional update gating
if (s.isForced) { /* block UI */ } // open store: Linking.openURL(TwiceVersionCheck.storeUrl(s))

// Push: app gets the token (expo-notifications), SDK registers it for the user:
await TwicePush.register(deviceToken);
```
Module parity with Unity: Analytics, Remote Config, Players, Leaderboards, Version Check, Push.
Monetization / Functions / operator Notifications / Settings are backend/dashboard features driven
by events — there is no separate client API for them (same as Unity).

## Event naming conventions (follow these)
- `snake_case`, lowercase, allowed chars `[a-z0-9_.:-]`, ≤ 64 chars.
- Keep params **flat**: string / number / boolean only (no nested objects/arrays).
- Reuse stable names across releases so charts stay continuous.
- Common names: `app_open`, `screen_view`, `level_started`, `level_completed`, `purchase`,
  `ad_revenue`, `tutorial_completed`, `share`, `signup`, `login`.

## Consent (GDPR/KVKK)
If the app shows a consent prompt, start opted-out and flip on accept:
```ts
Twice.init({ apiKey, consent: false });
// on accept:
TwiceAnalytics.setConsent(true);
```

## DO NOT
- ❌ Call `init` repeatedly in a re-rendering component body — use `useEffect([])` / module scope.
- ❌ Put nested objects in event params — flatten them first.
- ❌ Hardcode or guess an API key — read from env or ask the user.
- ❌ Log PII (emails, names) in params. Use `setUserProperty` / `setDisplayName` deliberately.
- ❌ Add a different analytics SDK for the same purpose unless asked.

## Verify
- Dev builds tag events `sandbox`; check the **Sandbox** tab in the Twice dashboard.
- Set `Twice.init({ apiKey, debug: true })` to see `[Twice]` console logs while wiring up.
- `TwiceAnalytics.getDebugInfo()` returns `{ pending, environment, lastStatus, ... }`.

## API quick reference
```
Twice.init(options)                              // analytics + remote config
TwiceAnalytics.init(options) / .flush() / .getDebugInfo()
TwiceAnalytics.logEvent(name, params?)
TwiceAnalytics.logEvent(name, type, params?)     // type: EventType
TwiceAnalytics.debugEvent / warningEvent / errorEvent(name, params?)
TwiceAnalytics.errorEvent(name, error, extra?)   // Error overload
TwiceAnalytics.levelStarted / levelCompleted / levelFailed / tutorialCompleted / screenView
TwiceAnalytics.purchase(productId, price, currency, extra?)
TwiceAnalytics.adWatched / adRevenue / rewardClaimed
TwiceAnalytics.setConsent(bool) / setSandbox(bool) / setUserProperty(k,v) / setDisplayName(name) / getUserId()
TwiceRemoteConfig.getBool/getInt/getFloat/getNumber/getString/getJson(key, fallback)
TwiceRemoteConfig.fetch() / onUpdated(cb) / version
TwicePlayers.userId / displayName / setDisplayName(name)
await TwiceLeaderboards.submit(boardId, score, playerName?)
await TwiceLeaderboards.getTop(boardId, count) / getMyRank(boardId) / getEntryCount(boardId)
await TwiceLeaderboards.getTopBeforeReset(boardId, count) / getMyRankBeforeReset(boardId)
await TwiceVersionCheck.check({platform?,version?,build?})  // → UpdateStatus; .storeUrl(status)
await TwicePush.register(token, {platform?,env?}) / unregister(token)
```
Options: `{ apiKey, endpointBaseUrl?, appVersion?, build?, platform?, sandbox?, flushIntervalSeconds?, maxBatchSize?, autoTrackSessions?, consent?, debug?, userId? }`.
