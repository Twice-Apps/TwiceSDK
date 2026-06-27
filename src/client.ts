import { AppState, AppStateStatus, Platform } from 'react-native';
import { Core } from './core';
import { Storage } from './storage';
import { uuid } from './uuid';
import { DebugInfo, EventParams, TwiceOptions } from './types';

// Module-scoped ambients (do not leak to consumers — this file is a module).
declare const require: (name: string) => any;
declare const __DEV__: boolean;

const KNOWN_TYPES = ['debug', 'warning', 'error', 'purchase', 'ad', 'general'];
const DEFAULT_ENDPOINT = 'https://www.twiceapps.co/api/v1';
const USER_ID_KEY = 'twice_user_id';
const DISPLAY_NAME_KEY = 'twice_display_name';
const CONSENT_KEY = 'twice_consent';
const QUEUE_KEY = 'twice_queue';
const MAX_QUEUE = 1000;
const MAX_BACKOFF = 60;
const NEW_SESSION_AFTER_MIN = 30;

interface QueuedEvent {
  eventId: string;
  sessionId: string;
  ts: number;
  name: string;
  type: string; // '' = general (omitted on the wire; backend derives)
  params: EventParams;
}

/** Internal engine: owns the queue, identity, session, persistence and network. */
export class TwiceClient {
  private apiKey = '';
  private endpoint = DEFAULT_ENDPOINT;
  private flushInterval = 15;
  private maxBatch = 20;
  private autoSessions = true;
  private debug = false;
  private sandboxOverride: boolean | null = null;
  private platform = '';
  private appVersion = '';
  private build = '';

  private userId = '';
  private displayName = '';
  private sessionId = '';
  private sessionStart = 0;
  private bgSince: number | null = null;
  private userProps: EventParams = {};

  private queue: QueuedEvent[] = [];
  private consent = true;
  private configured = false;
  private identityReady = false;
  private sending = false;
  private currentDelay = 15;
  private flushTimer: ReturnType<typeof setTimeout> | null = null;
  private lastStatus = 'Idle';

  async init(options: TwiceOptions): Promise<void> {
    if (!options || !options.apiKey) {
      this.warn('init: `apiKey` is required (Twice admin → Projeler → your project → API anahtarı).');
    }
    this.apiKey = (options.apiKey || '').trim();
    if (options.endpointBaseUrl) this.endpoint = options.endpointBaseUrl.trim();
    if (typeof options.flushIntervalSeconds === 'number') this.flushInterval = Math.max(1, options.flushIntervalSeconds);
    if (typeof options.maxBatchSize === 'number') this.maxBatch = clamp(options.maxBatchSize, 1, 200);
    if (typeof options.autoTrackSessions === 'boolean') this.autoSessions = options.autoTrackSessions;
    if (typeof options.debug === 'boolean') this.debug = options.debug;
    if (typeof options.sandbox === 'boolean') this.sandboxOverride = options.sandbox;
    this.platform = options.platform || resolvePlatform();
    this.appVersion = options.appVersion || detectAppVersion();
    if (options.build != null) this.build = String(options.build);
    this.currentDelay = this.flushInterval;

    // Publish config so the other modules (players/leaderboards/version/push) can read it.
    Core.apiKey = this.apiKey;
    Core.endpoint = this.endpoint;
    Core.platform = this.platform;
    Core.appVersion = this.appVersion;
    Core.build = this.build;
    Core.sandbox = this.isSandbox;

    if (this.configured) return; // reconfigure is idempotent — don't restart session/loops
    this.configured = true;

    await this.loadState(options);
    this.startSession(this.autoSessions);
    AppState.addEventListener('change', this.onAppState);
    this.scheduleFlush();
    if (!this.apiKey) this.warn('No API key set — events will not be sent.');
  }

  // ---- public-facing engine ops -------------------------------------------

