import { ApiError } from '../sync/sync-api';

/** Message en français pour un échec d'un écran de gestion : le détail du serveur quand il est parlant, sinon une phrase claire. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    // Le serveur explique lui-même pourquoi (abonnement terminé, référence déjà utilisée…) : on garde ses mots.
    if ((error.code === 'subscription_expired' || error.code === 'too_many_requests') && error.message) {
      return error.message;
    }
    switch (error.status) {
      case 0:
        return 'Le serveur est injoignable : cet écran a besoin du réseau. Vérifiez la connexion puis réessayez.';
      case 401:
        return 'Votre session a expiré : reconnectez-vous puis réessayez.';
      case 403:
        return 'Votre profil n’a pas le droit de faire cette opération.';
      case 429:
        return 'Trop de demandes : patientez une minute avant de réessayer.';
    }
    if (error.status >= 400 && error.status < 500 && error.message) {
      return error.message;
    }
  }
  return 'L’opération a échoué. Réessayez dans un instant.';
}
