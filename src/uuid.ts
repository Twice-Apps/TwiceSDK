/**
 * RFC4122-v4-ish id as 32 lowercase hex chars (no dashes) — matches the Unity
 * SDK's `Guid.ToString("N")` format used for `event_id` / `user_id`.
 *
 * Uses Math.random: fine for de-duplication ids and an anonymous user id; it is
 * NOT a cryptographic token. Kept dependency-free on purpose.
 */
export function uuid(): string {
  let s = '';
  for (let i = 0; i < 32; i++) {
    const r = (Math.random() * 16) | 0;
    if (i === 12) {
      s += '4';
    } else if (i === 16) {
      s += ((r & 0x3) | 0x8).toString(16);
    } else {
      s += r.toString(16);
    }
  }
  return s;
}
