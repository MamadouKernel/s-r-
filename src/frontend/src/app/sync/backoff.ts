const BASE_MS = 2_000;
const MAX_MS = 60_000;

/** Délai avant le prochain essai de synchronisation : 2 s, 4 s, 8 s… plafonné à 60 s (PWA-07). */
export function nextDelayMs(failedAttempts: number): number {
  return Math.min(BASE_MS * 2 ** failedAttempts, MAX_MS);
}
