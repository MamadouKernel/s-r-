import { SaleOperation } from '../data/journal';
import { formatQuantity, lineTotal, MeasureUnit } from './measure';
import { MOBILE_MONEY_METHODS, PaymentMethod } from '../domain/types';

/** Le ticket de caisse (docs/specs/etiquettes-tickets lot 2) : tout ce qu'il montre, déjà connu de l'appareil, donc imprimable sans réseau. */
export interface Receipt {
  commerce: string;
  /** Instant de la vente (ISO). */
  at: string;
  cashier: string;
  register: string;
  /** Rang de la vente sur cette caisse ce jour-là : une référence lisible, pas une numérotation légale (ADR 0016). */
  rank: number;
  /** `quantity` est en grammes ou millilitres pour une ligne pesée ou mesurée (`unit`) ; `unitPriceFcfa` est alors le prix d'un kilo ou d'un litre. */
  lines: { name: string; quantity: number; unit?: MeasureUnit; unitPriceFcfa: number; totalFcfa: number }[];
  discountFcfa: number;
  totalFcfa: number;
  payments: { label: string; amountFcfa: number }[];
  /** Espèces remises par le client, `null` sans paiement en espèces. */
  tenderedFcfa: number | null;
  changeFcfa: number;
  /** Vente à crédit : le client et ce qu'il reste à payer. */
  customer: { name: string; creditFcfa: number } | null;
  /** Présent sur un ticket de retour ou d'annulation : le ticket d'origine et ce qui est rendu au client (docs/decisions/0020). */
  reversal?: { title: string; originalTicket: number; reason: string };
}

export interface ReversalReceiptInput {
  commerce: string;
  cashier: string;
  register: string;
  at: string;
  /** Le numéro du ticket de la vente d'origine. */
  saleTicket: number;
  kind: 'Return' | 'Cancellation';
  reason: string;
  /** Les articles rendus, avec le montant payé pour eux. */
  lines: Receipt['lines'];
  /** Ce qui est rendu au client, par moyen de paiement d'origine. */
  refund: { method: PaymentMethod; amountFcfa: number }[];
}

export interface ReceiptContext {
  commerce: string;
  cashier: string;
  register: string;
  rank: number;
  customer: { name: string } | null;
}

/** Largeur du papier : 58 mm (32 colonnes) ou 80 mm (42 colonnes). */
export type TicketWidth = 58 | 80;

const COLUMNS: Record<TicketWidth, number> = { 58: 32, 80: 42 };
/** Largeur utile du papier en millimètres (le reste est la marge de l'imprimante) et corps de caractère qui y fait tenir les colonnes. */
const PAPER: Record<TicketWidth, { widthMm: number; fontMm: number }> = { 58: { widthMm: 48, fontMm: 2.5 }, 80: { widthMm: 72, fontMm: 2.86 } };

export function methodLabel(method: PaymentMethod): string {
  if (method === 'Cash') {
    return 'Espèces';
  }
  if (method === 'Credit') {
    return 'Crédit';
  }
  return MOBILE_MONEY_METHODS.find((option) => option.method === method)?.label ?? method;
}

export function buildReceipt(sale: SaleOperation, context: ReceiptContext): Receipt {
  const cash = sale.payments.filter((payment) => payment.method === 'Cash' && payment.tenderedFcfa !== undefined);
  const credit = sale.payments.filter((payment) => payment.method === 'Credit').reduce((sum, payment) => sum + payment.amountFcfa, 0);
  return {
    commerce: context.commerce,
    at: sale.at,
    cashier: context.cashier,
    register: context.register,
    rank: context.rank,
    lines: sale.lines.map((line) => ({
      name: line.name,
      quantity: line.quantity,
      ...(line.unit && line.unit !== 'Piece' ? { unit: line.unit } : {}),
      unitPriceFcfa: line.unitPriceFcfa,
      totalFcfa: lineTotal(line.unitPriceFcfa, line.quantity, line.unit),
    })),
    discountFcfa: sale.discountFcfa,
    totalFcfa: sale.netTotalFcfa,
    payments: sale.payments.map((payment) => ({ label: methodLabel(payment.method), amountFcfa: payment.amountFcfa })),
    tenderedFcfa: cash.length > 0 ? cash.reduce((sum, payment) => sum + (payment.tenderedFcfa ?? 0), 0) : null,
    changeFcfa: sale.changeFcfa,
    customer: context.customer && credit > 0 ? { name: context.customer.name, creditFcfa: credit } : null,
  };
}

function refundLabel(method: PaymentMethod): string {
  if (method === 'Cash') {
    return 'Rendu en espèces';
  }
  return method === 'Credit' ? 'Dette effacée' : `À rembourser par ${methodLabel(method)}`;
}

/** Le ticket de retour ou d'annulation : fabriqué avec ce que le serveur a calculé, imprimé comme un ticket de caisse. */
export function buildReversalReceipt(input: ReversalReceiptInput): Receipt {
  return {
    commerce: input.commerce,
    at: input.at,
    cashier: input.cashier,
    register: input.register,
    rank: input.saleTicket,
    lines: input.lines,
    discountFcfa: 0,
    totalFcfa: input.refund.reduce((sum, part) => sum + part.amountFcfa, 0),
    payments: input.refund.map((part) => ({ label: refundLabel(part.method), amountFcfa: part.amountFcfa })),
    tenderedFcfa: null,
    changeFcfa: 0,
    customer: null,
    reversal: { title: input.kind === 'Cancellation' ? 'VENTE ANNULÉE' : 'TICKET DE RETOUR', originalTicket: input.saleTicket, reason: input.reason },
  };
}