  setConsent(granted: boolean): void {
    this.consent = granted;
    void Storage.set(CONSENT_KEY, granted ? '1' : '0');
    if (!granted) {
      this.queue = [];
      void this.persistQueue();
      this.log('consent revoked — queue cleared, collection paused');
    } else {
      this.log('consent granted');
    }
  }

  setSandbox(sandbox: boolean): void {
    this.sandboxOverride = sandbox;
    this.log('environment set to ' + (sandbox ? 'sandbox' : 'production'));
  }

  setUserProperty(key: string, value: EventParams[string]): void {
    if (!key) return;
    this.userProps[key] = value;
  }

  setDisplayName(name: string): void {
    this.displayName = (name || '').trim();
    Core.displayName = this.displayName;
    void Storage.set(DISPLAY_NAME_KEY, this.displayName);
    this.requestFlush();
  }

  getUserId(): string {
    return this.userId;
  }

  getDisplayName(): string {
    return this.displayName;
  }

  /** Enqueue an event. `type` '' / unknown → general (backend derives). */
  enqueue(name: string, params?: EventParams | null, type?: string | null): void {
    if (!this.consent) return;
    const clean = sanitizeName(name);
    const cleanType = normalizeType(type);
    let merged: EventParams = params ? { ...params } : {};
    if (Object.keys(this.userProps).length) merged = { ...this.userProps, ...merged };

    this.queue.push({ eventId: uuid(), sessionId: this.sessionId, ts: nowSec(), name: clean, type: cleanType, params: merged });
    if (this.queue.length > MAX_QUEUE) this.queue.splice(0, this.queue.length - MAX_QUEUE);
    void this.persistQueue();
    this.log(`queued '${clean}' (${this.queue.length} pending)`);
    if (this.queue.length >= this.maxBatch) this.requestFlush();
  }

  requestFlush(): void {
    if (!this.consent || this.sending || !this.identityReady) return;
    void this.drain();
  }

  getDebugInfo(): DebugInfo {
    return {
      initialized: this.configured,
      consent: this.consent,
      environment: this.isSandbox ? 'sandbox' : 'production',
      pending: this.queue.length,
      userId: this.userId,
      sessionId: this.sessionId,
      platform: this.platform,
      appVersion: this.appVersion,
      endpoint: this.endpoint,
      lastStatus: this.lastStatus,
    };
  }

  // ---- session ------------------------------------------------------------

  private startSession(logStart: boolean): void {
    this.sessionId = uuid();
    this.sessionStart = Date.now();
    if (logStart) {
      this.enqueue('session_start', { os: deviceOS(), platform: this.platform });
    }
  }

  private endSession(): void {
    if (!this.autoSessions) return;
    const duration = Math.round((Date.now() - this.sessionStart) / 1000);
    this.enqueue('session_end', { duration });
  }

  private onAppState = (state: AppStateStatus): void => {
    if (state === 'background' || state === 'inactive') {
      this.bgSince = Date.now();
      void this.persistQueue();
      this.requestFlush();
    } else if (state === 'active' && this.bgSince != null) {
      const mins = (Date.now() - this.bgSince) / 60000;
      this.bgSince = null;
      if (mins >= NEW_SESSION_AFTER_MIN) {
        this.endSession();
        this.startSession(this.autoSessions);
        this.log(`resumed after ${Math.round(mins)} min — started a new session`);
      }
    }
  };

  // ---- environment --------------------------------------------------------

  private get isSandbox(): boolean {
    if (this.sandboxOverride !== null) return this.sandboxOverride;
    return typeof __DEV__ !== 'undefined' && __DEV__ === true;
  }

  // ---- network ------------------------------------------------------------

  private scheduleFlush(): void {
    if (this.flushTimer) clearTimeout(this.flushTimer);
    this.flushTimer = setTimeout(() => { void this.tick(); }, this.currentDelay * 1000);
  }

