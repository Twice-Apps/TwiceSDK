import { TwiceAnalytics } from './analytics';
import { TwiceRemoteConfig } from './remoteConfig';
import { TwiceOptions } from './types';

/** Options for {@link Twice.init}. */
export interface TwiceInitOptions extends TwiceOptions {
  /** Also initialise Remote Config with the same key/endpoint. Default: true. */
  remoteConfig?: boolean;
}

/**
 * One-call entry point: initialise analytics and (optionally) remote config.
 *
 * ```ts
 * import { Twice } from '@twiceapps/react-native';
 * await Twice.init({ apiKey: 'tw_…' });
 * ```
 *
 * Prefer the per-module APIs (`TwiceAnalytics`, `TwiceRemoteConfig`) for finer control.
 */
export const Twice = {
  async init(options: TwiceInitOptions): Promise<void> {
    await TwiceAnalytics.init(options);
    if (options.remoteConfig !== false) {
      await TwiceRemoteConfig.init({ apiKey: options.apiKey, endpointBaseUrl: options.endpointBaseUrl });
    }
  },
};
