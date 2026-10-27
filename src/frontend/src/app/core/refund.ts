import { PaymentMethod } from '../domain/types';

/** Ce qu'il faut savoir d'une vente pour estimer un remboursement : les mêmes chiffres que ceux que le serveur utilise. */
export interface RefundSale {
  lines: { quantity: number; totalFcfa: number }[];
  grossTotalFcfa: number;
  discountFcfa: number;
  payments: { method: PaymentMethod; amountFcfa: number }[];
  /** Les retours déjà faits : quantités par ligne, et ce qui a été rendu par moyen de paiement. */
  returns: { lines: { lineIndex: number; quantity: number }[]; refund: { method: PaymentMethod; amountFcfa: number }[] }[];
}

export interface RefundEstimate {
  totalFcfa: number;
  parts: { method: PaymentMethod; amountFcfa: number }[];
}

/** total × part ÷ ensemble, arrondi au franc le plus proche (demi vers le haut) : les mêmes entiers que le serveur (docs/decisions/0020). */
const share = (total: number, part: number, whole: number): number => (whole === 0 ? 0 : Math.floor((total * part * 2 + whole) / (whole * 2)));

function netRefunded(sale: RefundSale, returned: number[]): number {
  const gross = sale.lines.reduce((sum, line, index) => sum + share(line.totalFcfa, returned[index], line.quantity), 0);
  const discount = sale.grossTotalFcfa === 0 ? 0 : share(sale.discountFcfa, gross, sale.grossTotalFcfa);
  return gross - discount;
}

/**
 * Estimation, avant l'envoi, de ce que le client va récupérer : calcul sur le cumul de ce qui est rendu puis retranché de ce qui l'a déjà été
 * (les arrondis s'annulent), réparti dette du client d'abord, espèces ensuite, Mobile Money pour finir. Le serveur recalcule et fait foi.
 */
export function estimateRefund(sale: RefundSale, requested: { lineIndex: number; quantity: number }[]): RefundEstimate {
  if (requested.length === 0) {
    return { totalFcfa: 0, parts: [] };
  }
  const already = sale.lines.map(() => 0);
  for (const done of sale.returns) {
    for (const line of done.lines) {
      already[line.lineIndex] += line.quantity;
    }
  }
  const now = [...already];
  for (const item of requested) {
    now[item.lineIndex] += item.quantity;
  }
  const amount = netRefunded(sale, now) - netRefunded(sale, already);

  const paid = new Map<PaymentMethod, number>();
  for (const payment of sale.payments) {
    paid.set(payment.method, (paid.get(payment.method) ?? 0) + payment.amountFcfa);
  }
  for (const part of sale.returns.flatMap((done) => done.refund)) {
    paid.set(part.method, (paid.get(part.method) ?? 0) - part.amountFcfa);
  }
  const rank = (method: PaymentMethod) => (method === 'Credit' ? 0 : method === 'Cash' ? 1 : 2);
  const parts: RefundEstimate['parts'] = [];
  let remaining = amount;
  for (const method of [...paid.keys()].sort((a, b) => rank(a) - rank(b))) {
    const take = Math.min(remaining, Math.max(paid.get(method) ?? 0, 0));
    if (take > 0) {
      parts.push({ method, amountFcfa: take });
      remaining -= take;
    }
  }
  return { totalFcfa: amount - remaining, parts };
}
