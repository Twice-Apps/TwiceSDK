import { AppState, AppStateStatus } from 'react-native';

import { TwiceAnalytics } from './analytics';
import { apiGet, Core } from './core';
import { Storage } from './storage';
import { EventParams } from './types';

/** A grant (or deduction, when `amount` is negative) queued in the Twice panel. */
export interface WalletGrant {
  id: string; // "g_…" — stable, use it to de-duplicate a receipt UI
  currency: string; // the key you passed to TwiceWallet.register (e.g. "coin")
  amount: number; // positive = give, negative = take away
  note: string; // free text the operator typed in the panel ("" if none)
  createdAt: number; // unix seconds
}

/** How the game exposes one currency to the wallet. */
export interface WalletCurrency {
  /** Current balance. */
  get(): number;
  /**
   * Add `delta` (negative = remove) and save. Return true when applied, false to refuse
   * (e.g. not enough to remove) — the panel then shows the grant as rejected.
   */
  apply(delta: number, grant: WalletGrant): boolean | Promise<boolean>;
}

const DONE_KEY = 'twice_wallet_done'; // { grantId: { s: 'a' | 'r', t: unix } }
const LAST_KEY = 'twice_wallet_last'; // { currency: value, _t: unix }
const MIN_RESYNC_MS = 60_000;
const DONE_KEEP_DAYS = 45;
const DONE_CAP = 300;
// A grant we already acknowledged but the server still lists (the ack event has not reached
// it yet, or was dropped): after this long the ack is sent again.
const REACK_AFTER_S = 600;

const currencies = new Map<string, WalletCurrency>();
const listeners = new Set<(g: WalletGrant) => void>();
let syncing: Promise<number> | null = null;
let lastSync = 0;
let started = false;

const nowS = (): number => Math.floor(Date.now() / 1000);

function validKey(k: string): boolean {
  return /^[a-z0-9_]{1,32}$/.test(k);
}

async function loadJson(key: string): Promise<Record<string, any>> {
  try {
    const raw = await Storage.get(key);
    const v = raw ? JSON.parse(raw) : {};
    return v && typeof v === 'object' ? v : {};
  } catch {
    return {};
  }
}

async function saveDone(done: Record<string, { s: string; t: number }>): Promise<void> {
  const cutoff = nowS() - DONE_KEEP_DAYS * 86400;
  const rows = Object.entries(done).sort((a, b) => (b[1]?.t ?? 0) - (a[1]?.t ?? 0));
  const kept: Record<string, { s: string; t: number }> = {};
  rows.forEach(([id, v], i) => {
    if (i < DONE_CAP && (v?.t ?? 0) >= cutoff) kept[id] = v;
  });
  await Storage.set(DONE_KEY, JSON.stringify(kept));
}

function balanceOf(currency: string): number | null {
  const c = currencies.get(currency);
  if (!c) return null;
  try {
    const v = c.get();
    return typeof v === 'number' && isFinite(v) ? v : null;
  } catch {
    return null;
  }
}

function logAck(g: WalletGrant, applied: boolean, balance: number | null): void {
  const p: EventParams = { grant_id: g.id, currency: g.currency, amount: g.amount };
  if (balance !== null) p.balance_after = balance;
  if (!applied) p.reason = 'rejected_by_game';
  TwiceAnalytics.logEvent(applied ? 'wallet_grant_applied' : 'wallet_grant_rejected', p);
}

/** Logs wallet_balance ({currency: balance}); unforced calls skip it when nothing changed (< 1 day). */
async function reportBalances(force: boolean): Promise<void> {
  if (currencies.size === 0) return;
  const p: EventParams = {};
  for (const key of currencies.keys()) {
    const v = balanceOf(key);
    if (v !== null) p[key] = v;
  }
  if (Object.keys(p).length === 0) return;
  const ts = nowS();
  if (!force) {
    const last = await loadJson(LAST_KEY);
    const lastT = typeof last._t === 'number' ? last._t : 0;
    delete last._t;
    const same =
      Object.keys(last).length === Object.keys(p).length && Object.keys(p).every((k) => last[k] === p[k]);
    if (same && ts - lastT < 86400) return;
  }
  TwiceAnalytics.logEvent('wallet_balance', p);
  await Storage.set(LAST_KEY, JSON.stringify({ ...p, _t: ts }));
}

