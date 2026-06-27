import { TwiceClient } from './client';
import { DebugInfo, EventParams, EventType, ParamValue, TwiceOptions } from './types';

let _client: TwiceClient | null = null;
/** The lazily-created singleton engine. */
function client(): TwiceClient {
  if (!_client) _client = new TwiceClient();
  return _client;
}

function withExtra(base: EventParams, extra?: EventParams | null): EventParams {
  return extra ? { ...base, ...extra } : base;
}

/** Public analytics API. Mirrors the Unity SDK's `TwiceAnalytics`. */
export interface TwiceAnalyticsApi {
  /** Initialise the SDK. Call once, early (e.g. in your App component). Async (loads persisted state). */
  init(options: TwiceOptions): Promise<void>;
  /** Enable/disable collection (GDPR/KVKK). When false, the queue is cleared and nothing is sent. */
  setConsent(granted: boolean): void;
  /** Force sandbox (true) / production (false) tagging at runtime. */
  setSandbox(sandbox: boolean): void;
  /** Attach a property merged into every subsequent event's params (event params win on collision). */
  setUserProperty(key: string, value: ParamValue): void;
  /** Set a friendly display name for the current player (rides the next batch; persisted). */
  setDisplayName(name: string): void;
  /** The anonymous, persisted user id stamped on every event. */
  getUserId(): string;
  /** Request an immediate (async) flush of the queued events. */
  flush(): void;
  /** Live snapshot of SDK state (safe any time). */
  getDebugInfo(): DebugInfo;

  /** Log an event (type defaults to "general"). */
  logEvent(name: string, params?: EventParams): void;
  /** Log an event with an explicit type ("debug" | "warning" | "error" | "purchase" | "ad" | "general"). */
  logEvent(name: string, type: EventType, params?: EventParams): void;

  /** Log a developer/diagnostic event (type "debug"). */
  debugEvent(name: string, params?: EventParams): void;
  /** Log a warning event (type "warning"). */
  warningEvent(name: string, params?: EventParams): void;
  /** Log an error event (type "error"). */
  errorEvent(name: string, params?: EventParams): void;
  /** Log an error from a caught Error — message, name and stack ride in params (type "error"). */
  errorEvent(name: string, error: Error, extra?: EventParams): void;

  // ---- preset helpers ----
  levelStarted(level: string | number, extra?: EventParams): void;
  levelCompleted(level: string | number, opts?: { score?: number; duration?: number }, extra?: EventParams): void;
  levelFailed(level: string | number, reason?: string, extra?: EventParams): void;
  tutorialCompleted(step?: string, extra?: EventParams): void;
  screenView(screen: string): void;
  /** Auto-tagged type "purchase". `price` is the local price; `currency` an ISO code (e.g. "USD"). */
  purchase(productId: string, price: number, currency: string, extra?: EventParams): void;
  /** Auto-tagged type "ad". */
  adWatched(placement: string, extra?: EventParams): void;
  /** Impression-level ad revenue in USD (mediation reports USD). Auto-tagged type "ad". */
  adRevenue(revenue: number, network: string, opts?: { placement?: string; adFormat?: string }, extra?: EventParams): void;
  rewardClaimed(reward: string, extra?: EventParams): void;
}

export const TwiceAnalytics: TwiceAnalyticsApi = {
  init(options: TwiceOptions): Promise<void> {
    return client().init(options);
  },
  setConsent(granted: boolean): void {
    client().setConsent(granted);
  },
  setSandbox(sandbox: boolean): void {
    client().setSandbox(sandbox);
  },
  setUserProperty(key: string, value: ParamValue): void {
    client().setUserProperty(key, value);
  },
  setDisplayName(name: string): void {
    client().setDisplayName(name);
  },
  getUserId(): string {
    return client().getUserId();
  },
  flush(): void {
    client().requestFlush();
  },
  getDebugInfo(): DebugInfo {
    return client().getDebugInfo();
  },

  logEvent(name: string, a?: EventType | EventParams, b?: EventParams): void {
    if (typeof a === 'string') client().enqueue(name, b, a);
    else client().enqueue(name, a, null);
  },

  debugEvent(name: string, params?: EventParams): void {
    client().enqueue(name, params, 'debug');
  },
  warningEvent(name: string, params?: EventParams): void {
    client().enqueue(name, params, 'warning');
  },
  errorEvent(name: string, a?: EventParams | Error, b?: EventParams): void {
    if (a instanceof Error) {
      const p: EventParams = { message: a.message, exception: a.name };
      if (a.stack) p.stack = a.stack;
      client().enqueue(name, b ? { ...p, ...b } : p, 'error');
    } else {
      client().enqueue(name, a, 'error');
    }
  },

  levelStarted(level: string | number, extra?: EventParams): void {
    client().enqueue('level_started', withExtra({ level }, extra));
  },
  levelCompleted(level: string | number, opts?: { score?: number; duration?: number }, extra?: EventParams): void {
    const base: EventParams = { level };
    if (opts && typeof opts.score === 'number') base.score = opts.score;
    if (opts && typeof opts.duration === 'number') base.duration = opts.duration;
    client().enqueue('level_completed', withExtra(base, extra));
  },
  levelFailed(level: string | number, reason?: string, extra?: EventParams): void {
    const base: EventParams = { level };
    if (reason) base.reason = reason;
    client().enqueue('level_failed', withExtra(base, extra));
  },
  tutorialCompleted(step?: string, extra?: EventParams): void {
    const base: EventParams = {};
    if (step) base.step = step;
    client().enqueue('tutorial_completed', withExtra(base, extra));
  },
  screenView(screen: string): void {
    client().enqueue('screen_view', { screen });
  },
  purchase(productId: string, price: number, currency: string, extra?: EventParams): void {
    client().enqueue('purchase', withExtra({ product_id: productId, price, currency }, extra), 'purchase');
  },
  adWatched(placement: string, extra?: EventParams): void {
    client().enqueue('ad_watched', withExtra({ placement }, extra), 'ad');
  },
  adRevenue(revenue: number, network: string, opts?: { placement?: string; adFormat?: string }, extra?: EventParams): void {
    const base: EventParams = { revenue, network: network || 'unknown' };
    if (opts && opts.placement) base.placement = opts.placement;
    if (opts && opts.adFormat) base.ad_format = opts.adFormat;
    client().enqueue('ad_revenue', withExtra(base, extra), 'ad');
  },
  rewardClaimed(reward: string, extra?: EventParams): void {
    client().enqueue('reward_claimed', withExtra({ reward }, extra));
  },
};
