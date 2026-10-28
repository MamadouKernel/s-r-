import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DENOMINATIONS, needsRecount, sumDenominations } from '../core/cash-count';
import { formatFcfa, parseFcfa } from '../core/money';
import { describeVariance } from '../core/variance';
import { SessionSummary } from '../data/journal';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { SyncService } from '../services/sync.service';

const RECOUNT_MESSAGE = 'Votre comptage ne correspond pas à ce qui est attendu. Recomptez, billet par billet, puis confirmez.';

/**
 * S7 : clôture à l'aveugle (docs/decisions/0021, REQ-08). Le caissier compte sans voir ce qui est attendu ; un comptage trop éloigné est refait une
 * seule fois, sans dire de combien ni dans quel sens. Seuls le gérant et le propriétaire voient l'attendu et l'écart, une fois la caisse close.
 */
@Component({
  selector: 'app-close-page',
  template: `
    @if (closedCount() !== null) {
      <section class="mx-auto max-w-md panel" role="status">
        <h2 class="text-xl font-bold text-succes">Caisse clôturée</h2>
        <p class="mt-2 text-base">Clôture enregistrée sur l’appareil{{ sync.counts().pending > 0 ? ', en attente de synchronisation.' : '.' }}</p>
        <p class="amount mt-4 text-lg font-bold">Montant compté : {{ format(closedCount()!) }}</p>
        @if (seesExpected()) {
          <p class="amount mt-1 text-base">Montant attendu : {{ format(expected()) }}</p>
          <p class="amount text-base font-bold" [class.text-erreur]="variance() !== 0">Écart : {{ varianceLabel() }}</p>
        }
        <button type="button" class="btn-primary mt-6 w-full" (click)="finish()">Terminer</button>
      </section>
    } @else {
      <section class="mx-auto max-w-md panel">
        <h2 class="mb-2 text-xl font-bold">Clôturer la caisse</h2>
        <p class="text-base text-ardoise">Comptez les espèces du tiroir, billets et pièces, puis tapez le total.</p>
        @if (cash.held().length > 0) {
          <p class="mt-4 rounded border border-avertissement p-3 text-base font-semibold text-avertissement" role="note">
            {{ cash.held().length }} vente{{ cash.held().length > 1 ? 's' : '' }} en attente ne {{ cash.held().length > 1 ? 'seront' : 'sera' }} pas conservée{{ cash.held().length > 1 ? 's' : '' }} :
            elle{{ cash.held().length > 1 ? 's seront supprimées' : ' sera supprimée' }} à la clôture. Reprenez-la{{ cash.held().length > 1 ? 's' : '' }} et encaissez-la{{ cash.held().length > 1 ? 's' : '' }} si les clients sont toujours là.
          </p>
        }
        @if (recounting()) {
          <p class="mt-4 rounded border border-avertissement bg-ivoire p-3 text-base font-semibold text-avertissement" role="alert">{{ recountMessage }}</p>
        }
        <form (submit)="submit($event)" class="mt-4" novalidate>
          <label class="field-label" for="compte">{{ recounting() ? 'Espèces recomptées (FCFA)' : 'Espèces comptées (FCFA)' }}</label>
          <input
            id="compte"
            class="field amount text-right text-2xl"
            type="text"
            inputmode="numeric"
            autocomplete="off"
            [value]="counted()"
            (input)="setTotal($any($event.target).value)"
            [attr.aria-invalid]="error() ? 'true' : null"
            aria-describedby="compte-erreur"
          />
          <p id="compte-erreur" class="field-error" role="alert">{{ error() }}</p>

          @if (!helping()) {
            <button type="button" class="btn mt-2" (click)="helping.set(true)">Compter par billets</button>
          } @else {
            <fieldset class="mt-3 rounded border border-sauge p-3">
              <legend class="field-label px-1">Billets et pièces</legend>
              @for (item of denominations; track item.value) {
                <div class="mb-2 grid grid-cols-[1fr_6rem] items-center gap-2">
                  <label class="text-base" [for]="'coupure-' + item.value">{{ item.label }}</label>
                  <input
                    [id]="'coupure-' + item.value"
                    class="field amount text-right"
                    type="text"
                    inputmode="numeric"
                    autocomplete="off"
                    [value]="counts()[item.value] ?? ''"
                    (input)="setCount(item.value, $any($event.target).value)"
                  />
                </div>
              }
              <p class="amount mt-2 text-lg font-bold">Total : {{ format(detailTotal()) }}</p>
            </fieldset>
          }
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">{{ recounting() ? 'Confirmer le recomptage' : 'Clôturer la caisse' }}</button>
        </form>
      </section>
    }
  `,
})
export class ClosePage implements OnInit {
  protected readonly cash = inject(CashService);
  protected readonly sync = inject(SyncService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly denominations = DENOMINATIONS;
  protected readonly recountMessage = RECOUNT_MESSAGE;
  private readonly summary = signal<SessionSummary | null>(null);
  protected readonly counted = signal('');
  protected readonly counts = signal<Record<number, string>>({});
  protected readonly helping = signal(false);
  protected readonly recounting = signal(false);
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly closedCount = signal<number | null>(null);

  /** Le gérant et le propriétaire voient l'attendu et l'écart une fois la caisse close ; un caissier jamais. */
  protected readonly seesExpected = computed(() => ['Owner', 'Manager'].includes(this.auth.user()?.role ?? ''));
  protected readonly expected = computed(() => this.summary()?.expectedCashFcfa ?? 0);
  protected readonly variance = computed(() => (this.closedCount() ?? 0) - this.expected());
  protected readonly varianceLabel = computed(() => describeVariance(this.variance()));
  protected readonly detailTotal = computed(() =>
    sumDenominations(Object.fromEntries(Object.entries(this.counts()).map(([value, text]) => [Number(value), Number.parseInt(text, 10) || 0]))),
  );
  protected format = formatFcfa;

  async ngOnInit(): Promise<void> {
    this.summary.set(await this.cash.summary());
  }

  protected setTotal(text: string): void {
    this.counted.set(text);
  }

  /** Taper les billets remplit le total ; le total reste modifiable, mais le détail n'est envoyé que s'il l'égale. */
  protected setCount(value: number, text: string): void {
    this.counts.update((counts) => ({ ...counts, [value]: text }));
    this.counted.set(String(this.detailTotal()));
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    const value = parseFcfa(this.counted());
    if (value === null) {
      this.error.set('Saisissez le montant compté en chiffres, sans virgule.');
      return;
    }
    this.error.set('');
    if (!this.recounting() && needsRecount(value, this.expected())) {
      // Le recomptage se décide ici, sur l'appareil, avec ce qu'il sait : il marche donc sans réseau, au moment où l'argent est encore dans le tiroir.
      this.recounting.set(true);
      this.counted.set('');
      this.counts.set({});
      return;
    }
    this.busy.set(true);
    try {
      await this.cash.closeSession(value, { recount: this.recounting(), denominations: this.detail(value) });
      this.closedCount.set(value);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'La clôture a échoué. Réessayez.');
    } finally {
      this.busy.set(false);
    }
  }

  private detail(counted: number): Record<number, number> | undefined {
    const entries = Object.entries(this.counts())
      .map(([value, text]) => [Number(value), Number.parseInt(text, 10) || 0] as const)
      .filter(([, count]) => count > 0);
    const detail = Object.fromEntries(entries);
    return entries.length > 0 && sumDenominations(detail) === counted ? detail : undefined;
  }

  protected async finish(): Promise<void> {
    await this.router.navigateByUrl('/ouverture');
  }
}
