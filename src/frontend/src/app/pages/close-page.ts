import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { formatFcfa, parseFcfa } from '../core/money';
import { describeVariance } from '../core/variance';
import { CloseSessionOperation, SessionSummary } from '../data/journal';
import { CashService } from '../services/cash.service';
import { SyncService } from '../services/sync.service';

/** S7 : le caissier compte la caisse, l'écart est calculé et expliqué (REQ-08). */
@Component({
  selector: 'app-close-page',
  template: `
    @if (closed(); as closing) {
      <section class="mx-auto max-w-md panel" role="status">
        <h2 class="text-xl font-bold text-succes">Caisse clôturée</h2>
        <p class="mt-2 text-base">Clôture enregistrée sur l’appareil{{ sync.counts().pending > 0 ? ', en attente de synchronisation.' : '.' }}</p>
        <dl class="mt-4 space-y-1">
          <div class="flex justify-between"><dt>Montant attendu</dt><dd class="amount font-bold">{{ format(expected()) }}</dd></div>
          <div class="flex justify-between"><dt>Montant compté</dt><dd class="amount font-bold">{{ format(closing.countedCashFcfa) }}</dd></div>
          <div class="flex justify-between"><dt>Écart</dt><dd class="amount font-bold" [class.text-erreur]="variance() !== 0">{{ varianceLabel() }}</dd></div>
        </dl>
        <button type="button" class="btn-primary mt-6 w-full" (click)="finish()">Terminer</button>
      </section>
    } @else {
      <section class="mx-auto max-w-md panel">
        <h2 class="mb-4 text-xl font-bold">Clôturer la caisse</h2>
        @if (summary(); as data) {
          <dl class="space-y-1">
            <div class="flex justify-between"><dt>Ventes de la session</dt><dd class="amount font-bold">{{ data.salesCount }}</dd></div>
            <div class="flex justify-between"><dt>Total des ventes</dt><dd class="amount font-bold">{{ format(data.netTotalFcfa) }}</dd></div>
            <div class="flex justify-between"><dt>Espèces attendues en caisse</dt><dd class="amount font-bold">{{ format(data.expectedCashFcfa) }}</dd></div>
          </dl>
        }
        @if (cash.held().length > 0) {
          <p class="mt-4 rounded border border-avertissement p-3 text-base font-semibold text-avertissement" role="note">
            {{ cash.held().length }} vente{{ cash.held().length > 1 ? 's' : '' }} en attente ne {{ cash.held().length > 1 ? 'seront' : 'sera' }} pas conservée{{ cash.held().length > 1 ? 's' : '' }} :
            elle{{ cash.held().length > 1 ? 's seront supprimées' : ' sera supprimée' }} à la clôture. Reprenez-la{{ cash.held().length > 1 ? 's' : '' }} et encaissez-la{{ cash.held().length > 1 ? 's' : '' }} si les clients sont toujours là.
          </p>
        }
        <form (submit)="submit($event)" class="mt-4" novalidate>
          <label class="field-label" for="compte">Espèces comptées (FCFA)</label>
          <input
            id="compte"
            class="field amount text-right text-2xl"
            type="text"
            inputmode="numeric"
            autocomplete="off"
            [value]="counted()"
            (input)="counted.set($any($event.target).value)"
            [attr.aria-invalid]="error() ? 'true' : null"
            aria-describedby="compte-ecart compte-erreur"
          />
          <p id="compte-ecart" class="mt-2 text-base font-semibold" aria-live="polite" [class.text-erreur]="variance() !== 0">{{ preview() }}</p>
          <p id="compte-erreur" class="field-error" role="alert">{{ error() }}</p>
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">Clôturer la caisse</button>
        </form>
      </section>
    }
  `,
})
export class ClosePage implements OnInit {
  protected readonly cash = inject(CashService);
  protected readonly sync = inject(SyncService);
  private readonly router = inject(Router);

  protected readonly summary = signal<SessionSummary | null>(null);
  protected readonly counted = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly closed = signal<CloseSessionOperation | null>(null);
  protected readonly expected = computed(() => this.summary()?.expectedCashFcfa ?? 0);
  protected format = formatFcfa;

  protected readonly variance = computed(() => {
    const value = parseFcfa(this.counted());
    return value === null ? 0 : value - this.expected();
  });

  protected readonly varianceLabel = computed(() => describeVariance(this.variance()));

  protected readonly preview = computed(() =>
    parseFcfa(this.counted()) === null ? 'Saisissez le montant compté pour voir l’écart.' : describeVariance(this.variance()),
  );

  async ngOnInit(): Promise<void> {
    this.summary.set(await this.cash.summary());
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    const value = parseFcfa(this.counted());
    if (value === null) {
      this.error.set('Saisissez le montant compté en chiffres, sans virgule.');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      this.closed.set(await this.cash.closeSession(value));
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'La clôture a échoué. Réessayez.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async finish(): Promise<void> {
    await this.router.navigateByUrl('/ouverture');
  }
}
