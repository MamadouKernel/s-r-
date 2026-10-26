import { InjectionToken } from '@angular/core';

/** Nombre de tours du calcul : fort en production, réduit par les tests. */
export const OFFLINE_ITERATIONS = new InjectionToken<number>('OFFLINE_ITERATIONS', { providedIn: 'root', factory: () => DEFAULT_ITERATIONS });

/**
 * Preuve locale du PIN pour se connecter sans réseau (docs/decisions/0019, docs/specs/connexion-hors-ligne). Le PIN n'est jamais gardé : seul un
 * condensat salé (PBKDF2-SHA256) calculé sur l'appareil l'est, avec les compteurs d'essais. Le serveur n'y participe pas.
 */
export interface OfflineProof {
  userId: string;
  /** En minuscules : l'identifiant se compare sans tenir compte de la casse. */
  identifier: string;
  displayName: string;
  role: string;
  /** Sel et condensat en base 64. */
  salt: string;
  hash: string;
  iterations: number;
  /** Instant de la dernière connexion en ligne du compte sur cet appareil. */
  createdAt: string;
  failures: number;
  lockedUntil: string | null;
  /** Verrouillages de suite sans connexion en ligne réussie. */
  lockouts: number;
  blocked: boolean;
}

export interface ProofOwner {
  id: string;
  identifier: string;
  displayName: string;
  role: string;
}

export const OFFLINE_DAYS = 3;
export const MAX_FAILURES = 5;
export const LOCK_MINUTES = 15;
export const MAX_LOCKOUTS = 3;
const DEFAULT_ITERATIONS = 600_000;
const DAY = 24 * 3_600_000;

/** Profils qui encaissent : les seuls à pouvoir entrer sans réseau (le propriétaire n'a pas de PIN). */
export const OFFLINE_ROLES: readonly string[] = ['Cashier', 'Manager'];

function toBase64(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes));
}

function fromBase64(text: string): Uint8Array<ArrayBuffer> {
  return Uint8Array.from(atob(text), (character) => character.charCodeAt(0));
}

async function derive(pin: string, salt: Uint8Array<ArrayBuffer>, iterations: number): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(pin), 'PBKDF2', false, ['deriveBits']);
  return new Uint8Array(await crypto.subtle.deriveBits({ name: 'PBKDF2', salt, iterations, hash: 'SHA-256' }, key, 256));
}

export async function makeProof(pin: string, owner: ProofOwner, now: number, iterations = DEFAULT_ITERATIONS): Promise<OfflineProof> {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  return {
    userId: owner.id,
    identifier: owner.identifier.trim().toLowerCase(),
    displayName: owner.displayName,
    role: owner.role,
    salt: toBase64(salt),
    hash: toBase64(await derive(pin, salt, iterations)),
    iterations,
    createdAt: new Date(now).toISOString(),
    failures: 0,
    lockedUntil: null,
    lockouts: 0,
    blocked: false,
  };
}

/** Comparaison en temps constant du condensat du PIN tapé avec celui de la preuve. */
export async function verifyPin(pin: string, proof: OfflineProof): Promise<boolean> {
  if (!pin) {
    return false;
  }
  const expected = fromBase64(proof.hash);
  const actual = await derive(pin, fromBase64(proof.salt), proof.iterations);
  if (expected.length !== actual.length) {
    return false;
  }
  let difference = 0;
  for (let index = 0; index < expected.length; index++) {
    difference |= expected[index] ^ actual[index];
  }
  return difference === 0;
}

export function isExpired(proof: OfflineProof, now: number): boolean {
  return now - Date.parse(proof.createdAt) > OFFLINE_DAYS * DAY;
}

export function isLocked(proof: OfflineProof, now: number): boolean {
  return proof.lockedUntil !== null && Date.parse(proof.lockedUntil) > now;
}

/** Minutes d'attente restantes, arrondies au-dessus (« Réessayez dans 1 minute »). */
export function minutesLeft(proof: OfflineProof, now: number): number {
  return proof.lockedUntil === null ? 0 : Math.max(0, Math.ceil((Date.parse(proof.lockedUntil) - now) / 60_000));
}

/** Un PIN faux : au cinquième, 15 minutes de verrou ; au troisième verrou de suite, le compte attend une connexion en ligne. */
export function failedAttempt(proof: OfflineProof, now: number): OfflineProof {
  const failures = proof.failures + 1;
  if (failures < MAX_FAILURES) {
    return { ...proof, failures };
  }
  const lockouts = proof.lockouts + 1;
  return {
    ...proof,
    failures: 0,
    lockouts,
    lockedUntil: new Date(now + LOCK_MINUTES * 60_000).toISOString(),
    blocked: lockouts >= MAX_LOCKOUTS,
  };
}

/** Une connexion sans réseau réussie remet les essais à zéro ; les verrouillages ne sont remis à zéro que par une connexion en ligne. */
export function succeededOffline(proof: OfflineProof): OfflineProof {
  return { ...proof, failures: 0, lockedUntil: null };
}
