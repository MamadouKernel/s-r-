import { ApiError } from '../sync/sync-api';

/** Version des conditions d'utilisation que le serveur attend (réglage `Signup:TermsVersion`). */
export const TERMS_VERSION = '2026-10-brouillon';

/** Types d'activité : la valeur envoyée au serveur et le mot que le commerçant lit. */
export const ACTIVITIES: readonly { value: string; label: string }[] = [
  { value: 'Shop', label: 'Boutique' },
  { value: 'Supermarket', label: 'Supérette' },
  { value: 'Hardware', label: 'Quincaillerie' },
  { value: 'Pharmacy', label: 'Pharmacie' },
  { value: 'Restaurant', label: 'Restaurant ou maquis' },
  { value: 'Salon', label: 'Salon' },
  { value: 'Workshop', label: 'Atelier' },
  { value: 'Other', label: 'Autre' },
];

export const PASSWORD_RULE = 'Au moins 10 caractères, avec au moins une lettre et un chiffre.';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

/** Contrôle léger avant l'envoi (le serveur reste l'arbitre) : une adresse avec un @ et un point dans le domaine. */
export function looksLikeEmail(value: string): boolean {
  return EMAIL.test(value.trim());
}

/**
 * Message en français simple pour un échec d'un écran d'inscription ou de connexion : le message du serveur quand il parle au
 * commerçant (il est écrit pour cela), sinon une phrase qui dit quoi faire. Jamais de code ni de jargon.
 */
export function onboardingError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 0) {
      return 'Le serveur est injoignable. Vérifiez votre connexion internet puis réessayez.';
    }
    if (error.status === 429 && !error.message) {
      return 'Trop de demandes. Patientez une minute avant de réessayer.';
    }
    if (error.status >= 400 && error.status < 500 && error.message) {
      return error.message;
    }
    if (error.status === 503 && error.message) {
      return error.message;
    }
  }
  return 'Une erreur est survenue. Réessayez dans un instant.';
}
