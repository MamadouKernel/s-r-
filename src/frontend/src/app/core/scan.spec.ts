import { KeyInput, ScanDetector, ScanResult, speedLabel } from './scan';

/** Une touche de chiffre telle qu'un lecteur la « tape », avec la touche physique et le caractère produit par la disposition du clavier. */
const digit = (value: string, at: number, key = value): KeyInput => ({ key, code: `Digit${value}`, at });
const letter = (value: string, at: number): KeyInput => ({ key: value, code: `Key${value.toUpperCase()}`, at });
const enter = (at: number): KeyInput => ({ key: 'Enter', code: 'Enter', at });
const tab = (at: number): KeyInput => ({ key: 'Tab', code: 'Tab', at });

/** Envoie les touches d'un code, une toutes les `gap` ms, à partir de `start`. */
function burst(detector: ScanDetector, code: string, start = 1000, gap = 10): { last: number; results: ScanResult[] } {
  const results: ScanResult[] = [];
  let at = start;
  for (const character of code) {
    const outcome = detector.push(/\d/.test(character) ? digit(character, at) : letter(character, at));
    if (outcome.scan) {
      results.push(outcome.scan);
    }
    at += gap;
  }
  return { last: at - gap, results };
}

describe('ScanDetector', () => {
  it('recognises a fast burst ended by Enter as a scan and swallows the Enter', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011');

    const end = detector.push(enter(last + 10));

    expect(end.scan).toMatchObject({ code: '6111000000011', end: 'enter' });
    expect(end.swallow).toBe(true);
  });

  it('accepts Tab as an ending too', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011');

    const end = detector.push(tab(last + 10));

    expect(end.scan).toMatchObject({ code: '6111000000011', end: 'tab' });
    expect(end.swallow).toBe(true);
  });

  it('recognises a burst with no ending after 100 ms of silence', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011');

    expect(detector.poll(last + 50)).toBeNull();
    expect(detector.poll(last + 100)).toMatchObject({ code: '6111000000011', end: 'silence' });
    expect(detector.poll(last + 300)).toBeNull(); // un scan n'est rendu qu'une fois
  });

  it('never takes a human typing for a scan', () => {
    const detector = new ScanDetector();
    const { last, results } = burst(detector, '6111000000011', 1000, 120);

    const end = detector.push(enter(last + 120));

    expect(results).toEqual([]);
    expect(end.scan).toBeNull();
    expect(end.swallow).toBe(false);
    expect(detector.poll(last + 1000)).toBeNull();
  });

  it('does not take a short burst for a scan', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '12345');

    expect(detector.push(enter(last + 10)).scan).toBeNull();
    expect(detector.poll(last + 500)).toBeNull();
  });

  it('restarts after a pause: only the fast part counts', () => {
    const detector = new ScanDetector();
    burst(detector, '999', 1000, 10);
    const { last } = burst(detector, '6111000000011', 2000, 10);

    expect(detector.push(enter(last + 10)).scan?.code).toBe('6111000000011');
  });

  it('reads digits from the physical key so a mismatched keyboard layout does not matter', () => {
    const detector = new ScanDetector();
    // Un lecteur « américain » sur un clavier français : les touches Digit1… produisent « & é " ' ( … ».
    const layout: Record<string, string> = { 1: '&', 2: 'é', 3: '"', 4: "'", 5: '(', 6: '-', 7: 'è', 8: '_', 9: 'ç', 0: 'à' };
    let at = 1000;
    for (const character of '6111000000011') {
      detector.push(digit(character, at, layout[character]));
      at += 10;
    }

    const end = detector.push(enter(at));

    expect(end.scan).toMatchObject({ code: '6111000000011', corrected: true });
  });

  it('reports no correction when the characters already match the keys', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011');

    expect(detector.push(enter(last + 10)).scan?.corrected).toBe(false);
  });

  it('reads the numeric keypad digits too', () => {
    const detector = new ScanDetector();
    let at = 1000;
    for (const character of '6111000000011') {
      detector.push({ key: 'x', code: `Numpad${character}`, at });
      at += 10;
    }

    expect(detector.push(enter(at)).scan?.code).toBe('6111000000011');
  });

  it('keeps letters and dashes of an alphanumeric code as produced', () => {
    const detector = new ScanDetector();
    let at = 1000;
    for (const input of [letter('a', 0), { key: '-', code: 'Minus', at: 0 }, digit('1', 0), digit('2', 0), digit('3', 0), digit('4', 0), letter('b', 0)]) {
      detector.push({ ...input, at });
      at += 10;
    }

    expect(detector.push(enter(at)).scan?.code).toBe('a-1234b');
  });

  it('ignores a control prefix and suffix and surrounding spaces', () => {
    const detector = new ScanDetector();
    detector.push({ key: 'Unidentified', code: '', at: 990 }); // préfixe (caractère de contrôle)
    detector.push({ key: ' ', code: 'Space', at: 995 });
    const { last } = burst(detector, '6111000000011', 1000, 10);
    detector.push({ key: ' ', code: 'Space', at: last + 5 });
    detector.push({ key: 'Unidentified', code: '', at: last + 8 }); // suffixe

    expect(detector.push(enter(last + 12)).scan?.code).toBe('6111000000011');
  });

  it('measures the speed in milliseconds per character', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011', 1000, 8);

    expect(detector.push(enter(last + 8)).scan?.msPerChar).toBe(8);
  });

  it('counts the characters of the code', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011');

    expect(detector.push(enter(last + 10)).scan?.code).toHaveLength(13);
  });

  it('forgets the buffer after Backspace, Escape and keys with modifiers', () => {
    const detector = new ScanDetector();
    const first = burst(detector, '6111000000011', 1000, 10);
    detector.push({ key: 'Backspace', code: 'Backspace', at: first.last + 10 });
    expect(detector.push(enter(first.last + 20)).scan).toBeNull();

    burst(detector, '6111000000011', 3000, 10);
    detector.push({ key: 'c', code: 'KeyC', at: 3200, ctrl: true });
    expect(detector.push(enter(3210)).scan).toBeNull();
  });

  it('an Enter alone is not a scan and is not swallowed', () => {
    const detector = new ScanDetector();

    const end = detector.push(enter(1000));

    expect(end.scan).toBeNull();
    expect(end.swallow).toBe(false);
  });

  it('two scans in a row are two results', () => {
    const detector = new ScanDetector();
    const first = burst(detector, '6111000000011', 1000, 10);
    const one = detector.push(enter(first.last + 10)).scan;
    const second = burst(detector, '6111000000028', first.last + 500, 10);
    const two = detector.push(enter(second.last + 10)).scan;

    expect([one?.code, two?.code]).toEqual(['6111000000011', '6111000000028']);
  });
});