  private async tick(): Promise<void> {
    if (this.consent && this.queue.length > 0 && !this.sending) {
      await this.drain();
    } else if (this.queue.length === 0) {
      this.currentDelay = this.flushInterval;
    }
    this.scheduleFlush();
  }

  private async drain(): Promise<void> {
    if (this.sending) return;
    this.sending = true;
    try {
      // eslint-disable-next-line no-constant-condition
      while (true) {
        if (!this.consent || !this.apiKey || !this.identityReady) break;
        const batch = this.takeBatch();
        if (batch.length === 0) { this.currentDelay = this.flushInterval; break; }

        const result = await this.sendBatch(batch);
        if (result === 'ok' || result === 'drop') {
          this.removeSent(batch.length, batch[0].sessionId);
          void this.persistQueue();
          this.currentDelay = this.flushInterval;
        } else {
          this.currentDelay = Math.min(Math.max(this.currentDelay, this.flushInterval) * 2, MAX_BACKOFF);
          this.log(`flush failed, backing off ${this.currentDelay}s`);
          break;
        }
      }
    } finally {
      this.sending = false;
    }
  }

  /** Up to maxBatch leading events that share the first event's session id. */
  private takeBatch(): QueuedEvent[] {
    const batch: QueuedEvent[] = [];
    if (this.queue.length === 0) return batch;
    const sid = this.queue[0].sessionId;
    for (let i = 0; i < this.queue.length && batch.length < this.maxBatch; i++) {
      if (this.queue[i].sessionId !== sid) break;
      batch.push(this.queue[i]);
    }
    return batch;
  }

  private removeSent(n: number, sessionId: string): void {
    let removed = 0;
    while (removed < n && this.queue.length > 0 && this.queue[0].sessionId === sessionId) {
      this.queue.shift();
      removed++;
    }
  }

  private async sendBatch(batch: QueuedEvent[]): Promise<'ok' | 'drop' | 'retry'> {
    const url = this.endpoint.replace(/\/+$/, '') + '/sdk/events';
    const body = this.buildBody(batch);
    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-App-Key': this.apiKey },
        body,
      });
      const code = res.status;
      if (code >= 200 && code < 300) {
        this.lastStatus = `OK ${code} — sent ${batch.length} event(s)`;
        this.log(this.lastStatus);
        return 'ok';
      }
      if (code === 401) {
        this.lastStatus = '401 invalid_app_key (dropped)';
        this.warn('401 invalid_app_key — check your API key. These events are dropped.');
        return 'drop';
      }
      if (code === 400 || code === 422) {
        this.lastStatus = `${code} rejected (dropped)`;
        this.warn(`${code} rejected the batch (dropped to avoid a retry loop).`);
        return 'drop';
      }
      this.lastStatus = `failed (code=${code}) — will retry`;
      return 'retry';
    } catch {
      this.lastStatus = 'network error — will retry';
      return 'retry';
    }
  }

  private buildBody(batch: QueuedEvent[]): string {
    const envelope: Record<string, unknown> = {
      session_id: clampStr(batch[0].sessionId, 80),
      user_id: clampStr(this.userId, 80),
      platform: this.platform,
      app_version: this.appVersion,
      env: this.isSandbox ? 'sandbox' : 'production',
      events: batch.map((e) => {
        const o: Record<string, unknown> = { event_id: e.eventId, name: e.name };
        if (e.type) o.type = e.type;
        o.ts = e.ts;
        o.params = e.params || {};
        return o;
      }),
    };
    if (this.displayName) envelope.display_name = clampStr(this.displayName, 40);
    if (this.build) envelope.build = this.build;
    return JSON.stringify(envelope);
  }

  // ---- persistence + identity --------------------------------------------

  private async loadState(options: TwiceOptions): Promise<void> {
    if (typeof options.consent === 'boolean') {
      this.consent = options.consent;
      await Storage.set(CONSENT_KEY, this.consent ? '1' : '0');
    } else {
      const c = await Storage.get(CONSENT_KEY);
      this.consent = c == null ? true : c === '1';
    }

    if (options.userId) {
      this.userId = options.userId;
    } else {
      let id = await Storage.get(USER_ID_KEY);
      if (!id) {
        id = uuid();
        await Storage.set(USER_ID_KEY, id);
      }
      this.userId = id;
    }

    const dn = await Storage.get(DISPLAY_NAME_KEY);
    if (dn) this.displayName = dn;

    await this.loadQueue();
    Core.userId = this.userId;
    Core.displayName = this.displayName;
    Core.configured = true;
    this.identityReady = true;
  }

  private async persistQueue(): Promise<void> {
    try {
      await Storage.set(QUEUE_KEY, JSON.stringify(this.queue));
    } catch {
      /* ignore */
    }
  }

  private async loadQueue(): Promise<void> {
    try {
      const raw = await Storage.get(QUEUE_KEY);
      if (!raw) return;
      const arr = JSON.parse(raw);
      if (Array.isArray(arr)) {
        for (const e of arr) {
          if (e && typeof e.name === 'string' && typeof e.sessionId === 'string') {
            this.queue.push({
              eventId: e.eventId || uuid(),
              sessionId: e.sessionId,
              ts: typeof e.ts === 'number' ? e.ts : nowSec(),
              name: e.name,
              type: typeof e.type === 'string' ? e.type : '',
              params: e.params && typeof e.params === 'object' ? e.params : {},
            });
          }
        }
      }
    } catch {
      /* ignore */
    }
  }

  // ---- logging ------------------------------------------------------------

  private log(msg: string): void {
    if (this.debug) console.log('[Twice] ' + msg);
  }

  private warn(msg: string): void {
    console.warn('[Twice] ' + msg);
  }
}

