const NBSP = '\u00a0';

/** « 1 500 FCFA » : entier, séparateur de milliers insécable, jamais de décimales (RG-18). */
export function formatFcfa(amount: number): string {
  const rounded = Math.trunc(amount);
  const sign = rounded < 0 ? '-' : '';
  const digits = Math.abs(rounded)
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, NBSP);
  return `${sign}${digits}${NBSP}FCFA`;
}

/** Lit un montant saisi au clavier : chiffres seulement, espaces tolérés ; `null` si vide ou invalide. */
export function parseFcfa(input: string): number | null {
  const cleaned = input.replace(/[\s  ]/g, '');
  if (!/^\d{1,12}$/.test(cleaned)) {
    return null;
  }
  return Number(cleaned);
}
