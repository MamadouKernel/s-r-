import { barcodeSvg, BarcodeSymbol, encodeBarcode } from '../core/barcode';
import { formatFcfa } from '../core/money';
import { ManagedProduct } from './products-api';

export type LabelFormatId = 'a4' | 'roll';

export interface LabelFormat {
  id: LabelFormatId;
  name: string;
  columns: number;
  rows: number;
  /** Taille d'une étiquette, en millimètres. */
  widthMm: number;
  heightMm: number;
}

/** Les deux présentations proposées (ADR 0016). Les dimensions ne sont écrites qu'ici : un réglage de marge se fait en un seul endroit. */
export const LABEL_FORMATS: Record<LabelFormatId, LabelFormat> = {
  a4: { id: 'a4', name: 'Feuille A4 — 24 étiquettes (3 × 8)', columns: 3, rows: 8, widthMm: 70, heightMm: 37 },
  roll: { id: 'roll', name: 'Rouleau 50 × 30 mm', columns: 1, rows: 1, widthMm: 50, heightMm: 30 },
};

export const MAX_COPIES = 99;

export interface LabelItem {
  title: string;
  subtitle: string | null;
  price: string;
  /** Le code tel qu'il est écrit sous les barres. */
  code: string;
  /** Le dessin des barres ; `null` quand le code contient un caractère qu'aucun code-barres ne porte. */
  symbol: BarcodeSymbol | null;
}

export interface LabelSelection {
  product: ManagedProduct;
  copies: number;
}

export function labelOf(product: ManagedProduct): LabelItem {
  const code = (product.barcode ?? '').trim();
  return {
    title: product.name,
    subtitle: product.formatLabel?.trim() ? product.formatLabel.trim() : null,
    price: formatFcfa(product.salePriceFcfa),
    code,
    symbol: encodeBarcode(code),
  };
}

export function clampCopies(value: number): number {
  return Number.isFinite(value) ? Math.min(Math.max(Math.trunc(value), 1), MAX_COPIES) : 1;
}

export function clampStart(value: number, sheetSize: number): number {
  return Number.isFinite(value) ? Math.min(Math.max(Math.trunc(value), 1), sheetSize) : 1;
}

/** Les produits choisis (au moins un exemplaire) qui n'ont pas de code-barres : l'impression est refusée tant qu'il en reste. */
export function blockedProducts(selection: readonly LabelSelection[]): ManagedProduct[] {
  return selection.filter((item) => item.copies > 0 && !(item.product.barcode ?? '').trim()).map((item) => item.product);
}

/**
 * Répartit les étiquettes en pages. Sur une planche, `startAt` est la première case à remplir (pour réutiliser une planche entamée) et les
 * cases vides valent `null` ; sur un rouleau, une étiquette fait une page.
 */
export function planLabels(selection: readonly LabelSelection[], formatId: LabelFormatId, startAt: number): (LabelItem | null)[][] {
  const labels = selection.filter((item) => item.copies > 0).flatMap((item) => Array.from({ length: item.copies }, () => labelOf(item.product)));
  const format = LABEL_FORMATS[formatId];
  if (formatId === 'roll') {
    return labels.map((label) => [label]);
  }
  const size = format.columns * format.rows;
  const offset = clampStart(startAt, size) - 1;
  const cells: (LabelItem | null)[] = [...Array<null>(offset).fill(null), ...labels];
  const pages: (LabelItem | null)[][] = [];
  for (let index = 0; index < cells.length; index += size) {
    const page = cells.slice(index, index + size);
    pages.push([...page, ...Array<null>(size - page.length).fill(null)]);
  }
  return labels.length === 0 ? [] : pages;
}


function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function labelHtml(label: LabelItem | null): string {
  if (label === null) {
    return '<div class="label empty"></div>';
  }
  const bars = label.symbol
    ? `<div class="bars">${barcodeSvg(label.symbol, { height: 10 })}</div><div class="code">${escapeHtml(label.code)}</div>`
    : '<div class="bars missing">Code non imprimable</div>';
  const subtitle = label.subtitle ? `<div class="sub">${escapeHtml(label.subtitle)}</div>` : '';
  return `<div class="label"><div class="name">${escapeHtml(label.title)}</div>${subtitle}<div class="price">${escapeHtml(label.price)}</div>${bars}</div>`;
}

/**
 * La page complète des étiquettes : la même sert à l'aperçu et à l'impression (fenêtre d'impression de l'appareil, qui lit `@page` pour le
 * format du papier). Tout ce qui vient d'un produit est échappé.
 */
export function labelsDocument(pages: readonly (readonly (LabelItem | null)[])[], formatId: LabelFormatId): string {
  const format = LABEL_FORMATS[formatId];
  const paperWidth = format.columns * format.widthMm;
  const paperHeight = formatId === 'a4' ? 297 : format.heightMm;
  const sheets = pages.map((page) => `<section class="page">${page.map(labelHtml).join('')}</section>`).join('');
  return `<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<title>Étiquettes</title>
<style>
@page { size: ${formatId === 'a4' ? '210mm' : `${paperWidth}mm`} ${paperHeight}mm; margin: 0; }
* { box-sizing: border-box; }
body { margin: 0; font-family: Arial, Helvetica, sans-serif; color: #000; }
.page { width: ${formatId === 'a4' ? 210 : paperWidth}mm; height: ${paperHeight}mm; display: grid; grid-template-columns: repeat(${format.columns}, ${format.widthMm}mm); grid-template-rows: repeat(${format.rows}, ${format.heightMm}mm); break-after: page; page-break-after: always; overflow: hidden; }
.label { width: ${format.widthMm}mm; height: ${format.heightMm}mm; padding: ${formatId === 'a4' ? '2.5mm 4mm' : '1.5mm 2.5mm'}; overflow: hidden; display: flex; flex-direction: column; justify-content: space-between; }
.name { font-weight: bold; font-size: ${formatId === 'a4' ? '3.8mm' : '3.2mm'}; line-height: 1.1; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.sub { font-size: ${formatId === 'a4' ? '3.2mm' : '2.8mm'}; line-height: 1.1; }
.price { font-weight: bold; font-size: ${formatId === 'a4' ? '5mm' : '4.4mm'}; }
.bars { height: ${formatId === 'a4' ? '11mm' : '8mm'}; padding: 0 2mm; }
.bars svg { width: 100%; height: 100%; display: block; }
.bars.missing { font-size: 3mm; font-style: italic; }
.code { font-family: 'Courier New', monospace; font-size: 2.8mm; text-align: center; letter-spacing: 0.3mm; }
@media screen { body { background: #d8dcd6; } .page { background: #fff; margin: 8px auto; box-shadow: 0 1px 4px rgba(0,0,0,.3); } .label { outline: 1px dashed #bbb; outline-offset: -1px; } }
</style>
</head>
<body class="${formatId}">${sheets}</body>
</html>`;
}
