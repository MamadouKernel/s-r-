import { barcodeSvg, BarcodeSymbol, CODE128_PATTERNS, encodeBarcode, isValidEan13 } from './barcode';

// ---- Relecteurs indépendants du dessin : ils relisent les barres comme le ferait un lecteur ---------------------------------------------

const EAN_L = ['0001101', '0011001', '0010011', '0111101', '0100011', '0110001', '0101111', '0111011', '0110111', '0001011'];
const EAN_G = ['0100111', '0110011', '0011011', '0100001', '0011101', '0111001', '0000101', '0010001', '0001001', '0010111'];
const EAN_R = EAN_L.map((pattern) => [...pattern].map((bit) => (bit === '0' ? '1' : '0')).join(''));
const EAN_PARITY = ['LLLLLL', 'LLGLGG', 'LLGGLG', 'LLGGGL', 'LGLLGG', 'LGGLLG', 'LGGGLL', 'LGLGLG', 'LGLGGL', 'LGGLGL'];

const bits = (modules: boolean[]) => modules.map((bar) => (bar ? '1' : '0')).join('');

function decodeEan13(modules: boolean[]): string | null {
  const text = bits(modules);
  if (text.length !== 95 || !text.startsWith('101') || text.slice(45, 50) !== '01010' || !text.endsWith('101')) {
    return null;
  }
  const left = Array.from({ length: 6 }, (_, index) => text.slice(3 + index * 7, 10 + index * 7));
  const right = Array.from({ length: 6 }, (_, index) => text.slice(50 + index * 7, 57 + index * 7));
  let parity = '';
  let digits = '';
  for (const group of left) {
    const l = EAN_L.indexOf(group);
    const g = EAN_G.indexOf(group);
    if (l >= 0) {
      parity += 'L';
      digits += l;
    } else if (g >= 0) {
      parity += 'G';
      digits += g;
    } else {
      return null;
    }
  }
  const first = EAN_PARITY.indexOf(parity);
  const tail = right.map((group) => EAN_R.indexOf(group));
  return first < 0 || tail.includes(-1) ? null : `${first}${digits}${tail.join('')}`;
}

function decodeCode128(modules: boolean[]): string | null {
  const text = bits(modules);
  const widths = CODE128_PATTERNS.map((pattern) => pattern);
  const symbols: number[] = [];
  let at = 0;
  const readSymbol = (): number => {
    for (let value = 0; value < 106; value++) {
      const pattern = widths[value];
      const chunk = text.slice(at, at + 11);
      if (chunk === runs(pattern)) {
        at += 11;
        return value;
      }
    }
    return -1;
  };
  // Les motifs sont des largeurs alternées barre/espace : on les rend en 0/1 pour comparer.
  function runs(pattern: string): string {
    let bar = true;
    return [...pattern].map((width) => {
      const piece = (bar ? '1' : '0').repeat(Number(width));
      bar = !bar;
      return piece;
    }).join('');
  }
  const start = readSymbol();
  if (start !== 104) {
    return null;
  }
  symbols.push(start);
  const values: number[] = [];
  while (text.length - at > 13) {
    const value = readSymbol();
    if (value < 0) {
      return null;
    }
    values.push(value);
  }
  const checksum = values.pop();
  let sum = 104;
  values.forEach((value, index) => (sum += value * (index + 1)));
  if (checksum !== sum % 103 || text.slice(at) !== runs(widths[106])) {
    return null;
  }
  return values.map((value) => String.fromCharCode(value + 32)).join('');
}

describe('isValidEan13', () => {
  it('checks the control digit', () => {
    expect(isValidEan13('6111000000014')).toBe(true);
    expect(isValidEan13('2900000000018')).toBe(true);
    expect(isValidEan13('6111000000011')).toBe(false);
    expect(isValidEan13('611100000001')).toBe(false);
    expect(isValidEan13('611100000001a')).toBe(false);
  });
});

