/**
 * Twice SDK for React Native & Expo.
 *
 * @example
 * import { Twice, TwiceAnalytics, TwiceRemoteConfig } from '@twiceapps/react-native';
 *
 * await Twice.init({ apiKey: 'tw_xxx' });            // analytics + remote config
 * TwiceAnalytics.logEvent('app_open');
 * TwiceAnalytics.errorEvent('save_failed', err);     // typed: error
 * const adsOn = TwiceRemoteConfig.getBool('ads_enabled', true);
 */
export { Twice } from './twice';
export type { TwiceInitOptions } from './twice';
export { TwiceAnalytics } from './analytics';
export type { TwiceAnalyticsApi } from './analytics';
export { TwiceRemoteConfig } from './remoteConfig';
export type { RemoteConfigOptions } from './remoteConfig';
export type { EventType, EventParams, ParamValue, TwiceOptions, DebugInfo } from './types';

import { TwiceAnalytics } from './analytics';
export default TwiceAnalytics;
