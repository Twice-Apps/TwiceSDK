import { apiDelete, apiPost, Core } from './core';

/**
 * Player push registration. Your app obtains the device push token (e.g. via
 * `expo-notifications`: `getDevicePushTokenAsync()` for native FCM/APNs, or
 * `getExpoPushTokenAsync()`), then hands it to {@link TwicePush.register}. The token is
 * stored on the Twice backend against the current user so operator/segment pushes can
 * reach this device. Mirrors the Unity `TwicePush`.
 *
 * @example
 * import * as Notifications from 'expo-notifications';
 * const { data: token } = await Notifications.getDevicePushTokenAsync();
 * await TwicePush.register(token);
 */
export const TwicePush = {
  /** Register a captured device token for the current user. Resolves true on success. */
  async register(token: string, opts?: { platform?: string; env?: 'sandbox' | 'production' }): Promise<boolean> {
    if (!token) return false;
    const platform = ((opts && opts.platform) || Core.platform || '').toLowerCase();
    const env = (opts && opts.env) || (Core.sandbox ? 'sandbox' : 'production');
    const j = await apiPost('/sdk/push-token', { token, platform, user_id: Core.userId, env });
    return !!j && j.ok !== false;
  },

  /** Unregister a device token (e.g. on logout) so it stops receiving pushes. */
  async unregister(token: string): Promise<boolean> {
    if (!token) return false;
    return apiDelete(`/sdk/push-token?token=${encodeURIComponent(token)}`, { token });
  },
};
