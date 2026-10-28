/** Billets et pièces du franc CFA, du plus grand au plus petit ; une valeur n'y figure qu'une fois (le billet et la pièce de 500 se comptent ensemble). */
export const DENOMINATIONS: readonly { value: number; label: string }[] = [
  { value: 10_000, label: '10 000 (billet)' },
  { value: 5_000, label: '5 000 (billet)' },
  { value: 2_000, label: '2 000 (billet)' },
  { value: 1_000, label: '1 000 (billet)' },
  { value: 500, label: '500 (billet ou pièce)' },
  { value: 250, label: '250 (pièce)' },
  { value: 200, label: '200 (pièce)' },
  { value: 100, label: '100 (pièce)' },
  { value: 50, label: '50 (pièce)' },
  { value: 25, label: '25 (pièce)' },
  { value: 10, label: '10 (pièce)' },
  { value: 5, label: '5 (pièce)' },
];

/** Écart toléré sans recomptage : valeur de départ sans source, la même que celle du serveur, qui tranche (docs/decisions/0021). */
export const COUNT_TOLERANCE_FCFA = 500;

/** Au-delà de cette sortie d'espèces, l'accord d'un gérant est nécessaire. */
export const CASH_OUT_APPROVAL_FCFA = 5_000;

/** Au-delà de cet argent dans le tiroir, la caisse avertit « faites un versement au coffre ». */
export const DRAWER_WARNING_FCFA = 100_000;

export function sumDenominations(counts: Readonly<Record<number, number>>): number {
  return Object.entries(counts).reduce((sum, [value, count]) => sum + Number(value) * count, 0);
}

/** Un comptage qui s'écarte de plus que la tolérance de ce qui est attendu doit être refait, une seule fois, sans dire de combien. */
export function needsRecount(countedFcfa: number, expectedFcfa: number): boolean {
  return Math.abs(countedFcfa - expectedFcfa) > COUNT_TOLERANCE_FCFA;
}
