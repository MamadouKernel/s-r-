import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  failedAttempt,
  isExpired,
  isLocked,
  makeProof,
  MAX_FAILURES,
  minutesLeft,
  OfflineProof,
  OFFLINE_DAYS,
  succeededOffline,
  verifyPin,
} from './offline-proof';

const user = { id: 'u-1', identifier: 'Awa', displayName: 'Awa Koné', role: 'Cashier' };
const T0 = Date.parse('2026-10-26T08:00:00Z');
// Peu de tours en test : la preuve garde ses tours, le calcul reste identique.
const ITERATIONS = 1000;

describe('makeProof et verifyPin', () => {
  it('accepts the right PIN and refuses a wrong one', async () => {
    const proof = await makeProof('4821', user, T0, ITERATIONS);

    expect(await verifyPin('4821', proof)).toBe(true);
    expect(await verifyPin('4822', proof)).toBe(false);
    expect(await verifyPin('', proof)).toBe(false);
  });

  it('never keeps the PIN, in clear or in any readable form', async () => {
    const proof = await makeProof('4821', user, T0, ITERATIONS);

    const text = JSON.stringify(proof);
    expect(text).not.toContain('4821');
    expect(Object.keys(proof).sort()).toEqual(
      ['blocked', 'createdAt', 'displayName', 'failures', 'hash', 'identifier', 'iterations', 'lockedUntil', 'lockouts', 'role', 'salt', 'userId'].sort(),
    );
  });

  it('uses a new random salt each time, so two proofs of the same PIN differ', async () => {
    const first = await makeProof('4821', user, T0, ITERATIONS);
    const second = await makeProof('4821', user, T0, ITERATIONS);

    expect(first.salt).not.toBe(second.salt);
    expect(first.hash).not.toBe(second.hash);
  });

  it('keeps the identifier in lower case, to compare without caring about case', async () => {
    expect((await makeProof('4821', user, T0, ITERATIONS)).identifier).toBe('awa');
  });

  it('uses the strong default number of rounds', async () => {
    const spy = vi.spyOn(crypto.subtle, 'deriveBits');
    await makeProof('4821', user, T0);

    expect(spy.mock.calls[0][0]).toMatchObject({ name: 'PBKDF2', iterations: 600_000, hash: 'SHA-256' });
    spy.mockRestore();
  });
});

describe('délai de validité', () => {
  const proof = (createdAt: number): OfflineProof => ({ ...base(), createdAt: new Date(createdAt).toISOString() });
  const hours = (count: number) => count * 3_600_000;

  it('is valid for three days after the last online login, then it expires', () => {
    expect(OFFLINE_DAYS).toBe(3);
    expect(isExpired(proof(T0), T0 + hours(71))).toBe(false);
    expect(isExpired(proof(T0), T0 + hours(72))).toBe(false);
    expect(isExpired(proof(T0), T0 + hours(72) + 1)).toBe(true);
  });
});

describe('essais limités', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('counts the failures and locks for 15 minutes at the fifth', () => {
    let proof = base();
    for (let attempt = 1; attempt < MAX_FAILURES; attempt++) {
      proof = failedAttempt(proof, T0);
      expect(isLocked(proof, T0)).toBe(false);
      expect(proof.failures).toBe(attempt);
    }

    proof = failedAttempt(proof, T0);

    expect(isLocked(proof, T0)).toBe(true);
    expect(proof.failures).toBe(0);
    expect(proof.lockouts).toBe(1);
    expect(minutesLeft(proof, T0)).toBe(15);
    expect(minutesLeft(proof, T0 + 14 * 60_000 + 1)).toBe(1);
    expect(isLocked(proof, T0 + 15 * 60_000)).toBe(false);
  });

  it('blocks the account offline after three locks in a row, until an online login', () => {
    let proof = base();
    for (let lock = 0; lock < 3; lock++) {
      for (let attempt = 0; attempt < MAX_FAILURES; attempt++) {
        proof = failedAttempt(proof, T0 + lock * 3_600_000);
      }
    }

    expect(proof.lockouts).toBe(3);
    expect(proof.blocked).toBe(true);
  });

  it('resets the failures on an offline success but keeps the locks', () => {
    let proof = failedAttempt(failedAttempt(base(), T0), T0);
    proof = { ...proof, lockouts: 2 };

    const after = succeededOffline(proof);

    expect(after.failures).toBe(0);
    expect(after.lockouts).toBe(2);
    expect(after.blocked).toBe(false);
  });
});

function base(): OfflineProof {
  return {
    userId: 'u-1', identifier: 'awa', displayName: 'Awa Koné', role: 'Cashier', salt: 'AAAA', hash: 'BBBB', iterations: 1000,
    createdAt: new Date(T0).toISOString(), failures: 0, lockedUntil: null, lockouts: 0, blocked: false,
  };
}
