/**
 * Dessin des codes-barres de l'étiquette (docs/specs/etiquettes-tickets ETQ-02), sans bibliothèque et hors ligne : EAN-13 pour un code valide,
 * Code 128 (jeu B) pour tout autre texte imprimable. Le résultat est une suite de modules (vrai = barre) que `barcodeSvg` met en forme.
 */

export interface BarcodeSymbol {
  kind: 'ean13' | 'code128';
  /** Un module par unité de largeur, de gauche à droite ; vrai pour une barre noire. */
  modules: boolean[];
  /** Le texte lisible à écrire sous les barres. */
  text: string;
}

// ---- EAN-13 ------------------------------------------------------------------------------------------------------------------------------

const EAN_L = ['0001101', '0011001', '0010011', '0111101', '0100011', '0110001', '0101111', '0111011', '0110111', '0001011'];
const EAN_G = ['0100111', '0110011', '0011011', '0100001', '0011101', '0111001', '0000101', '0010001', '0001001', '0010111'];
const EAN_R = EAN_L.map((pattern) => [...pattern].map((bit) => (bit === '0' ? '1' : '0')).join(''));
/** Pour chaque premier chiffre, le jeu (L ou G) des six chiffres de gauche. */
const EAN_PARITY = ['LLLLLL', 'LLGLGG', 'LLGGLG', 'LLGGGL', 'LGLLGG', 'LGGLLG', 'LGGGLL', 'LGLGLG', 'LGLGGL', 'LGGLGL'];

/** Clé de contrôle EAN-13 : poids 1 et 3 sur les douze premiers chiffres. */
function checkDigit(twelve: string): number {
  let sum = 0;
  for (let index = 0; index < twelve.length; index++) {
    sum += Number(twelve[index]) * (index % 2 === 0 ? 1 : 3);
  }
  return (10 - (sum % 10)) % 10;
}

export function isValidEan13(code: string): boolean {
  return /^\d{13}$/.test(code) && checkDigit(code.slice(0, 12)) === Number(code[12]);
}

function ean13Modules(code: string): boolean[] {
  const first = Number(code[0]);
  const parity = EAN_PARITY[first];
  let pattern = '101';
  for (let index = 0; index < 6; index++) {
    const digit = Number(code[1 + index]);
    pattern += parity[index] === 'L' ? EAN_L[digit] : EAN_G[digit];
  }
  pattern += '01010';
  for (let index = 7; index < 13; index++) {
    pattern += EAN_R[Number(code[index])];
  }
  pattern += '101';
  return [...pattern].map((bit) => bit === '1');
}

// ---- Code 128, jeu B (caractères 32 à 126) --------------------------------------------------------------------------------------------

/** Largeurs alternées barre / espace de chaque symbole : 0 à 102 (valeurs), 103 à 105 (départs A, B, C), 106 (arrêt, sept éléments). */
export const CODE128_PATTERNS: string[] = [
  '212222', '222122', '222221', '121223', '121322', '131222', '122213', '122312', '132212', '221213',
  '221312', '231212', '112232', '122132', '122231', '113222', '123122', '123221', '223211', '221132',
  '221231', '213212', '223112', '312131', '311222', '321122', '321221', '312212', '322112', '322211',
  '212123', '212321', '232121', '111323', '131123', '131321', '112313', '132113', '132311', '211313',
  '231113', '231311', '112133', '112331', '132131', '113123', '113321', '133121', '313121', '211331',
  '231131', '213113', '213311', '213131', '311123', '311321', '331121', '312113', '312311', '332111',
  '314111', '221411', '431111', '111224', '111422', '121124', '121421', '141122', '141221', '112214',
  '112412', '122114', '122411', '142112', '142211', '241211', '221114', '413111', '241112', '134111',
  '111242', '121142', '121241', '114212', '124112', '124211', '411212', '421112', '421211', '212141',
  '214121', '412121', '111143', '111341', '131141', '114113', '114311', '411113', '411311', '113141',
  '114131', '311141', '411131', '211412', '211214', '211232', '2331112',
];

const START_B = 104;
const STOP = 106;

function patternModules(pattern: string): boolean[] {
  const modules: boolean[] = [];
  let bar = true;
  for (const width of pattern) {
    for (let unit = 0; unit < Number(width); unit++) {
      modules.push(bar);
    }
    bar = !bar;
  }
  return modules;
}

function code128Modules(text: string): boolean[] | null {
  if (text === '' || ![...text].every((character) => character.charCodeAt(0) >= 32 && character.charCodeAt(0) <= 126)) {
    return null;
  }
  const values = [...text].map((character) => character.charCodeAt(0) - 32);
  let checksum = START_B;
  values.forEach((value, index) => (checksum += value * (index + 1)));
  return [START_B, ...values, checksum % 103, STOP].flatMap((symbol) => patternModules(CODE128_PATTERNS[symbol]));
}

/** Le dessin d'un code : EAN-13 s'il est valide, sinon Code 128 ; `null` si le texte contient un caractère que ni l'un ni l'autre ne peut porter. */
export function encodeBarcode(code: string): BarcodeSymbol | null {
  if (isValidEan13(code)) {
    return { kind: 'ean13', modules: ean13Modules(code), text: code };
  }
  const modules = code128Modules(code);
  return modules ? { kind: 'code128', modules, text: code } : null;
}

/** Les barres en SVG autonome : une ligne pleine par suite de modules noirs, hauteur donnée, sans texte (le texte se met en HTML). */
export function barcodeSvg(symbol: BarcodeSymbol, options: { height: number }): string {
  const { height } = options;
  const rects: string[] = [];
  let start = -1;
  symbol.modules.forEach((bar, index) => {
    if (bar && start < 0) {
      start = index;
    }
    if (start >= 0 && (!bar || index === symbol.modules.length - 1)) {
      const end = bar ? index + 1 : index;
      rects.push(`<rect x="${start}" y="0" width="${end - start}" height="${height}" fill="#000"/>`);
      start = -1;
    }
  });
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${symbol.modules.length} ${height}" preserveAspectRatio="none" ` +
    `shape-rendering="crispEdges" role="img" aria-label="Code-barres ${symbol.text.replace(/[<>&"]/g, '')}">${rects.join('')}</svg>`
  );
}
