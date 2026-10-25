import { Component, computed, input, output } from '@angular/core';
import { ProductAvatar } from '../shared/product-avatar';

/**
 * La photo d'un produit, toujours facultative (docs/specs/formats-produits FMT-04) : boutons pour prendre ou choisir une image, et pour la retirer.
 * La réduction et l'envoi sont l'affaire de l'écran qui l'utilise.
 */
@Component({
  selector: 'app-photo-field',
  imports: [ProductAvatar],
  template: `
    <div class="flex flex-wrap items-center gap-4">
      @if (previewUrl(); as preview) {
        <span class="flex h-24 w-24 shrink-0 items-center justify-center overflow-hidden rounded bg-sauge" aria-hidden="true">
          <img class="h-full w-full object-cover" [src]="preview" alt="" />
        </span>
      } @else {
        <app-product-avatar [name]="name()" [photoId]="photoId()" [large]="true" />
      }
      <div class="min-w-0 flex-1">
        <div class="flex flex-wrap gap-2">
          <label class="btn cursor-pointer">
            Prendre une photo
            <input class="sr-only" type="file" accept="image/*" capture="environment" [disabled]="busy()" (change)="choose($event)" />
          </label>
          <label class="btn cursor-pointer">
            Choisir un fichier
            <input class="sr-only" type="file" accept="image/*" [disabled]="busy()" (change)="choose($event)" />
          </label>
          @if (hasPhoto()) {
            <button type="button" class="btn !border-erreur !text-erreur" [disabled]="busy()" (click)="removed.emit()">Retirer la photo</button>
          }
        </div>
        <p class="field-help">L’image est réduite automatiquement. Sans photo, ce n’est pas grave : la caisse affiche les initiales.</p>
        @if (busy()) {
          <p class="field-help" role="status">Envoi de la photo…</p>
        }
        @if (error()) {
          <p class="field-error" role="alert">{{ error() }}</p>
        }
      </div>
    </div>
  `,
})
export class PhotoField {
  readonly name = input.required<string>();
  /** Photo déjà enregistrée sur le serveur. */
  readonly photoId = input<string | null | undefined>(null);
  /** Aperçu d'une image choisie mais pas encore envoyée (création d'un produit). */
  readonly previewUrl = input<string | null>(null);
  readonly busy = input(false);
  readonly error = input('');

  readonly picked = output<File>();
  readonly removed = output<void>();

  protected readonly hasPhoto = computed(() => !!this.photoId() || !!this.previewUrl());

  protected choose(event: Event): void {
    const field = event.target as HTMLInputElement;
    const file = field.files?.[0];
    field.value = ''; // permet de rechoisir le même fichier après une erreur
    if (file) {
      this.picked.emit(file);
    }
  }
}