async function applyOne(row: any, done: Record<string, { s: string; t: number }>): Promise<boolean> {
  const g: WalletGrant = {
    id: typeof row?.id === 'string' ? row.id : '',
    currency: typeof row?.currency === 'string' ? row.currency : '',
    amount: typeof row?.amount === 'number' ? row.amount : Number(row?.amount) || 0,
    note: typeof row?.note === 'string' ? row.note : '',
    createdAt: typeof row?.created_at === 'number' ? row.created_at : 0,
  };
  if (!g.id || !g.currency) return false;

  const prev = done[g.id];
  if (prev) {
    // Already handled on this device: never apply twice, only repeat a lost ack.
    if (nowS() - (prev.t ?? 0) >= REACK_AFTER_S) {
      logAck(g, prev.s === 'a', balanceOf(g.currency));
      done[g.id] = { s: prev.s, t: nowS() };
      await saveDone(done);
    }
    return false;
  }

  const c = currencies.get(g.currency);
  // Unknown to this build (yet): leave it queued so a later build/registration can apply it.
  if (!c) return false;

  let ok: boolean;
  try {
    ok = !!(await c.apply(g.amount, g));
  } catch (e) {
    console.warn('[TwiceWallet] apply threw — grant left pending', e);
    return false;
  }
  // Remember BEFORE acknowledging, so a crash between the two can't apply it twice.
  done[g.id] = { s: ok ? 'a' : 'r', t: nowS() };
  await saveDone(done);
  logAck(g, ok, balanceOf(g.currency));
  if (ok) {
    listeners.forEach((cb) => {
      try {
        cb(g);
      } catch {
        /* a listener must never break the sync */
      }
    });
  }
  return ok;
}

async function doSync(): Promise<number> {
  if (currencies.size === 0 || !Core.userId) return 0;
  lastSync = Date.now();
  const j = await apiGet(`/sdk/wallet/grants?user_id=${encodeURIComponent(Core.userId)}`);
  let applied = 0;
  if (j && Array.isArray(j.grants)) {
    const done = (await loadJson(DONE_KEY)) as Record<string, { s: string; t: number }>;
    for (const row of j.grants) {
      if (await applyOne(row, done)) applied++;
    }
  }
  await reportBalances(false);
  return applied;
}

function onAppState(state: AppStateStatus): void {
  if (state === 'active' && Date.now() - lastSync >= MIN_RESYNC_MS) void TwiceWallet.sync();
}

async function firstSync(): Promise<void> {
  // Let the game register all its currencies first, and wait for the player id.
  await new Promise((r) => setTimeout(r, 1000));
  for (let waited = 0; !Core.userId && waited < 60_000; waited += 500) {
    await new Promise((r) => setTimeout(r, 500));
  }
  await TwiceWallet.sync();
}

/**
 * Wallet: lets the Twice panel give or take a player's in-game currency, and shows the
 * player's balances in the panel (Players → player, Wallet module).
 *
 * The GAME stays the owner of the balance. Register each currency once with a getter and an
 * apply callback; the SDK pulls the grants queued for this player, applies them through the
 * callback and acknowledges them as analytics events (offline-safe queue).
 *
 * ```ts
 * TwiceWallet.register('coin', { get: () => coins, apply: (delta) => { addCoins(delta); return true; } });
 * ```
 *
 * Nothing happens until the first `register`. Pending grants are fetched shortly after that,
 * whenever the app returns to the foreground (at most once a minute) and on `sync()`.
 * Never throws.
 */
export const TwiceWallet = {
  /** Register a currency (key as defined in the panel's Wallet module, e.g. "coin"). */
  register(currency: string, handler: WalletCurrency): void {
    if (!validKey(currency)) {
      console.warn(`[TwiceWallet] register: currency key must be 1-32 chars of a-z, 0-9, _ — got '${currency}'`);
      return;
    }
    if (!handler || typeof handler.get !== 'function' || typeof handler.apply !== 'function') {
      console.warn(`[TwiceWallet] register('${currency}'): { get, apply } are required`);
      return;
    }
    currencies.set(currency, handler);
    if (!started) {
      started = true;
      AppState.addEventListener('change', onAppState);
      void firstSync();
    }
  },

  /** Stop handling a currency (its grants stay queued in the panel). */
  unregister(currency: string): void {
    currencies.delete(currency);
  },

  /** Fetch and apply the grants queued for this player now. Resolves the number applied. */
  sync(): Promise<number> {
    if (!syncing) {
      // (no .finally: the SDK targets es2017)
      syncing = doSync()
        .catch(() => 0)
        .then((n) => {
          syncing = null;
          return n;
        });
    }
    return syncing;
  },

  /** Called after a grant was applied (e.g. to show "You received 100 coins"). Returns an unsubscribe. */
  onGrantApplied(cb: (g: WalletGrant) => void): () => void {
    listeners.add(cb);
    return () => {
      listeners.delete(cb);
    };
  },

  /** Send the current balances to the panel now (normally automatic after each sync). */
  reportBalances(): void {
    void reportBalances(true);
  },
};
