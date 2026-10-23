import { Injectable, signal } from '@angular/core';

/** Message de confirmation affiché une seule fois sur l'écran suivant (« Produit enregistré »). */
@Injectable({ providedIn: 'root' })
export class FlashService {
  private readonly message = signal('');
  readonly current = this.message.asReadonly();

  show(text: string): void {
    this.message.set(text);
  }

  /** Lit le message et l'efface : il ne survit pas à la page qui l'a affiché. */
  consume(): string {
    const text = this.message();
    this.message.set('');
    return text;
  }
}
