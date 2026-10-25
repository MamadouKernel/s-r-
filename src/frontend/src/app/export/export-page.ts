import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '../management/errors';
import { NetworkService } from '../services/network.service';
import { ExportApi } from './export-api';

type PeriodChoice = 'month' | 'quarter' | 'year' | 'all' | 'dates';

const CHOICES: readonly { value: PeriodChoice; label: string }[] = [
  { value: 'month', label: '1 mois' },
  { value: 'quarter', label: '3 mois' },
  { value: 'year', label: '12 mois' },
  { value: 'all', label: 'Depuis le début' },
  { value: 'dates', label: 'Choisir les dates' },
];

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** Retire des mois à une date UTC (le pays suit l'heure UTC, sans heure d'été). */
function monthsBefore(today: Date, months: number): Date {
  const date = new Date(today);
  date.setUTCMonth(date.getUTCMonth() - months);
  return date;
}

function frenchDate(iso: string): string {
  const [year, month, day] = iso.split('-');
  return `${day}/${month}/${year}`;
}

/**
 * « Exporter mes données » (docs/specs/export-donnees DON-09) : une période, ce que contient le fichier, un avertissement, un bouton. Réservé au
 * propriétaire ; toujours disponible, même en lecture seule. Écran en ligne seulement.
 */
