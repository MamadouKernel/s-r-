const HOUR_MS = 3_600_000;
const STALE_AFTER_HOURS = 12;
const STABLE_BELOW_PERCENT = 0.5;

const PAYMENT_LABELS: Record<string, string> = {
  Cash: 'Espèces',
  OrangeMoney: 'Orange Money',
  MtnMomo: 'MTN MoMo',
  MoovMoney: 'Moov Money',
  Wave: 'Wave',
  Credit: 'Crédit',
};

export function paymentLabel(method: string): string {
  return PAYMENT_LABELS[method] ?? method;
}

/** Panier moyen au franc près ; pas de valeur sans vente. */
export function averageBasket(netTotalFcfa: number, ticketCount: number): number | null {
  return ticketCount > 0 ? Math.round(netTotalFcfa / ticketCount) : null;
}

export type VariationKind = 'up' | 'down' | 'stable' | 'none';

export interface Variation {
  kind: VariationKind;
  percent: number;
}

/** Variation par rapport à la veille à la même heure. « none » quand la veille est à zéro : un pourcentage n'aurait aucun sens. */
export function variation(current: number, previous: number): Variation {
  if (previous <= 0) {
    return { kind: 'none', percent: 0 };
  }
  const change = ((current - previous) / previous) * 100;
  if (Math.abs(change) < STABLE_BELOW_PERCENT) {
    return { kind: 'stable', percent: 0 };
  }
  return { kind: change > 0 ? 'up' : 'down', percent: Math.round(Math.abs(change)) };
}

const COMPARED_TO = 'par rapport à hier à la même heure';

/** La variation en toutes lettres : ni le signe ni la couleur ne portent seuls le sens (DESIGN.md §7). */
export function describeVariation(value: Variation): string {
  switch (value.kind) {
    case 'up':
      return `en hausse de ${value.percent} % ${COMPARED_TO}`;
    case 'down':
      return `en baisse de ${value.percent} % ${COMPARED_TO}`;
    case 'stable':
      return `stable ${COMPARED_TO}`;
    case 'none':
      return 'pas de vente hier à la même heure pour comparer';
  }
}

export interface PaymentLine {
  method: string;
  amountFcfa: number;
  count: number;
}

export interface PaymentShare extends PaymentLine {
  percent: number;
}

/** Part de chaque moyen de paiement dans le total, du plus important au plus faible. */
export function paymentShares(lines: readonly PaymentLine[]): PaymentShare[] {
  const total = lines.reduce((sum, line) => sum + line.amountFcfa, 0);
  return [...lines]
    .sort((a, b) => b.amountFcfa - a.amountFcfa)
    .map((line) => ({ ...line, percent: total > 0 ? Math.round((line.amountFcfa / total) * 100) : 0 }));
}

/** Heures écoulées depuis l'ouverture ; jamais négatif (horloges légèrement décalées). */
export function hoursOpen(openedAt: string, now: Date): number {
  return Math.max((now.getTime() - Date.parse(openedAt)) / HOUR_MS, 0);
}

/** Une caisse ouverte depuis plus de 12 heures est probablement oubliée (RG-08). */
export function isStaleSession(openedAt: string, now: Date): boolean {
  return hoursOpen(openedAt, now) > STALE_AFTER_HOURS;
}

export type RankingCriterionName = 'quantity' | 'revenue' | 'margin';
