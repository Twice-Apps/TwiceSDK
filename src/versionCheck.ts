import { apiGet, Core } from './core';

/** The backend's decision for the running build. */
export type UpdateAction = 'none' | 'optional' | 'forced';

/** Result of {@link TwiceVersionCheck.check}. Build the prompt + store URL from these. */
export interface UpdateStatus {
  action: UpdateAction;
  appId: string; // iOS App Store numeric id
  bundleId: string; // platform bundle id
  updateAvailable: boolean;
  isForced: boolean;
  isOptional: boolean;
}

function enc(s: string): string {
  return encodeURIComponent(s);
}

/**
 * Update gating. Asks the backend whether the running (platform, version, build) is behind
 * the configured "latest" (→ optional) or "minimum" (→ forced) and returns the decision +
 * store ids. The prompt UI and the store-open action are the app's job ({@link storeUrl}
 * builds the URL). Mirrors the Unity `TwiceVersionChecker`.
 */
export const TwiceVersionCheck = {
  async check(opts?: { platform?: string; version?: string; build?: string }): Promise<UpdateStatus> {
    const platform = (opts && opts.platform) || Core.platform;
    const version = (opts && opts.version) || Core.appVersion;
    const build = (opts && opts.build) || Core.build;
    const fallback: UpdateStatus = {
      action: 'none', appId: '', bundleId: '', updateAvailable: false, isForced: false, isOptional: false,
    };
    const j = await apiGet(`/sdk/version-check?platform=${enc(platform)}&version=${enc(version)}&build=${enc(build)}`);
    if (!j) return fallback;
    const action: UpdateAction = j.action === 'forced' ? 'forced' : j.action === 'optional' ? 'optional' : 'none';
    return {
      action,
      appId: j.app_id || '',
      bundleId: j.bundle_id || '',
      updateAvailable: action !== 'none',
      isForced: action === 'forced',
      isOptional: action === 'optional',
    };
  },

  /** Build the store URL to open for an update (App Store / Google Play). "" if unknown. */
  storeUrl(status: UpdateStatus, platform?: string): string {
    const p = (platform || Core.platform || '').toLowerCase();
    if (p.indexOf('ios') >= 0 && status.appId) return `itms-apps://itunes.apple.com/app/id${status.appId}`;
    if (p.indexOf('android') >= 0 && status.bundleId) return `market://details?id=${status.bundleId}`;
    if (status.bundleId) return `https://play.google.com/store/apps/details?id=${status.bundleId}`;
    return '';
  },
};
