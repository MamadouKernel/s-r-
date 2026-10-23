import { formatFcfa } from './money';

/** Écart de caisse en toutes lettres : le signe seul ne suffit pas (DESIGN.md §7). */
export function describeVariance(variance: number): string {
  if (variance === 0) {
    return 'Aucun écart';
  }
  return variance < 0 ? `Manque de ${formatFcfa(-variance)}` : `Excédent de ${formatFcfa(variance)}`;
}