@Component({
  selector: 'app-export-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-2xl" aria-labelledby="export-titre">
      <h2 id="export-titre" class="mb-3 text-xl font-bold">Exporter mes données</h2>

      @if (!network.online()) {
        <p class="panel mb-3 font-semibold text-information" role="note">
          Vous êtes hors ligne : cet écran a besoin du réseau. La caisse, elle, continue de fonctionner.
        </p>
      }

      <div class="panel">
        <p>
          Téléchargez un fichier Excel avec vos produits, vos clients et l’historique de vos ventes.
          <strong>Cet export reste toujours disponible</strong>, même si votre abonnement est terminé : vos données vous appartiennent.
        </p>
      </div>

      <div class="panel mt-3">
        <h3 class="text-lg font-bold">1. Quelle période d’historique ?</h3>
        <div class="mt-2 flex flex-wrap gap-2" role="group" aria-label="Période">
          @for (choice of choices; track choice.value) {
            <button type="button" class="btn" [class.border-2]="period() === choice.value" [class.border-feuille]="period() === choice.value" [class.text-feuille]="period() === choice.value" [attr.aria-pressed]="period() === choice.value" (click)="choose(choice.value)">{{ choice.label }}</button>
          }
        </div>
        @if (period() === 'dates') {
          <div class="mt-3 grid gap-3 sm:grid-cols-2">
            <div>
              <label class="field-label" for="du">Du</label>
              <input id="du" class="field" type="date" [value]="from()" (input)="from.set($any($event.target).value)" />
            </div>
            <div>
              <label class="field-label" for="au">Au (jour compris)</label>
              <input id="au" class="field" type="date" [value]="to()" (input)="to.set($any($event.target).value)" />
            </div>
          </div>
        }
        @if (rangeError()) {
          <p class="field-error" role="alert">{{ rangeError() }}</p>
        }
        <p class="field-help">
          Historique choisi : <strong>{{ periodText() }}</strong>. La période s’applique aux ventes, aux versements et aux mouvements de stock. Vos produits et vos clients sont toujours complets.
        </p>
      </div>

      <div class="panel mt-3">
        <h3 class="text-lg font-bold">2. Ce que contient le fichier</h3>
        <ul class="mt-2 list-disc space-y-1 pl-6 text-base">
          <li><strong>Produits</strong> : prix, stock, codes-barres, formats, actifs ou désactivés</li>
          <li><strong>Clients</strong> : noms, téléphones, plafonds de crédit et dettes</li>
          <li><strong>Ventes</strong> et <strong>lignes de vente</strong> : ce qui a été vendu, à quel prix, par qui</li>
          <li><strong>Versements</strong> des clients</li>
          <li><strong>Mouvements de stock</strong> : réceptions, ventes, pertes</li>
        </ul>
        <p class="field-help">Jamais dans le fichier : les codes PIN, les mots de passe, les appareils, les photos.</p>
        <p class="mt-3 border-l-4 border-avertissement bg-avertissement/10 p-3">
          <strong>Gardez ce fichier en lieu sûr.</strong> Il contient les numéros de téléphone de vos clients et vos prix d’achat.
        </p>
      </div>

      <div class="panel mt-3">
        @if (failure()) {
          <p class="field-error mb-3" role="alert">{{ failure() }}</p>
        }
        @if (savedAs(); as name) {
          <p class="mb-3 border-l-4 border-feuille bg-feuille/10 p-3" role="status">
            <strong>Votre export est prêt.</strong> Le fichier <strong>{{ name }}</strong> a été enregistré sur cet appareil. Gardez-le en lieu sûr : il contient les téléphones de vos clients et vos prix d’achat.
          </p>
        }
        <button type="button" class="btn-primary w-full" [disabled]="preparing()" (click)="download()">
          {{ preparing() ? 'Préparation du fichier…' : savedAs() ? 'Télécharger de nouveau' : 'Télécharger l’export Excel' }}
        </button>
        @if (preparing()) {
          <p class="field-help" role="status">Nous rassemblons vos données : cela prend quelques secondes.</p>
        } @else {
          <p class="field-help">Un export demande quelques secondes. Vous pouvez en faire 5 par heure.</p>
        }
      </div>

      <a class="btn mt-4" routerLink="/gestion/produits">← Retour à la gestion</a>
    </section>
  `,
})
export class ExportPage {
  private readonly api = inject(ExportApi);
  protected readonly network = inject(NetworkService);
  protected readonly choices = CHOICES;

  protected readonly period = signal<PeriodChoice>('year');
  protected readonly from = signal(isoDate(monthsBefore(new Date(), 1)));
  protected readonly to = signal(isoDate(new Date()));
  protected readonly preparing = signal(false);
  protected readonly failure = signal('');
  protected readonly savedAs = signal('');
  protected readonly rangeError = signal('');

  /** Les deux bornes à envoyer au serveur, en jours (UTC). */
  protected readonly range = computed(() => {
    const today = new Date();
    switch (this.period()) {
      case 'month':
        return { from: isoDate(monthsBefore(today, 1)), to: isoDate(today) };
      case 'quarter':
        return { from: isoDate(monthsBefore(today, 3)), to: isoDate(today) };
      case 'year':
        return { from: isoDate(monthsBefore(today, 12)), to: isoDate(today) };
      case 'all':
        return { from: isoDate(monthsBefore(today, 60)), to: isoDate(today) };
      default:
        return { from: this.from(), to: this.to() };
    }
  });

  protected readonly periodText = computed(() => {
    switch (this.period()) {
      case 'month':
        return 'le dernier mois';
      case 'quarter':
        return 'les 3 derniers mois';
      case 'year':
        return 'les 12 derniers mois';
      case 'all':
        return 'tout l’historique, depuis le début (5 ans au plus)';
      default:
        return this.from() && this.to() ? `du ${frenchDate(this.from())} au ${frenchDate(this.to())}` : 'les dates à choisir';
    }
  });

  protected choose(choice: PeriodChoice): void {
    this.period.set(choice);
    this.rangeError.set('');
  }

  protected async download(): Promise<void> {
    if (this.preparing()) {
      return;
    }
    this.failure.set('');
    this.savedAs.set('');
    const { from, to } = this.range();
    if (!from || !to) {
      this.rangeError.set('Choisissez la date de début et la date de fin.');
      return;
    }
    if (from > to) {
      this.rangeError.set('Le début de la période doit être avant sa fin.');
      return;
    }
    this.rangeError.set('');

    this.preparing.set(true);
    try {
      const file = await this.api.download(from, to);
      this.save(file.blob, file.fileName);
      this.savedAs.set(file.fileName);
    } catch (error) {
      this.failure.set(describeError(error));
    } finally {
      this.preparing.set(false);
    }
  }

  /** Le navigateur enregistre le fichier tout seul, sans ouvrir de nouvel onglet. */
  private save(blob: Blob, fileName: string): void {
    const address = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = address;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(address);
  }
}
