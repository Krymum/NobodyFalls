/**
 * The server's clock is the game's clock (it may be simulated in development):
 * keep the offset between it and the device, and count down from it.
 */
let offsetMs = 0;

export function syncServerTime(serverNow: string): void {
  const server = Date.parse(serverNow);
  if (Number.isFinite(server)) {
    offsetMs = server - Date.now();
  }
}

export function serverNow(): number {
  return Date.now() + offsetMs;
}

/** "03:21:07" until the given instant (never negative). */
export function countdown(until: string, now = serverNow()): string {
  const left = Math.max(0, Math.floor((Date.parse(until) - now) / 1000));
  const pad = (n: number): string => String(n).padStart(2, '0');
  return `${pad(Math.floor(left / 3600))}:${pad(Math.floor((left % 3600) / 60))}:${pad(left % 60)}`;
}

/** True during the last hour: the "last stand" window. */
export function isLastHour(until: string, now = serverNow()): boolean {
  const left = Date.parse(until) - now;
  return left > 0 && left <= 3_600_000;
}
