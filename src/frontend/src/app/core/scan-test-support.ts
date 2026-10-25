/**
 * Aide des tests : un scan « tapé » avec des heures de touche maîtrisées. Les événements fabriqués portent l'heure de leur création, qu'une
 * pause du ramasse-miettes peut étirer au-delà du seuil d'un scan ; on fixe donc l'heure de chaque touche (5 ms d'écart, 500 ms entre deux scans).
 */
let clock = 10_000;

export interface ScanOptions {
  ending?: 'Enter' | 'Tab' | null;
  /** Le caractère que le clavier écrit à la place du chiffre (disposition mal réglée) ; par défaut, le chiffre lui-même. */
  produced?: (character: string) => string;
  /** Appelé après chaque touche de caractère : de quoi écrire dans une zone de saisie comme le ferait le navigateur. */
  onKey?: (key: string) => void;
}

function press(target: EventTarget, key: string, code: string): KeyboardEvent {
  clock += 5;
  const event = new KeyboardEvent('keydown', { key, code, bubbles: true, cancelable: true });
  Object.defineProperty(event, 'timeStamp', { value: clock });
  target.dispatchEvent(event);
  return event;
}

/** Envoie les touches d'un code ; rend la touche de fin (Entrée, Tab) ou `null` s'il n'y en a pas. */
export function typeScan(target: EventTarget, code: string, options: ScanOptions = {}): KeyboardEvent | null {
  clock += 500;
  for (const character of code) {
    const key = /\d/.test(character) ? (options.produced ? options.produced(character) : character) : character;
    press(target, key, /\d/.test(character) ? `Digit${character}` : 'KeyX');
    options.onKey?.(key);
  }
  const ending = options.ending === undefined ? 'Enter' : options.ending;
  return ending ? press(target, ending, ending) : null;
}