describe('ScanDetector: late endings and stray keys (audit 25/10/2026)', () => {
  it('still reads a scan whose Enter arrives up to 100 ms after the last digit', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011');

    const end = detector.push(enter(last + 90));

    expect(end.scan).toMatchObject({ code: '6111000000011', end: 'enter' });
    expect(end.swallow).toBe(true);
  });

  it('does not read it when the Enter comes after the silence: that is a person pressing Enter', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011');

    const end = detector.push(enter(last + 150));

    expect(end.scan).toBeNull();
    expect(end.swallow).toBe(false);
  });

  it('keeps the space some readers send after the code from pressing the focused button', () => {
    const detector = new ScanDetector();
    const { last } = burst(detector, '6111000000011');

    const space = detector.push({ key: ' ', code: 'Space', at: last + 5 });

    expect(space.swallow).toBe(true);
    expect(detector.push(enter(last + 12)).scan?.code).toBe('6111000000011');
  });

  it('leaves a space typed by a person alone', () => {
    const detector = new ScanDetector();

    expect(detector.push({ key: ' ', code: 'Space', at: 1000 }).swallow).toBe(false);
    burst(detector, '123456789', 2000, 10);
    expect(detector.push({ key: ' ', code: 'Space', at: 2500 }).swallow).toBe(false);
  });
});

describe('ScanDetector.flush', () => {
  it('gives the scan in progress at once, once, and nothing when it is too short', () => {
    const detector = new ScanDetector();
    burst(detector, '6111000000011');
    expect(detector.flush()).toMatchObject({ code: '6111000000011', end: 'silence' });
    expect(detector.flush()).toBeNull();

    burst(detector, '123');
    expect(detector.flush()).toBeNull();
  });
});

describe('speedLabel', () => {
  it('names the speed in words', () => {
    expect(speedLabel(5)).toBe('très rapide');
    expect(speedLabel(15)).toBe('rapide');
    expect(speedLabel(35)).toBe('lente — risque de ne pas être reconnu');
  });
});
