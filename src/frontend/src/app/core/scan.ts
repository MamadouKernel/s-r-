/** Reconnaissance d'un scan de lecteur de codes-barres en mode clavier (ADR 0015) : une rafale de touches très rapprochées, jamais une saisie humaine. */

/** Écart maximal entre deux caractères d'un même scan. Un humain tape au mieux un caractère toutes les 60 ms. */
export const MAX_GAP_MS = 40;
/** Silence qui termine un scan quand le lecteur n'envoie ni Entrée ni Tab. */
export const SILENCE_MS = 100;
/** Longueur minimale d'un code : en dessous, c'est de la saisie. */
export const MIN_LENGTH = 6;

export interface KeyInput {
  key: string;
  code: string;
  /** Instant de la touche, en millisecondes. */
  at: number;
  ctrl?: boolean;
  alt?: boolean;
  meta?: boolean;
}

export type ScanEnd = 'enter' | 'tab' | 'silence';

export interface ScanResult {
  code: string;
  end: ScanEnd;
  /** Vitesse moyenne, en millisecondes par caractère. */
  msPerChar: number;
  /** Au moins un chiffre a été relu d'après la touche physique parce que le caractère produit était autre. */
  corrected: boolean;
}

export interface PushOutcome {
  scan: ScanResult | null;
  /** Vrai quand la touche (Entrée, Tab) termine un scan : l'appelant l'empêche d'agir (envoyer le formulaire, sauter de champ). */
  swallow: boolean;
}

const NOTHING: PushOutcome = { scan: null, swallow: false };

/** Chiffre porté par la touche physique, quelle que soit la disposition du clavier ; `null` si ce n'est pas une touche de chiffre. */
function physicalDigit(code: string): string | null {
  const match = /^(?:Digit|Numpad)(\d)$/.exec(code);
  return match ? match[1] : null;
}

export class ScanDetector {
  private chars: string[] = [];
  private corrected = false;
  private firstAt = 0;
  private lastAt = 0;

  /** Donne une touche au détecteur. Les touches de contrôle inconnues (préfixe, suffixe) et les espaces n'interrompent rien. */
  push(input: KeyInput): PushOutcome {
    if (input.ctrl || input.alt || input.meta) {
      this.reset();
      return NOTHING;
    }
    if (input.key === 'Enter' || input.key === 'Tab') {
      return this.finish(input.key === 'Enter' ? 'enter' : 'tab', input.at);
    }
    if (input.key === 'Backspace' || input.key === 'Delete' || input.key === 'Escape' || input.key.startsWith('Arrow')) {
      this.reset();
      return NOTHING;
    }
    if (input.key.length !== 1 || input.key === ' ') {
      return NOTHING;
    }

    if (this.chars.length > 0 && input.at - this.lastAt > MAX_GAP_MS) {
      this.reset();
    }
    const digit = physicalDigit(input.code);
    if (this.chars.length === 0) {
      this.firstAt = input.at;
    }
    if (digit !== null && digit !== input.key) {
      this.corrected = true;
    }
    this.chars.push(digit ?? input.key);
    this.lastAt = input.at;
    return NOTHING;
  }

  /** Appelé après un silence : rend le scan en cours s'il est assez long et que le silence a duré. Rendu une seule fois. */
  poll(now: number): ScanResult | null {
    if (this.chars.length === 0 || now - this.lastAt < SILENCE_MS) {
      return null;
    }
    return this.take('silence');
  }

  /** Rend le scan en cours sans attendre : l'appelant a lui-même mesuré le silence (minuterie relancée à chaque touche). */
  flush(): ScanResult | null {
    return this.chars.length === 0 ? null : this.take('silence');
  }

  private finish(end: ScanEnd, at: number): PushOutcome {
    if (this.chars.length === 0 || at - this.lastAt > MAX_GAP_MS) {
      this.reset();
      return NOTHING;
    }
    const scan = this.take(end);
    return { scan, swallow: scan !== null };
  }

  private take(end: ScanEnd): ScanResult | null {
    const code = this.chars.join('');
    const result: ScanResult | null =
      code.length >= MIN_LENGTH
        ? {
            code,
            end,
            msPerChar: Math.round((this.lastAt - this.firstAt) / (code.length - 1)),
            corrected: this.corrected,
          }
        : null;
    this.reset();
    return result;
  }

  private reset(): void {
    this.chars = [];
    this.corrected = false;
    this.firstAt = 0;
    this.lastAt = 0;
  }
}

/** La vitesse du lecteur en mots simples, pour l'écran de test. */
export function speedLabel(msPerChar: number): string {
  if (msPerChar < 10) {
    return 'très rapide';
  }
  return msPerChar < 25 ? 'rapide' : 'lente — risque de ne pas être reconnu';
}
