import { Injectable, signal } from '@angular/core';
import { SILENCE_MS, ScanDetector, ScanResult } from '../core/scan';

const SOUND_KEY = 'soro.scanSound';

/** Une zone où l'on écrit (champ, liste, texte libre) : un scan qui y arrive n'est pas détourné. */
export function isEditable(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement ||
    target instanceof HTMLSelectElement ||
    (target instanceof HTMLElement && target.isContentEditable)
  );
}

export interface ScanListener {
  /** Dit si un scan qui arrive sur cet élément (ou sur la page, `body`) doit être pris. Une zone de saisie qu'on ne détourne pas est refusée ici. */
  accept: (target: EventTarget | null) => boolean;
  onScan: (scan: ScanResult, target: EventTarget | null) => void;
}

/**
 * Lecteur de codes-barres universel (ADR 0015) : écoute le clavier de la page, reconnaît les scans à leur vitesse (ScanDetector) et rend le
 * retour sonore. Aucun réglage du lecteur n'est exigé : Entrée, Tab, ou rien du tout, clavier décalé ou non.
 */
@Injectable({ providedIn: 'root' })
export class ScanService {
  readonly soundOn = signal(readSound());
  private audio: AudioContext | null = null;

  /** Commence à écouter ; rend la fonction qui arrête. À appeler depuis un composant et à arrêter à sa destruction. */
  listen(listener: ScanListener): () => void {
    const detector = new ScanDetector();
    let timer: ReturnType<typeof setTimeout> | undefined;
    let lastTarget: EventTarget | null = null;

    const onKeyDown = (event: KeyboardEvent): void => {
      if (!listener.accept(event.target)) {
        return;
      }
      lastTarget = event.target;
      clearTimeout(timer);
      const outcome = detector.push({
        key: event.key,
        code: event.code,
        // L'heure de la touche, pas celle où la page la traite : un téléphone lent peut traiter une rafale en retard sans en changer le rythme.
        at: event.timeStamp > 0 ? event.timeStamp : performance.now(),
        ctrl: event.ctrlKey,
        alt: event.altKey,
        meta: event.metaKey,
      });
      if (outcome.swallow) {
        event.preventDefault();
      }
      if (outcome.scan) {
        listener.onScan(outcome.scan, lastTarget);
        return;
      }
      timer = setTimeout(() => {
        const scan = detector.flush();
        if (scan) {
          listener.onScan(scan, lastTarget);
        }
      }, SILENCE_MS);
    };

    document.addEventListener('keydown', onKeyDown, true);
    return () => {
      clearTimeout(timer);
      document.removeEventListener('keydown', onKeyDown, true);
    };
  }

  setSound(on: boolean): void {
    this.soundOn.set(on);
    try {
      localStorage.setItem(SOUND_KEY, on ? 'on' : 'off');
    } catch {
      // Stockage indisponible : le choix vaut pour cette session seulement.
    }
  }

  /** Bip aigu et court pour un code connu, grave et plus long pour un code inconnu. Jamais bloquant : sans son, le message à l'écran suffit. */
  feedback(known: boolean): void {
    if (!this.soundOn()) {
      return;
    }
    try {
      const Context = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
      if (!Context) {
        return;
      }
      this.audio ??= new Context();
      const oscillator = this.audio.createOscillator();
      const gain = this.audio.createGain();
      oscillator.frequency.value = known ? 1400 : 300;
      gain.gain.value = 0.15;
      oscillator.connect(gain);
      gain.connect(this.audio.destination);
      oscillator.start();
      oscillator.stop(this.audio.currentTime + (known ? 0.08 : 0.3));
    } catch {
      // Navigateur sans audio, ou son pas encore autorisé : on reste silencieux.
    }
  }
}

function readSound(): boolean {
  try {
    return localStorage.getItem(SOUND_KEY) !== 'off';
  } catch {
    return true;
  }
}
