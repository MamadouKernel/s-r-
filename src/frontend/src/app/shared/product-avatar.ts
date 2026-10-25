import { Component, effect, inject, input, signal } from '@angular/core';
import { initialsOf } from '../domain/families';
import { PhotoService } from '../services/photo.service';

/**
 * Photo d'un produit, ou ses initiales quand il n'en a pas ou qu'elle n'est pas disponible. Purement décoratif : le nom du produit est toujours
 * écrit à côté, un lecteur d'écran n'a rien à en tirer.
 */
@Component({
  selector: 'app-product-avatar',
  template: `
    @if (url(); as address) {
      <img class="h-full w-full rounded object-cover" [src]="address" alt="" />
    } @else {
      <span class="text-lg font-bold text-ardoise" aria-hidden="true">{{ initials() }}</span>
    }
  `,
  host: { class: 'flex shrink-0 items-center justify-center overflow-hidden rounded bg-sauge', '[class]': 'sizeClass()', 'aria-hidden': 'true' },
})
export class ProductAvatar {
  private readonly photos = inject(PhotoService);

  readonly name = input.required<string>();
  readonly photoId = input<string | null | undefined>(null);
  readonly large = input(false);

  protected readonly url = signal<string | null>(null);

  protected initials = () => initialsOf(this.name());
  protected sizeClass = () => (this.large() ? 'h-24 w-24' : 'h-14 w-14');

  constructor() {
    effect(() => {
      const id = this.photoId();
      this.url.set(null);
      void this.photos.load(id).then((address) => {
        if (id === this.photoId()) {
          this.url.set(address);
        }
      });
    });
  }
}
