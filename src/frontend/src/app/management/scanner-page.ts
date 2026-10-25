import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { ScanResult, speedLabel } from '../core/scan';
import { isEditable, ScanService } from '../services/scan.service';

/** Silence après lequel l'écran suppose que le lecteur n'envoie rien. */
const NOTHING_AFTER_MS = 15_000;
/** Deux lectures du même code plus rapprochées que cela : le lecteur relit en continu. */
const REPEAT_WITHIN_MS = 1_500;
const HISTORY_SIZE = 5;

interface Reading {
  code: string;
  length: string;
  ending: string;
  speed: string;
  corrected: boolean;
}

/**
 * Test du lecteur de codes-barres (ADR 0015, SCN-07) : l'utilisateur scanne un code et l'écran dit, en français, ce qui est arrivé et ce qu'il
 * faut éventuellement corriger. C'est ce qui permet de se passer de la notice du lecteur.
 */
@Component({
  selector: 'app-scanner-page',
  template: `
    <section aria-labelledby="lecteur-titre">
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <h2 id="lecteur-titre" class="text-xl font-bold">Lecteur de codes-barres</h2>
        <button type="button" class="btn ml-auto" (click)="toggleSound()">
          Son du lecteur : {{ scanner.soundOn() ? 'activé' : 'désactivé' }}
        </button>
      </div>

      <div class="panel mb-4">
        <p class="mb-2">
          Branchez votre lecteur (câble USB, Bluetooth ou petit récepteur sans fil), puis scannez n’importe quel code-barres.
          <strong>Aucun réglage n’est à faire dans l’application</strong> : elle s’adapte à votre lecteur.
        </p>
        <form (submit)="typed($event)" novalidate>
          <label class="field-label" for="zone-scan">Scannez un code ici</label>
          <input id="zone-scan" class="field font-mono" type="text" autocomplete="off" data-scan="test" [value]="zone()" (input)="zone.set($any($event.target).value)" />
        </form>
      </div>

      <div role="status" aria-live="polite">
        @if (last(); as reading) {
          <div class="panel mb-4">
            <h3 class="mb-2 text-lg font-bold">Dernière lecture</h3>
            <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
              <dt class="text-ardoise">Code lu</dt>
              <dd class="break-all font-mono font-semibold">{{ reading.code }}</dd>
              <dt class="text-ardoise">Longueur</dt>
              <dd>{{ reading.length }}</dd>
              <dt class="text-ardoise">Fin de lecture</dt>
              <dd>{{ reading.ending }}</dd>
              <dt class="text-ardoise">Vitesse</dt>
              <dd>{{ reading.speed }}</dd>
            </dl>
            <p class="mt-3 font-semibold" [class.text-succes]="!reading.corrected" [class.text-avertissement]="reading.corrected">
              {{ reading.corrected ? 'Les chiffres sont décalés (le clavier du lecteur ne correspond pas à celui de l’appareil) : l’application les corrige automatiquement.' : 'Votre lecteur est bien reconnu.' }}
            </p>
            @if (repeated()) {
              <p class="mt-2 font-semibold text-avertissement">
                Plusieurs lectures d’affilée pour un seul scan : réglez le lecteur en lecture manuelle (un appui = une lecture), sinon un même article risque d’être ajouté plusieurs fois.
              </p>
            }
          </div>
        }
        @if (slow()) {
          <p class="panel mb-4 font-semibold text-avertissement">
            Ce code est arrivé trop lentement pour être reconnu comme un scan. Il a été tapé comme au clavier : vérifiez le lecteur ou changez-le de mode.
          </p>
        }
        @if (silent() && !last()) {
          <p class="panel mb-4 font-semibold text-avertissement">
            Rien n’arrive : le lecteur n’est peut-être pas en mode clavier. Consultez sa notice : cherchez « mode clavier », « USB HID » ou « keyboard ».
          </p>
        }
      </div>

      @if (history().length > 0) {
        <div class="panel">
          <h3 class="mb-2 text-lg font-bold">Lectures précédentes</h3>
          <ul data-testid="historique" class="divide-y divide-sauge">
            @for (reading of history(); track $index) {
              <li class="py-2">
                <span class="break-all font-mono">{{ reading.code }}</span>
                <span class="block text-sm text-ardoise">{{ reading.length }} · {{ reading.ending }} · {{ reading.speed }}</span>
              </li>
            }
          </ul>
        </div>
      }
    </section>
  `,
})
export class ScannerPage implements OnInit {
  protected readonly scanner = inject(ScanService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly zone = signal('');
  protected readonly last = signal<Reading | null>(null);
  protected readonly history = signal<Reading[]>([]);
  protected readonly repeated = signal(false);
  protected readonly slow = signal(false);
  protected readonly silent = signal(false);

  private lastCode = '';
  private lastAt = 0;

  constructor() {
    const stop = this.scanner.listen({
      accept: (target) => !isEditable(target) || (target as HTMLElement).dataset['scan'] === 'test',
      onScan: (scan) => this.read(scan),
    });
    this.destroyRef.onDestroy(stop);
  }

  ngOnInit(): void {
    const timer = setTimeout(() => this.silent.set(true), NOTHING_AFTER_MS);
    this.destroyRef.onDestroy(() => clearTimeout(timer));
  }

  protected toggleSound(): void {
    this.scanner.setSound(!this.scanner.soundOn());
    this.scanner.feedback(true);
  }

  /** Entrée dans la zone sans qu'un scan ait été reconnu : le code a été tapé à la main ou arrivé trop lentement. */
  protected typed(event: Event): void {
    event.preventDefault();
    if (this.zone().trim().length >= 6) {
      this.slow.set(true);
    }
    this.zone.set('');
  }

  private read(scan: ScanResult): void {
    const now = performance.now();
    const reading: Reading = {
      code: scan.code,
      length: `${scan.code.length} caractères`,
      ending: scan.end === 'enter' ? 'Entrée' : scan.end === 'tab' ? 'Tab' : 'aucune (reconnu par la vitesse)',
      speed: speedLabel(scan.msPerChar),
      corrected: scan.corrected,
    };
    this.repeated.set(scan.code === this.lastCode && now - this.lastAt < REPEAT_WITHIN_MS);
    this.lastCode = scan.code;
    this.lastAt = now;
    this.slow.set(false);
    this.silent.set(false);
    this.zone.set('');
    const previous = this.last();
    if (previous) {
      this.history.update((list) => [previous, ...list].slice(0, HISTORY_SIZE));
    }
    this.last.set(reading);
    this.scanner.feedback(true);
  }
}
