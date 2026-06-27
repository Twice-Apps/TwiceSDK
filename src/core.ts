/**
 * Shared SDK state + tiny request helpers.
 *
 * `TwiceAnalytics.init()` populates {@link Core} (api key, endpoint, identity). The
 * other modules (players, leaderboards, version check, push) read from it and use the
 * `apiGet/apiPost/apiDelete` helpers — so there is one source of truth for config and
 * one place that attaches the `X-App-Key` header.
 */
export const Core = {
  apiKey: '',
  endpoint: 'https://www.twiceapps.co/api/v1',
  userId: '',
  displayName: '',
  platform: '',
  appVersion: '',
  build: '',
  sandbox: false,
  configured: false,
};

function url(path: string): string {
  return Core.endpoint.replace(/\/+$/, '') + path;
}

function headers(json: boolean): Record<string, string> {
  const h: Record<string, string> = { 'X-App-Key': Core.apiKey };
  if (json) h['Content-Type'] = 'application/json';
  return h;
}

/** GET a JSON endpoint. Resolves the parsed body, or null on any failure. */
export async function apiGet(path: string): Promise<any | null> {
  if (!Core.apiKey) return null;
  try {
    const res = await fetch(url(path), { method: 'GET', headers: headers(false) });
    if (!res.ok) return null;
    return await res.json();
  } catch {
    return null;
  }
}

/** POST a JSON body. Resolves the parsed body (or `{}`), or null on any failure. */
export async function apiPost(path: string, body: unknown): Promise<any | null> {
  if (!Core.apiKey) return null;
  try {
    const res = await fetch(url(path), { method: 'POST', headers: headers(true), body: JSON.stringify(body) });
    if (!res.ok) return null;
    try {
      return await res.json();
    } catch {
      return {};
    }
  } catch {
    return null;
  }
}

/** DELETE with an optional JSON body. Resolves true on a 2xx. */
export async function apiDelete(path: string, body?: unknown): Promise<boolean> {
  if (!Core.apiKey) return false;
  try {
    const res = await fetch(url(path), {
      method: 'DELETE',
      headers: headers(true),
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    return res.ok;
  } catch {
    return false;
  }
}
