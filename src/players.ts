import { TwiceAnalytics } from './analytics';
import { Core } from './core';

/**
 * Player identity & profile. `userId` is the stable anonymous id the SDK attributes
 * everything to (analytics, leaderboards, purchases). `displayName` is a friendly label
 * shown on leaderboards and in the admin panel.
 *
 * Mirrors the Unity `TwicePlayers`. State lives in the analytics engine (the id stamps
 * every event, the name rides the event envelope), so this is a thin facade over it.
 */
export const TwicePlayers = {
  /** The persistent anonymous user id (same id sent with every event). "" before init resolves. */
  get userId(): string {
    return Core.userId;
  },
  getUserId(): string {
    return Core.userId;
  },

  /** The current display name, or "" if none set. */
  get displayName(): string {
    return Core.displayName;
  },
  getDisplayName(): string {
    return Core.displayName;
  },

  /**
   * Set this player's display name (leaderboards, admin panel). Persisted locally and
   * sent with the next event batch. Pass an empty string to clear it.
   */
  setDisplayName(name: string): void {
    TwiceAnalytics.setDisplayName(name);
  },
};
