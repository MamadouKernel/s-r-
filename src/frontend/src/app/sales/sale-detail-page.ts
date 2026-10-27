import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { formatQuantity, lineTotal, MeasureUnit, parseQuantity } from '../core/measure';
import { formatFcfa } from '../core/money';
import { buildReversalReceipt, methodLabel, Receipt } from '../core/receipt';
import { estimateRefund, RefundEstimate } from '../core/refund';
import { PaymentMethod } from '../domain/types';
import { TicketActions } from '../pages/ticket-actions';
import { AuthService } from '../services/auth.service';
import { CashService } from '../services/cash.service';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { ApprovalDialog } from './approval-dialog';
import { ApprovalGranted, ReversalDone, SaleDetail, SalesApi } from './sales-api';

const REASONS = ['Erreur de saisie', 'Le client renonce', 'Client mécontent', 'Article défectueux', 'Autre'];

interface LineState {
  checked: boolean;
  quantity: string;
  /** `false` : la marchandise est abîmée, elle part en perte au lieu de retourner en rayon. */
  resellable: boolean;
}

/**
 * Une vente en détail, et ce qu'on peut en faire : reprendre des articles, ou l'annuler en entier (docs/specs/annulations-remises-retours
 * ARR-09, ARR-10). Le serveur calcule et fait foi ; l'estimation affichée avant l'envoi n'est qu'un aperçu. En ligne seulement.
 */