// ---- module helpers -------------------------------------------------------

function clamp(n: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, n));
}

function clampStr(s: string, max: number): string {
  return !s ? s : s.length <= max ? s : s.substring(0, max);
}

function nowSec(): number {
  return Math.floor(Date.now() / 1000);
}

/** Allow [A-Za-z0-9_.:-], replace the rest with '_', cap at 64. */
function sanitizeName(name: string): string {
  if (!name) return 'unnamed';
  let out = '';
  for (let i = 0; i < name.length && out.length < 64; i++) {
    const c = name[i];
    out += /[A-Za-z0-9_.:-]/.test(c) ? c : '_';
  }
  return out.length === 0 ? 'unnamed' : out;
}

/** Map to a known type; '' for unknown OR 'general' (omitted → backend derives). */
function normalizeType(type?: string | null): string {
  if (!type) return '';
  const t = String(type).trim().toLowerCase();
  if (t === 'general') return '';
  return KNOWN_TYPES.indexOf(t) >= 0 ? t : '';
}

function resolvePlatform(): string {
  switch (Platform.OS) {
    case 'ios': return 'iOS';
    case 'android': return 'Android';
    case 'macos': return 'macOS';
    case 'windows': return 'Windows';
    case 'web': return 'Web';
    default: return Platform.OS ? String(Platform.OS) : 'Unknown';
  }
}

function deviceOS(): string {
  try {
    return `${Platform.OS} ${Platform.Version}`.trim();
  } catch {
    return '';
  }
}

/** Best-effort app version from expo-constants (optional). Pass `appVersion` to override. */
function detectAppVersion(): string {
  try {
    const mod = require('expo-constants');
    const C = mod && mod.default ? mod.default : mod;
    const v =
      (C && C.expoConfig && C.expoConfig.version) ||
      (C && C.manifest && C.manifest.version) ||
      (C && C.manifest2 && C.manifest2.extra && C.manifest2.extra.expoClient && C.manifest2.extra.expoClient.version);
    return v ? String(v) : '';
  } catch {
    return '';
  }
}
