/**
 * Public types for the Twice React Native / Expo SDK.
 */

/** Event category the backend dashboard filters and splits by. */
export type EventType = 'debug' | 'warning' | 'error' | 'purchase' | 'ad' | 'general';

/** A flat param value. Keep params flat (no nested objects) — like GA4 / GameAnalytics. */
export type ParamValue = string | number | boolean | null;

/** Event params: a flat map of name → value. */
export type EventParams = Record<string, ParamValue>;

/** Options for {@link TwiceAnalytics.init}. Only `apiKey` is required. */
export interface TwiceOptions {
  /** Project API key (`X-App-Key`) from Twice admin → Projeler → your project. */
  apiKey: string;
  /** Backend base URL. Default: `https://www.twiceapps.co/api/v1`. */
  endpointBaseUrl?: string;
  /** Marketing app version (e.g. "1.2.0"). Auto-detected from expo-constants when available. */
  appVersion?: string;
  /** Build number (iOS CFBundleVersion / Android versionCode). Optional. */
  build?: string | number;
  /** Platform override. Default: `Platform.OS` ("ios" | "android" | "web" | …). */
  platform?: string;
  /**
   * Force sandbox (true) / production (false) tagging. Default: sandbox in `__DEV__`,
   * production otherwise. Sandbox events are kept out of the production dashboard.
   */
  sandbox?: boolean;
  /** Seconds between automatic flushes. Default: 15. */
  flushIntervalSeconds?: number;
  /** Max events per network batch. Default: 20 (clamped 1–200). */
  maxBatchSize?: number;
  /** Auto-track `session_start` / `session_end`. Default: true. */
  autoTrackSessions?: boolean;
  /** Verbose console logging for debugging the SDK itself. Default: false. */
  debug?: boolean;
  /** Override the user id. Default: a random id persisted on the device. */
  userId?: string;
  /** Start with consent granted. Default: true (set false to require opt-in first). */
  consent?: boolean;
}

/** Live snapshot of SDK state (see {@link TwiceAnalytics.getDebugInfo}). */
export interface DebugInfo {
  initialized: boolean;
  consent: boolean;
  environment: 'sandbox' | 'production';
  pending: number;
  userId: string;
  sessionId: string;
  platform: string;
  appVersion: string;
  endpoint: string;
  lastStatus: string;
}