@Component({
  selector: 'app-sale-detail-page',
  imports: [RouterLink, ApprovalDialog, TicketActions],
  template: `
    <section class="mx-auto max-w-2xl" aria-labelledby="vente-titre">
      <a class="btn mb-4" routerLink="/ventes">← Retour aux ventes</a>

      @if (!network.online()) {
        <p class="panel font-semibold text-avertissement" role="status">Cet écran a besoin du réseau : les annulations et les retours attendent le retour du réseau.</p>
      } @else if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement de la vente…</p>
      } @else if (loadError()) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ loadError() }}</p>
        </div>
      } @else if (sale(); as current) {
        <h2 id="vente-titre" class="text-xl font-bold">Ticket n° {{ current.ticket }}</h2>
        <p class="text-base text-ardoise">{{ when(current.recordedAt) }}</p>
        <p class="mt-1 font-semibold" [class.text-erreur]="current.status === 'Cancelled'">{{ current.status === 'Cancelled' ? 'Annulée' : 'Validée' }}</p>
        @if (current.cancellation; as cancellation) {
          <p class="text-base">Motif : {{ cancellation.reason }}</p>
        }

        <ul class="panel mt-3 flex flex-col gap-2">
          @for (line of current.lines; track $index) {
            <li class="flex justify-between gap-3">
              <span class="min-w-0">{{ line.productName }}<span class="block text-sm text-ardoise">{{ quantity(line.quantity, line.unit) }} × {{ format(line.unitPriceFcfa) }}</span></span>
              <span class="amount font-semibold">{{ format(line.totalFcfa) }}</span>
            </li>
          }
          @if (current.discountFcfa > 0) {
            <li class="amount flex justify-between text-ardoise"><span>Remise : -{{ format(current.discountFcfa) }}</span></li>
          }
          <li class="amount flex justify-between border-t border-sauge pt-2 text-lg font-bold"><span>Total</span><span>{{ format(current.netTotalFcfa) }}</span></li>
          @for (payment of current.payments; track $index) {
            <li class="amount flex justify-between text-sm"><span>{{ method(payment.method) }} : {{ format(payment.amountFcfa) }}</span></li>
          }
        </ul>

        @for (done of current.returns ?? []; track done.id) {
          <p class="mt-2 rounded border border-sauge bg-ivoire p-2 text-sm">
            {{ done.kind === 'Cancellation' ? 'Annulation' : 'Retour' }} du {{ when(done.at) }} : Rendu : {{ format(done.totalFcfa) }} · {{ done.reason }}
          </p>
        }

        @if (step() === 'none' && current.status === 'Validated') {
          <div class="mt-4 grid grid-cols-2 gap-3">
            <button type="button" class="btn" (click)="startReturn()">Retour d’articles</button>
            <button type="button" class="btn" (click)="startCancel()" [disabled]="hasReturns()">Annuler la vente</button>
          </div>
          @if (hasReturns()) {
            <p class="field-help">Cette vente a déjà un retour : faites un retour pour le reste.</p>
          }
        }

        @if (step() === 'form') {
          <form class="panel mt-4" (submit)="proceed($event)" novalidate [attr.aria-label]="mode() === 'return' ? 'Retour d’articles' : 'Annulation de la vente'">
            <h3 class="text-lg font-bold">{{ mode() === 'return' ? 'Retour d’articles' : 'Annuler la vente' }}</h3>
            @if (mode() === 'return') {
              @for (line of current.lines; track $index; let index = $index) {
                @if (remaining(index) > 0) {
                  <fieldset class="mt-3 border-t border-sauge pt-3">
                    <label class="flex items-center gap-2 text-base font-semibold">
                      <input [id]="'retour-ligne-' + index" type="checkbox" class="h-6 w-6" [checked]="states()[index].checked" (change)="patch(index, { checked: $any($event.target).checked })" />
                      {{ line.productName }} <span class="font-normal text-ardoise">(reste {{ quantity(remaining(index), line.unit) }})</span>
                    </label>
                    @if (states()[index].checked) {
                      <label class="field-label mt-2" [for]="'retour-quantite-' + index">Quantité rendue{{ line.unit && line.unit !== 'Piece' ? ' (en ' + (line.unit === 'Kilogram' ? 'kg' : 'litres') + ')' : '' }}</label>
                      <input [id]="'retour-quantite-' + index" class="field amount text-right" type="text" [attr.inputmode]="line.unit && line.unit !== 'Piece' ? 'decimal' : 'numeric'" autocomplete="off" [value]="states()[index].quantity" (input)="patch(index, { quantity: $any($event.target).value })" />
                      <label class="field-label mt-2" [for]="'retour-destination-' + index">La marchandise</label>
                      <select [id]="'retour-destination-' + index" class="field" (change)="patch(index, { resellable: $any($event.target).value === 'shelf' })">
                        <option value="shelf" [selected]="states()[index].resellable">Retourne en rayon</option>
                        <option value="loss" [selected]="!states()[index].resellable">Est abîmée (perte)</option>
                      </select>
                    }
                  </fieldset>
                }
              }
            } @else {
              <label class="mt-3 flex items-center gap-2 text-base">
                <input id="marchandise-abimee" type="checkbox" class="h-6 w-6" [checked]="damaged()" (change)="damaged.set($any($event.target).checked)" />
                La marchandise est abîmée (elle ne retourne pas en rayon)
              </label>
            }

            <label class="field-label mt-4" for="motif">Motif</label>
            <select id="motif" class="field" (change)="reason.set($any($event.target).value)">
              <option value="" [selected]="reason() === ''">Choisir…</option>
              @for (option of reasons; track option) {
                <option [value]="option" [selected]="reason() === option">{{ option }}</option>
              }
            </select>
            @if (reason() === 'Autre') {
              <label class="field-label mt-2" for="motif-libre">Quel motif ?</label>
              <input id="motif-libre" class="field" type="text" maxlength="200" autocomplete="off" [value]="otherReason()" (input)="otherReason.set($any($event.target).value)" />
            }

            <p class="field-error" role="alert">{{ error() }}</p>
            <div class="mt-3 grid grid-cols-2 gap-3">
              <button type="button" class="btn" (click)="reset()">Annuler</button>
              <button type="submit" class="btn-primary">Continuer</button>
            </div>
          </form>
        }

        @if (step() === 'confirm') {
          <section class="panel mt-4" [attr.aria-label]="'Récapitulatif'">
            <h3 class="text-lg font-bold">Récapitulatif</h3>
            <ul class="mt-2 list-disc pl-5 text-base">
              @for (item of requestedLines(); track item.lineIndex) {
                <li>{{ current.lines[item.lineIndex].productName }} : {{ quantity(item.quantity, current.lines[item.lineIndex].unit) }}{{ item.resellable ? '' : ' (abîmé : perte)' }}</li>
              }
            </ul>
            <p class="mt-2 text-base">Motif : {{ reasonText() }}</p>
            @for (part of estimate().parts; track part.method) {
              <p class="amount mt-2 text-lg font-bold">{{ describe(part, true) }}</p>
            }
            @if (estimate().parts.length === 0) {
              <p class="mt-2 text-base">Rien à rembourser.</p>
            }
            <p class="field-help">Le montant exact est calculé par le serveur au moment de l’enregistrement.</p>
            @if (approval(); as granted) {
              <p class="mt-2 font-semibold text-succes" role="status">Validé par {{ granted.approverName }}.</p>
            }
            @if (error()) {
              <p class="field-error mt-2" role="alert">{{ error() }}</p>
            }
            @if (needsManager() && !approval()) {
              <div class="mt-3">
                <app-approval-dialog
                  [saleId]="current.id" purpose="reversal"
                  [reason]="mode() === 'return' ? 'Un retour demande l’accord d’un gérant.' : 'Une annulation demande l’accord d’un gérant.'"
                  (granted)="approved($event)" (cancelled)="step.set('form')"
                />
              </div>
            } @else {
              <div class="mt-3 grid grid-cols-2 gap-3">
                <button type="button" class="btn" (click)="step.set('form')" [disabled]="busy()">Modifier</button>
                <button type="button" class="btn-primary" (click)="submit()" [disabled]="busy()">{{ busy() ? 'Enregistrement…' : mode() === 'return' ? 'Confirmer le retour' : 'Annuler la vente' }}</button>
              </div>
            }
          </section>
        }

        @if (step() === 'done' && result(); as finished) {
          <section class="panel mt-4" role="status" aria-labelledby="fait-titre">
            <h3 id="fait-titre" class="text-xl font-bold text-succes">{{ mode() === 'return' ? 'Retour enregistré' : 'Vente annulée' }}</h3>
            @for (part of finished.refund; track part.method) {
              <p class="amount mt-2 text-lg font-bold">{{ describe(part, false) }}</p>
            }
            @if (finished.refund.length === 0) {
              <p class="mt-2 text-base">Rien à rembourser.</p>
            }
            @if (approval(); as granted) {
              <p class="mt-2 text-base">Validé par {{ granted.approverName }}.</p>
            }
            @if (receipt(); as ticket) {
              <div class="mt-4 rounded border border-sauge bg-ivoire p-3">
                <p class="mb-2 font-semibold">Ticket de retour (facultatif)</p>
                <app-ticket-actions [receipt]="ticket" />
              </div>
            }
            <a class="btn-primary mt-4 w-full" routerLink="/ventes">Retour aux ventes</a>
          </section>
        }
      }
    </section>
  `,
})
export class SaleDetailPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(SalesApi);
  private readonly auth = inject(AuthService);
  private readonly cash = inject(CashService);
  protected readonly network = inject(NetworkService);

  protected readonly reasons = REASONS;
  protected readonly sale = signal<SaleDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal('');
  protected readonly mode = signal<'return' | 'cancel'>('return');
  protected readonly step = signal<'none' | 'form' | 'confirm' | 'done'>('none');
  protected readonly states = signal<LineState[]>([]);
  protected readonly damaged = signal(false);
  protected readonly reason = signal('');
  protected readonly otherReason = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly approval = signal<ApprovalGranted | null>(null);
  protected readonly result = signal<ReversalDone | null>(null);
  protected readonly receipt = signal<Receipt | null>(null);
  protected readonly requestedLines = signal<{ lineIndex: number; quantity: number; resellable: boolean }[]>([]);
  /** Fixés une fois par écran : un nouvel essai après une coupure ne rembourse jamais deux fois. */
  private operationId: Record<'return' | 'cancel', string> = { return: crypto.randomUUID(), cancel: crypto.randomUUID() };

  protected readonly hasReturns = computed(() => (this.sale()?.returns?.length ?? 0) > 0);
  /** Le gérant et le propriétaire n'ont besoin de personne d'autre ; un caissier doit avoir l'accord d'un gérant. */
  protected readonly needsManager = computed(() => !['Owner', 'Manager'].includes(this.auth.user()?.role ?? ''));
  protected readonly reasonText = computed(() => (this.reason() === 'Autre' ? this.otherReason().trim() : this.reason()));
  protected readonly estimate = computed<RefundEstimate>(() => {
    const current = this.sale();
    return current
      ? estimateRefund(
          {
            lines: current.lines.map((line) => ({ quantity: line.quantity, totalFcfa: line.totalFcfa })),
            grossTotalFcfa: current.grossTotalFcfa,
            discountFcfa: current.discountFcfa,
            payments: current.payments.map((payment) => ({ method: payment.method, amountFcfa: payment.amountFcfa })),
            returns: (current.returns ?? []).map((done) => ({ lines: done.lines, refund: done.refund })),
          },
          this.requestedLines(),
        )
      : { totalFcfa: 0, parts: [] };
  });
  protected format = formatFcfa;

  async ngOnInit(): Promise<void> {
    if (!this.network.online()) {
      return;
    }
    try {
      this.sale.set(await this.api.detail(this.route.snapshot.paramMap.get('id') ?? ''));
    } catch (error) {
      this.loadError.set(error instanceof ApiError && error.status === 404 ? 'Cette vente n’existe pas.' : 'La vente n’a pas pu être chargée. Réessayez.');
    } finally {
      this.loading.set(false);
    }
  }

  protected quantity(value: number, unit?: MeasureUnit): string {
    return formatQuantity(value, unit);
  }

  protected method(method: PaymentMethod): string {
    return methodLabel(method);
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }

  /** Ce qui n'a pas encore été rendu sur cette ligne. */
  protected remaining(index: number): number {
    const current = this.sale();
    if (!current) {
      return 0;
    }
    const returned = (current.returns ?? []).flatMap((done) => done.lines).filter((line) => line.lineIndex === index).reduce((sum, line) => sum + line.quantity, 0);
    return current.lines[index].quantity - returned;
  }

  protected startReturn(): void {
    this.begin('return');
  }

  protected startCancel(): void {
    this.begin('cancel');
  }

  private begin(mode: 'return' | 'cancel'): void {
    const current = this.sale();
    this.mode.set(mode);
    this.states.set((current?.lines ?? []).map((_, index) => ({ checked: false, quantity: this.fill(index), resellable: true })));
    this.damaged.set(false);
    this.reason.set('');
    this.otherReason.set('');
    this.error.set('');
    this.approval.set(null);
    this.step.set('form');
  }

  /** La quantité qui reste, telle qu'on la retape (« 1,5 » pour un produit pesé). */
  private fill(index: number): string {
    const current = this.sale();
    const unit = current?.lines[index].unit;
    return formatQuantity(this.remaining(index), unit).replace(/[^\d,]/g, '').replace(/(,\d*?)0+$/, '$1').replace(/,$/, '');
  }

  protected patch(index: number, change: Partial<LineState>): void {
    this.states.update((states) => states.map((state, position) => (position === index ? { ...state, ...change } : state)));
  }

  protected reset(): void {
    this.step.set('none');
    this.error.set('');
  }

  protected proceed(event: Event): void {
    event.preventDefault();
    const current = this.sale();
    if (!current) {
      return;
    }
    const lines = this.mode() === 'cancel' ? this.allLines(current) : this.chosenLines(current);
    if (lines === null) {
      return;
    }
    if (this.reasonText() === '') {
      this.error.set('Choisissez le motif.');
      return;
    }
    this.error.set('');
    this.requestedLines.set(lines);
    this.step.set('confirm');
  }

  private allLines(current: SaleDetail): { lineIndex: number; quantity: number; resellable: boolean }[] {
    return current.lines.map((_, index) => ({ lineIndex: index, quantity: this.remaining(index), resellable: !this.damaged() }));
  }

  private chosenLines(current: SaleDetail): { lineIndex: number; quantity: number; resellable: boolean }[] | null {
    const chosen: { lineIndex: number; quantity: number; resellable: boolean }[] = [];
    for (const [index, state] of this.states().entries()) {
      if (!state.checked) {
        continue;
      }
      const parsed = parseQuantity(state.quantity, current.lines[index].unit ?? 'Piece');
      if (!parsed.ok) {
        this.error.set(`${current.lines[index].productName} : ${parsed.message}`);
        return null;
      }
      if (parsed.quantity > this.remaining(index)) {
        this.error.set(`${current.lines[index].productName} : Vous rendez plus que ce qui a été vendu.`);
        return null;
      }
      chosen.push({ lineIndex: index, quantity: parsed.quantity, resellable: state.resellable });
    }
    if (chosen.length === 0) {
      this.error.set('Cochez au moins un article à rendre.');
      return null;
    }
    return chosen;
  }

  protected async approved(granted: ApprovalGranted): Promise<void> {
    this.approval.set(granted);
    await this.submit();
  }

  protected describe(part: { method: PaymentMethod; amountFcfa: number }, future: boolean): string {
    const amount = formatFcfa(part.amountFcfa);
    if (part.method === 'Cash') {
      return future ? `Vous rendez ${amount} en espèces` : `Rendez ${amount} en espèces`;
    }
    if (part.method === 'Credit') {
      return `Dette effacée : ${amount}`;
    }
    return `À rembourser par ${methodLabel(part.method)} : ${amount}`;
  }

  protected async submit(): Promise<void> {
    const current = this.sale();
    if (!current || this.busy()) {
      return;
    }
    this.error.set('');
    this.busy.set(true);
    const token = this.approval()?.token ?? null;
    const registerId = this.auth.device()?.registerId ?? '';
    const operationId = this.operationId[this.mode()];
    try {
      const done =
        this.mode() === 'cancel'
          ? await this.api.cancel(current.id, { operationId, registerId, reason: this.reasonText(), resellable: !this.damaged(), approvalToken: token })
          : await this.api.returnLines(current.id, { operationId, registerId, reason: this.reasonText(), lines: this.requestedLines(), approvalToken: token });
      this.result.set(done);
      if (done.cashFcfa > 0) {
        await this.cash.recordCashRefund(done.returnId, done.cashFcfa);
      }
      this.receipt.set(this.buildReceipt(current, done));
      this.step.set('done');
    } catch (error) {
      this.error.set(wording(error));
    } finally {
      this.busy.set(false);
    }
  }

  private buildReceipt(current: SaleDetail, done: ReversalDone): Receipt {
    const device = this.auth.device();
    return buildReversalReceipt({
      commerce: device?.commerceName ?? 'Boutique',
      cashier: this.auth.user()?.displayName ?? '',
      register: device?.name ?? 'Caisse',
      at: new Date().toISOString(),
      saleTicket: current.ticket,
      kind: this.mode() === 'cancel' ? 'Cancellation' : 'Return',
      reason: this.reasonText(),
      lines: this.requestedLines().map((item) => {
        const line = current.lines[item.lineIndex];
        return {
          name: line.productName,
          quantity: item.quantity,
          ...(line.unit && line.unit !== 'Piece' ? { unit: line.unit } : {}),
          unitPriceFcfa: line.unitPriceFcfa,
          totalFcfa: lineTotal(line.unitPriceFcfa, item.quantity, line.unit),
        };
      }),
      refund: done.refund,
    });
  }
}

function wording(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 0) {
      return 'Le serveur ne répond pas. Vérifiez le réseau puis réessayez : rien n’est remboursé deux fois.';
    }
    if (error.status === 403 || error.status === 422 || error.status === 404 || error.status === 409) {
      return error.message;
    }
  }
  return 'L’opération a échoué. Réessayez : rien n’est remboursé deux fois.';
}
