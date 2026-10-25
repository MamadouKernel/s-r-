import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NetworkService } from '../services/network.service';
import { describeError } from './errors';
import { ImportApi, ImportReport } from './import-api';

type Stage = 'start' | 'checking' | 'problems' | 'ready' | 'nothing' | 'importing' | 'done';


/**
 * Importer des produits depuis Excel (docs/specs/import-excel IMP-09). Télécharger le modèle, choisir le fichier rempli : il est vérifié tout de suite,
 * sans rien enregistrer ; le bouton d'importation n'apparaît que quand tout est juste. Écran de gestion : en ligne seulement.
 */
@Component({
  selector: 'app-import-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-3xl" aria-labelledby="import-titre">
      <a class="btn mb-4" routerLink="/gestion/produits">← Retour aux produits</a>
      <h2 id="import-titre" class="mb-4 text-xl font-bold">Importer des produits depuis Excel</h2>

      @if (!network.online()) {
        <p class="panel mb-4 font-semibold text-avertissement" role="note">
          Vous êtes hors ligne : cet écran a besoin du réseau. La caisse, elle, continue de fonctionner.
        </p>
      }

      @if (stage() === 'start' || stage() === 'checking') {
        <div class="panel">
          <h3 class="text-lg font-bold">1. Téléchargez le modèle</h3>
          <p class="mt-1 text-base text-ardoise">Remplissez-le dans Excel : une ligne par produit, ou par format (5 kg, 25 kg…).</p>
          <button type="button" class="btn mt-3" [disabled]="downloading()" (click)="downloadTemplate()">
            {{ downloading() ? 'Téléchargement…' : 'Télécharger le modèle Excel' }}
          </button>
          @if (templateError()) {
            <p class="field-error" role="alert">{{ templateError() }}</p>
          }
          <details class="mt-3">
            <summary class="flex min-h-12 cursor-pointer items-center font-semibold">Comment remplir le fichier ?</summary>
            <ul class="mt-1 list-disc space-y-1 pl-6 text-base text-ardoise">
              <li>Plusieurs lignes avec le même nom forment <strong>un seul produit en plusieurs formats</strong>.</li>
              <li>Prix et stock : des nombres entiers, sans virgule. Le prix de vente est obligatoire.</li>
              <li>Le « Stock de départ » est ce que vous avez aujourd’hui en rayon.</li>
              <li>Un produit déjà dans votre catalogue n’est jamais modifié.</li>
            </ul>
          </details>
        </div>
      }

      @if (stage() === 'start' || stage() === 'checking' || stage() === 'problems' || stage() === 'nothing') {
        <div class="panel mt-3">
          <h3 class="text-lg font-bold">{{ stage() === 'start' || stage() === 'checking' ? '2. Choisissez votre fichier rempli' : 'Choisir un autre fichier' }}</h3>
          <p class="mt-1 text-base text-ardoise">Un fichier .xlsx, de 2 Mo et 2 000 lignes au plus. Rien n’est enregistré à cette étape : nous vérifions d’abord votre fichier.</p>
          <label class="btn-primary mt-3 cursor-pointer" [class.opacity-50]="stage() === 'checking'">
            {{ stage() === 'checking' ? 'Vérification en cours…' : 'Choisir un fichier' }}
            <input
              class="sr-only" type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              [disabled]="stage() === 'checking'" (change)="choose($event)"
            />
          </label>
          @if (failure()) {
            <p class="field-error mt-3" role="alert">{{ failure() }}</p>
          }
        </div>
      }

      @if (report(); as current) {
        @if (stage() === 'problems') {
          <div class="panel mt-3">
            <p class="font-semibold">{{ fileName() }} <span class="font-normal text-ardoise">· {{ current.rowsRead }} {{ current.rowsRead > 1 ? 'lignes lues' : 'ligne lue' }}</span></p>
            <p class="mt-3 border-l-4 border-erreur bg-erreur/10 p-3" role="alert">
              <strong>{{ current.problemCount }} {{ current.problemCount > 1 ? 'lignes sont à corriger' : 'ligne est à corriger' }}.</strong>
              Rien n’a été importé. Corrigez le fichier dans Excel, puis choisissez-le de nouveau.
            </p>
            <div class="relative mt-3 overflow-x-auto">
              <table class="w-full text-left text-base">
                <caption class="sr-only">Lignes à corriger</caption>
                <thead class="text-sm text-ardoise">
                  <tr><th scope="col" class="px-2 py-2">Ligne</th><th scope="col" class="px-2 py-2">Colonne</th><th scope="col" class="px-2 py-2">À corriger</th></tr>
                </thead>
                <tbody class="divide-y divide-sauge">
                  @for (problem of current.problems; track $index) {
                    <tr>
                      <td class="amount px-2 py-2 font-bold">{{ problem.line }}</td>
                      <td class="px-2 py-2">{{ problem.column }}</td>
                      <td class="px-2 py-2">{{ capital(problem.message) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (current.problemCount > current.problems.length) {
              <p class="mt-2 text-sm text-ardoise">… et {{ current.problemCount - current.problems.length }} autres lignes à corriger.</p>
            }
          </div>
        }

        @if (stage() === 'ready' || stage() === 'importing') {
          <div class="panel mt-3">
            <p class="font-semibold">{{ fileName() }} <span class="font-normal text-ardoise">· {{ current.rowsRead }} {{ current.rowsRead > 1 ? 'lignes lues' : 'ligne lue' }}</span></p>
            <p class="mt-3 border-l-4 border-feuille bg-feuille/10 p-3" role="status"><strong>Votre fichier est prêt.</strong> Voici ce qui sera créé.</p>
            <dl class="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
              <div class="rounded border border-sauge p-3"><dt class="text-sm text-ardoise">{{ current.newProducts > 1 ? 'produits' : 'produit' }}</dt><dd class="amount text-3xl font-bold">{{ current.newProducts }}</dd></div>
              <div class="rounded border border-sauge p-3"><dt class="text-sm text-ardoise">{{ current.newFormats > 1 ? 'formats au total' : 'format au total' }}</dt><dd class="amount text-3xl font-bold">{{ current.newFormats }}</dd></div>
              <div class="rounded border border-sauge p-3"><dt class="text-sm text-ardoise">{{ current.stockMovements > 1 ? 'stocks de départ' : 'stock de départ' }}</dt><dd class="amount text-3xl font-bold">{{ current.stockMovements }}</dd></div>
              <div class="rounded border border-sauge p-3"><dt class="text-sm text-ardoise">déjà dans votre catalogue (ignorés)</dt><dd class="amount text-3xl font-bold">{{ current.skippedCount }}</dd></div>
            </dl>
            @if (current.skippedCount > 0) {
              <details class="mt-3">
                <summary class="flex min-h-12 cursor-pointer items-center font-semibold">Voir les produits ignorés</summary>
                <ul class="space-y-1 text-base text-ardoise">
                  @for (skip of current.skipped; track $index) {
                    <li>Ligne {{ skip.line }} : {{ skip.name }} — {{ capital(skip.reason) }}</li>
                  }
                </ul>
              </details>
            }
            @if (failure()) {
              <p class="field-error mt-3" role="alert">{{ failure() }}</p>
            }
            <div class="mt-4 flex flex-wrap gap-3">
              <button type="button" class="btn-primary" [disabled]="stage() === 'importing'" (click)="importFile()">
                {{ stage() === 'importing' ? 'Importation…' : importLabel() }}
              </button>
              <button type="button" class="btn" [disabled]="stage() === 'importing'" (click)="reset()">Annuler</button>
            </div>
            <p class="mt-3 text-sm text-ardoise">Les stocks de départ seront enregistrés comme une première réception « Import Excel », visible dans l’historique de chaque produit.</p>
          </div>
        }

        @if (stage() === 'nothing') {
          <div class="panel mt-3" role="status">
            <p class="font-semibold">{{ current.skippedCount > 0 ? 'Rien à importer.' : 'Ce fichier ne contient aucun produit.' }}</p>
            <p class="mt-1 text-base text-ardoise">
              {{ current.skippedCount > 0
                ? 'Tous les produits de ce fichier sont déjà dans votre catalogue : ils ne sont pas modifiés.'
                : 'Remplissez la feuille « Produits » du modèle, puis choisissez le fichier de nouveau.' }}
            </p>
          </div>
        }

        @if (stage() === 'done') {
          <div class="panel mt-3">
            @if (current.alreadyProcessed && !current.imported) {
              <p class="border-l-4 border-feuille bg-feuille/10 p-3" role="status"><strong>Cette importation avait déjà été faite :</strong> rien n’a été ajouté en double.</p>
            } @else {
              <p class="border-l-4 border-feuille bg-feuille/10 p-3" role="status">
                <strong>L’importation est terminée.</strong>
                {{ current.newProducts }} {{ current.newProducts > 1 ? 'produits' : 'produit' }}
                ({{ current.newFormats }} {{ current.newFormats > 1 ? 'formats' : 'format' }}) {{ current.newProducts > 1 ? 'ont été ajoutés' : 'a été ajouté' }}
                à votre catalogue, avec {{ current.stockMovements }} {{ current.stockMovements > 1 ? 'stocks de départ' : 'stock de départ' }}.
              </p>
            }
            <div class="mt-4 flex flex-wrap gap-3">
              <a class="btn-primary" routerLink="/gestion/produits">Voir les produits</a>
              <button type="button" class="btn" (click)="reset()">Importer un autre fichier</button>
            </div>
            <p class="mt-3 text-sm text-ardoise">Astuce : ajoutez ensuite les photos depuis la fiche de chaque produit (facultatif).</p>
          </div>
        }
      }
    </section>
  `,
})
export class ImportPage {
  private readonly api = inject(ImportApi);
  protected readonly network = inject(NetworkService);

  protected readonly stage = signal<Stage>('start');
  protected readonly report = signal<ImportReport | null>(null);
  protected readonly failure = signal('');
  protected readonly templateError = signal('');
  protected readonly downloading = signal(false);
  protected readonly fileName = signal('');

  private file: File | null = null;
  /** Fixé au choix du fichier : un nouvel essai après une coupure ne crée rien en double. */
  private importId = '';

  protected readonly importLabel = computed(() => {
    const count = this.report()?.newProducts ?? 0;
    return count > 0 ? `Importer ${count === 1 ? 'ce produit' : 'ces ' + count + ' produits'}` : `Importer ces formats`;
  });

  protected capital(text: string): string {
    return text.charAt(0).toLocaleUpperCase('fr') + text.slice(1);
  }

  protected async downloadTemplate(): Promise<void> {
    this.templateError.set('');
    this.downloading.set(true);
    try {
      const blob = await this.api.template();
      const address = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = address;
      link.download = 'modele-import-produits.xlsx';
      link.click();
      URL.revokeObjectURL(address);
    } catch (error) {
      this.templateError.set(describeError(error));
    } finally {
      this.downloading.set(false);
    }
  }

  protected async choose(event: Event): Promise<void> {
    const field = event.target as HTMLInputElement;
    const file = field.files?.[0] ?? null;
    field.value = ''; // permet de rechoisir le même fichier après l'avoir corrigé
    if (!file) {
      return;
    }
    this.file = file;
    this.fileName.set(file.name);
    this.importId = crypto.randomUUID();
    this.failure.set('');
    this.report.set(null);
    this.stage.set('checking');
    try {
      const report = await this.api.preview(file);
      this.report.set(report);
      this.stage.set(report.problemCount > 0 ? 'problems' : report.canImport ? 'ready' : 'nothing');
    } catch (error) {
      this.failure.set(describeError(error));
      this.stage.set('start');
    }
  }

  protected async importFile(): Promise<void> {
    if (!this.file || this.stage() === 'importing') {
      return;
    }
    this.failure.set('');
    this.stage.set('importing');
    try {
      const report = await this.api.import(this.importId, this.file);
      this.report.set(report);
      this.stage.set(report.problemCount > 0 ? 'problems' : report.imported || report.alreadyProcessed ? 'done' : 'nothing');
    } catch (error) {
      this.failure.set(describeError(error));
      this.stage.set('ready');
    }
  }

  protected reset(): void {
    this.file = null;
    this.report.set(null);
    this.failure.set('');
    this.fileName.set('');
    this.stage.set('start');
  }
}

