import { PaymentDraft, PaymentMethod } from './types';

/** Monnaie à rendre pour un paiement espèces (REQ-03). */
export function cashChange(dueFcfa: number, tenderedFcfa: number): number {
  if (tenderedFcfa < dueFcfa) {
    throw new Error('Le montant remis est inférieur au montant dû.');
  }
  return tenderedFcfa - dueFcfa;
}

/** Clé d'une référence Mobile Money : même normalisation que le serveur (opérateur, majuscules, sans espaces autour). */
export function referenceKey(method: PaymentMethod, reference: string): string {
  return `${method}:${reference.trim().toUpperCase()}`;
}

/**
 * Contrôles de paiement avant enregistrement (PWA-05). Renvoie les messages d'erreur en français, vide si tout est valide.
 * `usedReferences` contient les clés déjà utilisées sur l'appareil ; le serveur reste l'arbitre global (SC-05).
 */
export function validatePayments(
  totalFcfa: number,
  payments: readonly PaymentDraft[],
  usedReferences: ReadonlySet<string>,
): string[] {
  const errors: string[] = [];
  if (payments.length === 0) {
    return ['Choisissez au moins un mode de paiement.'];
  }

  const seen = new Set<string>();
  let sum = 0;
  for (const payment of payments) {
    if (!Number.isInteger(payment.amountFcfa) || payment.amountFcfa <= 0) {
      errors.push('Chaque montant doit être un nombre entier de FCFA supérieur à zéro.');
      continue;
    }
    sum += payment.amountFcfa;

    if (payment.method === 'Credit') {
      errors.push("Le paiement à crédit n'est pas encore disponible.");
    } else if (payment.method === 'Cash') {
      if ((payment.tenderedFcfa ?? payment.amountFcfa) < payment.amountFcfa) {
        errors.push('Le montant remis est inférieur au montant dû en espèces.');
      }
    } else {
      const reference = payment.reference?.trim() ?? '';
      if (reference === '') {
        errors.push('La référence de la transaction est obligatoire.');
        continue;
      }
      const key = referenceKey(payment.method, reference);
      if (usedReferences.has(key) || seen.has(key)) {
        errors.push(`La référence ${reference} a déjà été utilisée.`);
      }
      seen.add(key);
    }
  }

  if (!Number.isInteger(totalFcfa) || sum !== totalFcfa) {
    errors.push('Le total des paiements doit être égal au montant à payer.');
  }
  return errors;
}
