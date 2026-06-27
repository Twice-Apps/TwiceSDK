import { Storage } from './storage';

const DEFAULT_ENDPOINT = 'https://www.twiceapps.co/api/v1';
const CACHE_KEY = 'twice_remote_config';

/** Options for {@link TwiceRemoteConfig.init}. */
export interface RemoteConfigOptions {
  apiKey: string;
  endpointBaseUrl?: string;
  /** Fetch fresh config immediately after loading the cache. Default: true. */
  autoFetch?: boolean;
}

class RemoteConfigClient {
  private apiKey = '';
  private endpoint = DEFAULT_ENDPOINT;
  private config: Record<string, unknown> = {};
  private _version = 0;
  private listeners: Array<() => void> = [];

  async init(options: RemoteConfigOptions): Promise<void> {
    this.apiKey = (options.apiKey || '').trim();
    if (options.endpointBaseUrl) this.endpoint = options.endpointBaseUrl.trim();
    await this.loadCache();
    if (options.autoFetch !== false) {
      void this.fetch();
    }
  }

  get version(): number {
    return this._version;
  }

  /** Re-pull the config from the backend. Resolves true when a fetch succeeded. */
  async fetch(): Promise<boolean> {
    try {
      const res = await fetch(this.endpoint.replace(/\/+$/, '') + '/sdk/config', {
        method: 'GET',
        headers: { 'X-App-Key': this.apiKey },
      });
      if (!res.ok) return false;
      const data = await res.json();
      if (!data || data.ok === false) return false;
      const newVersion = typeof data.version === 'number' ? data.version : 0;
      const changed = newVersion !== this._version;
      this.config = data.config && typeof data.config === 'object' ? data.config : {};
      this._version = newVersion;
      await this.saveCache();
      if (changed) this.emit();
      return true;
    } catch {
      return false;
    }
  }

  getString(key: string, fallback = ''): string {
    const v = this.config[key];
    return v == null ? fallback : String(v);
  }

  getNumber(key: string, fallback = 0): number {
    const n = Number(this.config[key]);
    return isFinite(n) ? n : fallback;
  }

  /** Alias of {@link getNumber} for parity with the Unity SDK. */
  getInt(key: string, fallback = 0): number {
    return Math.trunc(this.getNumber(key, fallback));
  }

  /** Alias of {@link getNumber}. */
  getFloat(key: string, fallback = 0): number {
    return this.getNumber(key, fallback);
  }

  getBool(key: string, fallback = false): boolean {
    const v = this.config[key];
    if (typeof v === 'boolean') return v;
    if (typeof v === 'number') return v !== 0;
    if (typeof v === 'string') return v === 'true' || v === '1';
    return fallback;
  }

  /** Read a nested/object value (already parsed by the backend). */
  getJson<T>(key: string, fallback: T): T {
    const v = this.config[key];
    return v == null ? fallback : (v as T);
  }

  /** Subscribe to config updates; returns an unsubscribe function. */
  onUpdated(cb: () => void): () => void {
    this.listeners.push(cb);
    return () => {
      const i = this.listeners.indexOf(cb);
      if (i >= 0) this.listeners.splice(i, 1);
    };
  }

  private emit(): void {
    for (const cb of this.listeners.slice()) {
      try {
        cb();
      } catch {
        /* ignore listener errors */
      }
    }
  }

  private async saveCache(): Promise<void> {
    try {
      await Storage.set(CACHE_KEY, JSON.stringify({ version: this._version, config: this.config }));
    } catch {
      /* ignore */
    }
  }

  private async loadCache(): Promise<void> {
    try {
      const raw = await Storage.get(CACHE_KEY);
      if (!raw) return;
      const data = JSON.parse(raw);
      if (data && typeof data === 'object') {
        this._version = typeof data.version === 'number' ? data.version : 0;
        this.config = data.config && typeof data.config === 'object' ? data.config : {};
      }
    } catch {
      /* ignore */
    }
  }
}

/**
 * Per-project typed key-value config (PlayFab Title-Data style). Cached offline,
 * instant next launch, bumps a `version`. Manage keys in Twice admin → Projeler
 * → your project → Remote Config.
 */
export const TwiceRemoteConfig = new RemoteConfigClient();
