import { Component, inject, input, output, signal } from '@angular/core';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { ApprovalGranted, SalesApi } from './sales-api';

/**
 * Un gérant tape son identifiant et son code PIN sur la caisse du caissier (docs/decisions/0020). La caisse reçoit en échange une approbation
 * signée, liée à cette vente : le code n'est ni gardé, ni envoyé une seconde fois. Il faut du réseau, le serveur seul décide.
 */
@Component({
  selector: 'app-approval-dialog',
  template: `
    <form class="rounded border border-avertissement bg-ivoire p-3" (submit)="submit($event)" novalidate aria-labelledby="validation-titre">
      <h3 id="validation-titre" class="text-lg font-bold">Validation du gérant</h3>
      <p class="mt-1 text-base">{{ reason() }}</p>
      @if (!network.online()) {
        <p class="mt-2 font-semibold text-avertissement" role="status">Il faut du réseau pour qu’un gérant valide : attendez le retour du réseau.</p>
      }
      <label class="field-label mt-3" for="validation-identifiant">Identifiant du gérant</label>
      <input id="validation-identifiant" class="field" type="text" autocomplete="off" autocapitalize="off" spellcheck="false" [value]="identifier()" (input)="identifier.set($any($event.target).value)" />
      <label class="field-label mt-3" for="validation-pin">Code PIN du gérant</label>
      <input id="validation-pin" class="field amount" type="password" inputmode="numeric" autocomplete="off" maxlength="12" [value]="pin()" (input)="pin.set($any($event.target).value)" />
      <p class="field-error" role="alert">{{ error() }}</p>
      <div class="mt-3 grid grid-cols-2 gap-3">
        <button type="button" class="btn" (click)="cancelled.emit()">Annuler</button>
        <button type="submit" class="btn-primary" [disabled]="busy() || !network.online()">{{ busy() ? 'Vérification…' : 'Valider' }}</button>
      </div>
    </form>
  `,
})
export class ApprovalDialog {
  protected readonly network = inject(NetworkService);
  private readonly api = inject(SalesApi);

  /** La vente concernée : la validation ne vaut que pour elle. */
  readonly saleId = input.required<string>();
  readonly purpose = input.required<'discount' | 'reversal'>();
  /** Pour une remise : le montant que le gérant voit et accepte. */
  readonly amountFcfa = input(0);
  /** Ce que le gérant est invité à valider, en toutes lettres. */
  readonly reason = input('Un gérant doit valider : son identifiant et son code PIN sont nécessaires.');

  readonly granted = output<ApprovalGranted>();
  readonly cancelled = output<void>();

  protected readonly identifier = signal('');
  protected readonly pin = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    if (!this.identifier().trim() || !this.pin()) {
      this.error.set('Tapez l’identifiant et le code PIN du gérant.');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      this.granted.emit(
        await this.api.grant({ identifier: this.identifier().trim(), pin: this.pin(), saleId: this.saleId(), purpose: this.purpose(), amountFcfa: this.amountFcfa() }),
      );
    } catch (error) {
      this.error.set(wording(error));
    } finally {
      this.pin.set('');
      this.busy.set(false);
    }
  }
}

function wording(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) {
      return 'Identifiant ou code PIN incorrect.';
    }
    if (error.status === 429) {
      return 'Trop de tentatives. Patientez une minute avant de réessayer.';
    }
    if (error.status === 0) {
      return 'Le serveur ne répond pas. Vérifiez le réseau puis réessayez.';
    }
  }
  return 'La validation a échoué. Réessayez.';
}
