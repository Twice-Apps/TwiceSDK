import { apiGet, apiPost, Core } from './core';

/** One ranked row from {@link TwiceLeaderboards.getTop}. */
export interface LeaderboardEntry {
  rank: number;
  userId: string;
  name: string; // display name the game submitted ("" if none)
  value: number;
}

/** The current player's standing, from {@link TwiceLeaderboards.getMyRank}. */
export interface LeaderboardRank {
  found: boolean; // is the player on the board this period?
  rank: number; // 1-based; 0 when not found
  value: number;
  total: number; // total entries on the board this period
  name: string;
}

function enc(s: string): string {
  return encodeURIComponent(s);
}

async function fetchTop(id: string, count: number, period?: 'previous'): Promise<LeaderboardEntry[]> {
  if (!id) return [];
  const limit = Math.max(1, Math.min(1000, count | 0));
  let path = `/sdk/leaderboard?board=${enc(id)}&limit=${limit}`;
  if (period) path += `&period=${period}`;
  const j = await apiGet(path);
  if (!j || !Array.isArray(j.entries)) return [];
  return j.entries.map((r: any, i: number) => ({
    rank: typeof r.rank === 'number' ? r.rank : i + 1,
    userId: r.user_id || '',
    name: r.name || '',
    value: typeof r.value === 'number' ? r.value : Number(r.value) || 0,
  }));
}

async function fetchRank(id: string, period?: 'previous'): Promise<LeaderboardRank> {
  const empty: LeaderboardRank = { found: false, rank: 0, value: 0, total: 0, name: '' };
  if (!id) return empty;
  let path = `/sdk/leaderboard/rank?board=${enc(id)}&user_id=${enc(Core.userId)}`;
  if (period) path += `&period=${period}`;
  const j = await apiGet(path);
  if (!j) return empty;
  return {
    found: !!j.found,
    rank: typeof j.rank === 'number' ? j.rank : 0,
    value: typeof j.value === 'number' ? j.value : Number(j.value) || 0,
    total: typeof j.total === 'number' ? j.total : 0,
    name: j.name || '',
  };
}

/**
 * Leaderboards client. Submits a score for the current player and reads the ranked top.
 * Sort direction, aggregation (last/min/max/sum) and reset frequency are configured
 * server-side per board — the client only sends the board id + score. Player id is taken
 * from {@link TwicePlayers}. All calls resolve gracefully (never throw).
 */
export const TwiceLeaderboards = {
  /** Submit a score for the current player. Resolves true on success. */
  async submit(leaderboardId: string, score: number, playerName?: string): Promise<boolean> {
    if (!leaderboardId) return false;
    const body: Record<string, unknown> = { leaderboard_id: leaderboardId, score, user_id: Core.userId };
    if (playerName) body.player_name = playerName;
    const j = await apiPost('/sdk/leaderboard/submit', body);
    return !!j && j.ok !== false;
  },

  /** Ranked top of a board (current period). `count` is clamped 1..1000. */
  getTop(leaderboardId: string, count: number): Promise<LeaderboardEntry[]> {
    return fetchTop(leaderboardId, count);
  },

  /** Total entries (players) on a board for the current period. */
  async getEntryCount(leaderboardId: string): Promise<number> {
    if (!leaderboardId) return 0;
    const j = await apiGet(`/sdk/leaderboard?board=${enc(leaderboardId)}&limit=1`);
    return j && typeof j.total === 'number' ? j.total : 0;
  },

  /** The current player's own rank/value on a board (current period). */
  getMyRank(leaderboardId: string): Promise<LeaderboardRank> {
    return fetchRank(leaderboardId);
  },

  /** Like {@link getTop} but reads the most recently reset (archived) period. */
  getTopBeforeReset(leaderboardId: string, count: number): Promise<LeaderboardEntry[]> {
    return fetchTop(leaderboardId, count, 'previous');
  },

  /** Like {@link getMyRank} but for the most recently reset (archived) period. */
  getMyRankBeforeReset(leaderboardId: string): Promise<LeaderboardRank> {
    return fetchRank(leaderboardId, 'previous');
  },
};
