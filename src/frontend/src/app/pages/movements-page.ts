import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CASH_OUT_APPROVAL_FCFA } from '../core/cash-count';
import { formatFcfa, parseFcfa } from '../core/money';
import { CashMovementKind, CashMovementOperation } from '../data/journal';
import { ApprovalDialog } from '../sales/approval-dialog';
import { ApprovalGranted } from '../sales/sales-api';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { NetworkService } from '../services/network.service';

const REASONS = ['Transport', 'Sachets et emballages', 'Eau ou électricité', 'Repas', 'Autre'];
const KIND_LABEL: Record<CashMovementKind, string> = { Expense: 'Dépense', Drop: 'Versement au coffre', Topup: 'Apport de monnaie' };
const STATUS_LABEL = { pending: 'En attente', synced: 'Envoyée', rejected: 'Refusée' } as const;

/**
 * Sorties et entrées d'espèces en cours de journée (docs/specs/cloture-sorties-rapport CLO-10) : une dépense, un versement au coffre ou un apport
 * de monnaie. Au-delà de 5 000 FCFA, un gérant valide avec son PIN (réseau obligatoire) ; en dessous, la saisie marche sans réseau.
 */
@Component({
  selector: 'app-movements-page',
  imports: [RouterLink, ApprovalDialog],
  template: `
    <section class="mx-auto max-w-md" aria-labelledby="mouvements-titre">
      <a class="btn mb-4" routerLink="/vente">← Retour à la caisse</a>
      <h2 id="mouvements-titre" class="mb-2 text-xl font-bold">Mouvements de caisse</h2>
      <p class="mb-4 text-base text-ardoise">Notez ici tout l’argent qui sort du tiroir ou qui y entre en dehors des ventes, pour que la clôture soit juste.</p>

      <form class="panel" (submit)="submit($event)" novalidate aria-label="Nouveau mouvement">
        <label class="field-label" for="type">Quoi ?</label>
        <select id="type" class="field" (change)="kind.set($any($event.target).value)">
          <option value="Expense" [selected]="kind() === 'Expense'">Dépense payée du tiroir</option>
          <option value="Drop" [selected]="kind() === 'Drop'">Versement au coffre (argent retiré du tiroir)</option>
          <option value="Topup" [selected]="kind() === 'Topup'">Apport de monnaie (argent ajouté au tiroir)</option>
        </select>

        <label class="field-label mt-3" for="montant">Montant (FCFA)</label>
        <input id="montant" class="field amount text-right" type="text" inputmode="numeric" autocomplete="off" [value]="amount()" (input)="amount.set($any($event.target).value)" />

        @if (kind() === 'Expense') {
          <label class="field-label mt-3" for="motif">À quoi sert-elle ?</label>
          <select id="motif" class="field" (change)="reason.set($any($event.target).value)">
            <option value="" [selected]="reason() === ''">Choisir…</option>
            @for (option of reasons; track option) {
              <option [value]="option" [selected]="reason() === option">{{ option }}</option>
            }
          </select>
          @if (reason() === 'Autre') {
            <label class="field-label mt-3" for="motif-libre">Précisez</label>
            <input id="motif-libre" class="field" type="text" maxlength="150" autocomplete="off" [value]="otherReason()" (input)="otherReason.set($any($event.target).value)" />
          }
        }

        <p class="field-error" role="alert">{{ error() }}</p>
        @if (asking()) {
          <div class="mt-3">
            <app-approval-dialog
              [saleId]="id()" purpose="cash-out" [amountFcfa]="pending()?.amountFcfa ?? 0"
              reason="Une sortie de plus de 5 000 FCFA demande l’accord d’un gérant."
              (granted)="approved($event)" (cancelled)="asking.set(false)"
            />
          </div>
        } @else {
          <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">Enregistrer</button>
        }
      </form>

      <h3 class="mb-2 mt-6 text-lg font-bold">Aujourd’hui, sur cette caisse</h3>
      @if (movements().length === 0) {
        <p class="panel text-ardoise">Aucun mouvement pour le moment.</p>
      } @else {
        <ul class="divide-y divide-sauge rounded border border-sauge bg-surface">
          @for (item of movements(); track item.seq) {
            <li class="px-4 py-3">
              <div class="flex flex-wrap items-baseline gap-x-3">
                <span class="font-semibold">{{ title(item) }}</span>
                <span class="ml-auto text-sm font-semibold" [class.text-avertissement]="item.status === 'pending'" [class.text-succes]="item.status === 'synced'" [class.text-erreur]="item.status === 'rejected'">{{ statusLabel(item) }}</span>
              </div>
              <p class="text-sm text-ardoise">{{ time(item.at) }}</p>
              @if (item.status === 'rejected' && item.message) {
                <p class="text-sm font-semibold text-erreur">{{ item.message }}</p>
              }
              @if (item.status !== 'rejected') {
                @if (undoing() === item.movementId) {
                  <label class="field-label mt-2" for="annulation-motif">Pourquoi annulez-vous ce mouvement ?</label>
                  <input id="annulation-motif" class="field" type="text" maxlength="100" autocomplete="off" [value]="undoReason()" (input)="undoReason.set($any($event.target).value)" />
                  <p class="field-error" role="alert">{{ undoError() }}</p>
                  <div class="mt-2 flex gap-2">
                    <button type="button" class="btn" (click)="undoing.set(null)">Garder</button>
                    <button type="button" class="btn-primary" (click)="undo(item)">Confirmer l’annulation</button>
                  </div>
                } @else {
                  <button type="button" class="btn mt-2" (click)="startUndo(item)">Annuler ce mouvement</button>
                }
              }
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class MovementsPage implements OnInit {
  private readonly cash = inject(CashService);
  private readonly auth = inject(AuthService);
  protected readonly network = inject(NetworkService);

  protected readonly reasons = REASONS;
  protected readonly kind = signal<CashMovementKind>('Expense');
  protected readonly amount = signal('');
  protected readonly reason = signal('');
  protected readonly otherReason = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly asking = signal(false);
  protected readonly pending = signal<{ kind: CashMovementKind; amountFcfa: number; reason: string | null } | null>(null);
  protected readonly movements = signal<CashMovementOperation[]>([]);
  protected readonly undoing = signal<string | null>(null);
  protected readonly undoReason = signal('');
  protected readonly undoError = signal('');
  protected readonly id = () => this.cash.movementId();

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  private async reload(): Promise<void> {
    this.movements.set((await this.cash.movements()).slice().reverse());
  }

  protected title(item: CashMovementOperation): string {
    return `${KIND_LABEL[item.movementKind]} · ${formatFcfa(item.amountFcfa)}${item.reason ? ` · ${item.reason}` : ''}`;
  }

  protected statusLabel(item: CashMovementOperation): string {
    return item.status === 'rejected' ? 'Refusée' : STATUS_LABEL[item.status];
  }

  protected time(iso: string): string {
    return new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    const amount = parseFcfa(this.amount());
    if (amount === null || amount <= 0) {
      this.error.set('Saisissez le montant en francs, en chiffres entiers.');
      return;
    }
    const reason = this.kind() === 'Expense' ? (this.reason() === 'Autre' ? this.otherReason().trim() : this.reason()) : null;
    if (this.kind() === 'Expense' && !reason) {
      this.error.set('Dites à quoi sert cette dépense.');
      return;
    }
    this.error.set('');
    const movement = { kind: this.kind(), amountFcfa: amount, reason };
    const needsManager = this.kind() !== 'Topup' && amount > CASH_OUT_APPROVAL_FCFA && !['Owner', 'Manager'].includes(this.auth.user()?.role ?? '');
    if (needsManager) {
      if (!this.network.online()) {
        this.error.set('Il faut du réseau pour qu’un gérant valide une sortie de plus de 5 000 FCFA : attendez le retour du réseau, ou faites deux sorties plus petites avec son accord.');
        return;
      }
      this.pending.set(movement);
      this.asking.set(true);
      return;
    }
    await this.record(movement);
  }

  protected async approved(granted: ApprovalGranted): Promise<void> {
    const movement = this.pending();
    this.asking.set(false);
    if (movement) {
      await this.record({ ...movement, approvalToken: granted.token });
    }
  }

  private async record(input: { kind: CashMovementKind; amountFcfa: number; reason: string | null; approvalToken?: string }): Promise<void> {
    this.busy.set(true);
    try {
      await this.cash.recordMovement(input);
      this.amount.set('');
      this.reason.set('');
      this.otherReason.set('');
      this.pending.set(null);
      await this.reload();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Le mouvement n’a pas pu être enregistré. Réessayez.');
    } finally {
      this.busy.set(false);
    }
  }

  protected startUndo(item: CashMovementOperation): void {
    this.undoReason.set('');
    this.undoError.set('');
    this.undoing.set(item.movementId);
  }

  /** On n'efface jamais un mouvement : on en passe un en sens inverse, avec son motif (un versement annulé est un apport, et l'inverse). */
  protected async undo(item: CashMovementOperation): Promise<void> {
    const why = this.undoReason().trim();
    if (!why) {
      this.undoError.set('Dites pourquoi vous annulez ce mouvement.');
      return;
    }
    const label = item.reason ?? KIND_LABEL[item.movementKind];
    try {
      await this.cash.recordMovement({
        kind: item.movementKind === 'Topup' ? 'Drop' : 'Topup',
        amountFcfa: item.amountFcfa,
        reason: `Annulation de « ${label} » : ${why}`,
      });
      this.undoing.set(null);
      await this.reload();
    } catch (error) {
      this.undoError.set(error instanceof Error ? error.message : 'L’annulation n’a pas pu être enregistrée.');
    }
  }
}
