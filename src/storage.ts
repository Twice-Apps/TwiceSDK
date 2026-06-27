/**
 * Tiny async key-value store used for offline event persistence + the user id.
 *
 * Uses `@react-native-async-storage/async-storage` when it is installed (the
 * normal case in an Expo / React Native app) and transparently falls back to an
 * in-memory map otherwise — so the SDK never hard-fails on a missing peer dep.
 * Add AsyncStorage to your app for events to survive an app restart.
 */
// Module-scoped ambient (file is a module → does not leak to consumers).
declare const require: (name: string) => any;

interface KeyValue {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
  removeItem(key: string): Promise<void>;
}

let backend: KeyValue | null = null;
try {
  // Optional peer dependency — resolved lazily so bundlers don't require it.
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  const mod = require('@react-native-async-storage/async-storage');
  backend = (mod && mod.default ? mod.default : mod) as KeyValue;
} catch {
  backend = null;
}

const memory = new Map<string, string>();

export const Storage = {
  /** True when real on-device persistence is available (AsyncStorage installed). */
  persistent: !!backend,

  async get(key: string): Promise<string | null> {
    if (backend) {
      try {
        return await backend.getItem(key);
      } catch {
        return null;
      }
    }
    return memory.has(key) ? (memory.get(key) as string) : null;
  },

  async set(key: string, value: string): Promise<void> {
    if (backend) {
      try {
        await backend.setItem(key, value);
      } catch {
        /* ignore */
      }
      return;
    }
    memory.set(key, value);
  },

  async remove(key: string): Promise<void> {
    if (backend) {
      try {
        await backend.removeItem(key);
      } catch {
        /* ignore */
      }
      return;
    }
    memory.delete(key);
  },
};