function localDay(iso: string): string {
  const date = new Date(iso);
  return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

/** Le rang de la vente `at` parmi les ventes du même jour (heure locale) de cette caisse, elle comprise. Au moins 1. */
export function daySaleRank(at: string, saleTimes: readonly string[]): number {
  const day = localDay(at);
  const moment = Date.parse(at);
  return Math.max(1, saleTimes.filter((time) => localDay(time) === day && Date.parse(time) <= moment).length);
}

// ---- Mise en forme texte ----------------------------------------------------------------------------------------------------------------

function amount(value: number): string {
  return `${Math.trunc(value).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ' ')} FCFA`;
}

function wrap(text: string, columns: number): string[] {
  const lines: string[] = [];
  let current = '';
  for (const word of text.split(/\s+/).filter(Boolean)) {
    let rest = word;
    while (rest.length > columns) {
      if (current) {
        lines.push(current);
        current = '';
      }
      lines.push(rest.slice(0, columns));
      rest = rest.slice(columns);
    }
    if (current && current.length + 1 + rest.length > columns) {
      lines.push(current);
      current = rest;
    } else {
      current = current ? `${current} ${rest}` : rest;
    }
  }
  return current ? [...lines, current] : lines;
}

const center = (text: string, columns: number): string[] =>
  wrap(text, columns).map((line) => ' '.repeat(Math.floor((columns - line.length) / 2)) + line);

/** Une ligne « libellé ........ montant » sur toute la largeur ; si elle ne tient pas, le montant passe seul à droite sur la ligne suivante. */
function row(left: string, right: string, columns: number): string[] {
  if (left.length + 1 + right.length <= columns) {
    return [left + ' '.repeat(columns - left.length - right.length) + right];
  }
  return [...wrap(left, columns), ' '.repeat(columns - right.length) + right];
}

const pad = (value: number): string => value.toString().padStart(2, '0');

function dateTime(iso: string): string {
  const date = new Date(iso);
  return `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Le ticket en texte à chasse fixe : aucune ligne ne dépasse `columns`, jamais d'espace insécable (certaines imprimantes le dessinent mal). */
export function receiptText(receipt: Receipt, columns: number): string {
  const rule = '-'.repeat(columns);
  const out: string[] = [
    ...center(receipt.commerce, columns),
    dateTime(receipt.at),
    ...wrap(`Caissier : ${receipt.cashier}`, columns),
    ...wrap(receipt.reversal ? receipt.register : `${receipt.register} · n° ${receipt.rank}`, columns),
    ...(receipt.reversal ? [...center(receipt.reversal.title, columns), ...wrap(`Ticket d'origine n° ${receipt.reversal.originalTicket}`, columns)] : []),
    rule,
  ];
  for (const line of receipt.lines) {
    out.push(...wrap(line.name, columns), ...row(`  ${formatQuantity(line.quantity, line.unit).replace(/\u00a0/g, ' ')} x ${amount(line.unitPriceFcfa)}`, amount(line.totalFcfa), columns));
  }
  out.push(rule);
  if (receipt.discountFcfa > 0) {
    out.push(...row('Remise', `-${amount(receipt.discountFcfa)}`, columns));
  }
  out.push(...row(receipt.reversal ? 'TOTAL RENDU' : 'TOTAL', amount(receipt.totalFcfa), columns));
  for (const payment of receipt.payments) {
    out.push(...row(payment.label, amount(payment.amountFcfa), columns));
  }
  if (receipt.tenderedFcfa !== null) {
    out.push(...row('Reçu', amount(receipt.tenderedFcfa), columns));
  }
  if (receipt.changeFcfa > 0) {
    out.push(...row('Monnaie rendue', amount(receipt.changeFcfa), columns));
  }
  if (receipt.customer) {
    out.push(...wrap(`Client : ${receipt.customer.name}`, columns), ...row('Reste à payer', amount(receipt.customer.creditFcfa), columns));
  }
  if (receipt.reversal) {
    out.push(...wrap(`Motif : ${receipt.reversal.reason}`, columns), rule, ...center('Ticket de retour', columns), ...center('ne vaut pas facture', columns));
    return out.join('\n');
  }
  out.push(rule, ...center('Merci de votre visite', columns), ...center('Ticket de caisse', columns), ...center('ne vaut pas facture', columns));
  return out.join('\n');
}

export function ticketColumns(width: TicketWidth): number {
  return COLUMNS[width];
}

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

/** La page du ticket pour la fenêtre d'impression de l'appareil : le texte à chasse fixe, à la largeur utile du papier. */
export function receiptDocument(receipt: Receipt, width: TicketWidth): string {
  const paper = PAPER[width];
  return `<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<title>Ticket de caisse</title>
<style>
@page { margin: 0; }
* { box-sizing: border-box; }
body { margin: 0; color: #000; background: #fff; }
.ticket { width: ${paper.widthMm}mm; margin: 0 auto; padding: 2mm 0; }
pre { margin: 0; font-family: 'Courier New', Courier, monospace; font-size: ${paper.fontMm}mm; line-height: 1.25; white-space: pre; }
</style>
</head>
<body><div class="ticket"><pre>${escapeHtml(receiptText(receipt, COLUMNS[width]))}</pre></div></body>
</html>`;
}
