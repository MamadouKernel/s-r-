import { formatFcfa } from './money';

/** Unité de vente d'un produit (docs/decisions/0017). Les quantités restent des entiers : en grammes ou millilitres pour un produit pesé. */
export type MeasureUnit = 'Piece' | 'Kilogram' | 'Litre';

/** Plus grande quantité acceptée (en unités stockées) : bien en dessous du maximum de l'entier 32 bits du serveur. */
const MAX_QUANTITY = 2_000_000_000;

export function scaleOf(unit: MeasureUnit = 'Piece'): number {
  return unit === 'Piece' ? 1 : 1000;
}

export function unitSymbol(unit: MeasureUnit = 'Piece'): string {
  return unit === 'Kilogram' ? 'kg' : unit === 'Litre' ? 'L' : '';
}

/** « 1,250 kg », « 0,500 L », « 3 » : la virgule française et trois décimales pour un produit pesé ; le nombre tel quel pour des pièces. */
export function formatQuantity(quantity: number, unit: MeasureUnit = 'Piece'): string {
  if (unit === 'Piece') {
    return String(quantity);
  }
  const sign = quantity < 0 ? '-' : '';
  const absolute = Math.abs(quantity);
  const whole = Math.floor(absolute / 1000)
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  const fraction = String(absolute % 1000).padStart(3, '0');
  return `${sign}${whole},${fraction} ${unitSymbol(unit)}`;
}

export type QuantityParse = { ok: true; quantity: number } | { ok: false; message: string };

const noun = (unit: MeasureUnit) => (unit === 'Kilogram' ? 'poids' : 'quantité');

/** Lit ce que tape le caissier : virgule ou point, trois décimales au plus ; rend la quantité entière en grammes ou millilitres. */
export function parseQuantity(text: string, unit: MeasureUnit): QuantityParse {
  const cleaned = text.replace(/[\s ]/g, '').replace(',', '.');
  if (unit === 'Piece') {
    return /^\d+$/.test(cleaned) && Number(cleaned) > 0 && Number(cleaned) <= MAX_QUANTITY
      ? { ok: true, quantity: Number(cleaned) }
      : { ok: false, message: 'Écrivez un nombre entier de pièces.' };
  }
  const example = unit === 'Kilogram' ? 'Écrivez un poids, par exemple 1,5.' : 'Écrivez une quantité en litres, par exemple 1,5.';
  const match = /^(\d+)(?:\.(\d*))?$/.exec(cleaned);
  if (!match) {
    return { ok: false, message: example };
  }
  const decimals = match[2] ?? '';
  if (decimals.length > 3) {
    return { ok: false, message: `Écrivez ${unit === 'Kilogram' ? 'le poids' : 'la quantité'} avec trois décimales au plus.` };
  }
  const quantity = Number(match[1]) * 1000 + Number(decimals.padEnd(3, '0'));
  if (quantity <= 0) {
    return { ok: false, message: `${unit === 'Kilogram' ? 'Le poids doit' : 'La quantité doit'} être supérieur${unit === 'Kilogram' ? '' : 'e'} à 0.` };
  }
  return quantity > MAX_QUANTITY ? { ok: false, message: `${noun(unit) === 'poids' ? 'Ce poids' : 'Cette quantité'} est trop grand${noun(unit) === 'poids' ? '' : 'e'}.` } : { ok: true, quantity };
}

/** Total d'une ligne : prix d'une unité × quantité, arrondi au franc, demi vers le haut. Le même calcul que le serveur. */
export function lineTotal(unitPriceFcfa: number, quantity: number, unit: MeasureUnit = 'Piece'): number {
  const scale = scaleOf(unit);
  return Math.floor((unitPriceFcfa * quantity + scale / 2) / scale);
}

export type AmountParse = { ok: true; quantity: number; totalFcfa: number } | { ok: false; message: string };

/** « Pour 1 000 francs de riz » : la quantité arrondie au gramme, puis le total réel recalculé (il peut différer de quelques francs). */
export function amountToQuantity(amountFcfa: number, pricePerUnitFcfa: number, unit: MeasureUnit = 'Kilogram'): AmountParse {
  if (!Number.isFinite(amountFcfa) || amountFcfa <= 0) {
    return { ok: false, message: 'Écrivez un montant en FCFA.' };
  }
  if (pricePerUnitFcfa <= 0) {
    return { ok: false, message: 'Ce produit n’a pas de prix : impossible de calculer le poids.' };
  }
  const scale = scaleOf(unit);
  const quantity = Math.floor((amountFcfa * scale) / pricePerUnitFcfa + 0.5);
  if (quantity < 1) {
    return { ok: false, message: 'Ce montant est trop petit pour ce produit.' };
  }
  if (quantity > MAX_QUANTITY) {
    return { ok: false, message: 'Ce montant est trop grand pour ce produit.' };
  }
  return { ok: true, quantity, totalFcfa: lineTotal(pricePerUnitFcfa, quantity, unit) };
}

/** « 1,250 kg · 1 000 FCFA » : la quantité et le total de la ligne, tels que le client les lit. */
export function quantityWords(quantity: number, unit: MeasureUnit, unitPriceFcfa: number): string {
  return `${formatQuantity(quantity, unit)} · ${formatFcfa(lineTotal(unitPriceFcfa, quantity, unit))}`;
}