describe('encodeBarcode: EAN-13', () => {
  it('draws a valid EAN-13 as 95 modules with the guard bars', () => {
    const symbol = encodeBarcode('6111000000014')!;

    expect(symbol.kind).toBe('ean13');
    expect(symbol.modules).toHaveLength(95);
    expect(bits(symbol.modules).startsWith('101')).toBe(true);
    expect(bits(symbol.modules).slice(45, 50)).toBe('01010');
    expect(bits(symbol.modules).endsWith('101')).toBe(true);
    expect(symbol.text).toBe('6111000000014');
  });

  it.each(['6111000000014', '2900000000018', '4006381333931', '0000000000000', '9780201379624', '5901234123457'])(
    'is read back exactly: %s',
    (code) => expect(decodeEan13(encodeBarcode(code)!.modules)).toBe(code),
  );

  it('uses the left tables of the standard (digit 0 is 0001101)', () => {
    // 0000000000000 : premier chiffre 0 donc six groupes L de « 0 », puis six groupes R de « 0 ».
    const text = bits(encodeBarcode('0000000000000')!.modules);
    expect(text.slice(3, 10)).toBe('0001101');
    expect(text.slice(50, 57)).toBe('1110010');
  });
});

describe('encodeBarcode: Code 128', () => {
  it.each(['111', 'RIZ-25', '6111000000011', 'Riz parfumé'.replace('é', 'e'), 'a', '~ ', '12345678901234567890'])(
    'is read back exactly: %s',
    (code) => {
      const symbol = encodeBarcode(code)!;
      expect(symbol.kind).toBe('code128');
      expect(decodeCode128(symbol.modules)).toBe(code);
    },
  );

  it('is used for an EAN-13 whose control digit is wrong, never drawn as a false EAN', () => {
    const symbol = encodeBarcode('6111000000011')!;

    expect(symbol.kind).toBe('code128');
    expect(decodeCode128(symbol.modules)).toBe('6111000000011');
  });

  it('has the right size: start + characters + checksum, 11 modules each, then the 13 modules of the stop', () => {
    const symbol = encodeBarcode('RIZ-25')!;

    expect(symbol.modules).toHaveLength(11 * (1 + 6 + 1) + 13);
  });

  it('keeps a coherent pattern table: 106 symbols of 11 modules (3 bars, 3 spaces), all different, and the stop of 13', () => {
    expect(CODE128_PATTERNS).toHaveLength(107);
    const symbols = CODE128_PATTERNS.slice(0, 106);
    expect(new Set(symbols).size).toBe(106);
    for (const pattern of symbols) {
      expect(pattern).toHaveLength(6);
      expect([...pattern].reduce((sum, width) => sum + Number(width), 0)).toBe(11);
      expect([...pattern].every((width) => Number(width) >= 1 && Number(width) <= 4)).toBe(true);
    }
    expect(CODE128_PATTERNS[104]).toBe('211214'); // Start B
    expect(CODE128_PATTERNS[33]).toBe('111323'); // « A » en jeu B
    expect(CODE128_PATTERNS[106]).toBe('2331112'); // Stop
  });

  it.each(['', 'é', 'Riz ', '日本'])('cannot draw %j and says so with null', (code) => expect(encodeBarcode(code)).toBeNull());
});

describe('barcodeSvg', () => {
  it('turns the symbol into a self-contained SVG with one rectangle per bar', () => {
    const symbol: BarcodeSymbol = { kind: 'code128', modules: [true, true, false, true, false, false, true], text: 'x' };

    const svg = barcodeSvg(symbol, { height: 30 });

    expect(svg.startsWith('<svg')).toBe(true);
    expect(svg).toContain('xmlns="http://www.w3.org/2000/svg"');
    expect(svg).toContain('viewBox="0 0 7 30"');
    expect(svg.match(/<rect /g)).toHaveLength(3);
    expect(svg).toContain('<rect x="0" y="0" width="2" height="30"');
    expect(svg).toContain('<rect x="3" y="0" width="1" height="30"');
    expect(svg).toContain('<rect x="6" y="0" width="1" height="30"');
    expect(svg).not.toContain('<script');
  });
});
