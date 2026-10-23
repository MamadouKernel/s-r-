import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** S1 : l'appareil retient le code de son commerce, une seule fois (PWA-01). */
@Component({
  selector: 'app-device-page',
  template: `
    <section class="mx-auto max-w-md panel">
      <h2 class="mb-2 text-xl font-bold">Configurer cet appareil</h2>
      <p class="mb-4 text-base text-ardoise">
        Saisissez le code de votre commerce, fourni par le propriétaire. Cette étape n’est à faire qu’une fois sur cet appareil.
      </p>
      <form (submit)="submit($event)" novalidate>
        <label class="field-label" for="tenant">Code du commerce</label>
        <input
          id="tenant"
          class="field font-mono"
          type="text"
          autocomplete="off"
          autocapitalize="off"
          spellcheck="false"
          [value]="code()"
          (input)="code.set($any($event.target).value)"
          [attr.aria-invalid]="error() ? 'true' : null"
          aria-describedby="tenant-aide tenant-erreur"
        />
        <p id="tenant-aide" class="field-help">Exemple : 3f2b8c1e-5d4a-4b7e-9c1d-2a6f8e0b1c3d</p>
        <p id="tenant-erreur" class="field-error" role="alert">{{ error() }}</p>
        <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">Enregistrer</button>
      </form>
    </section>
  `,
})
export class DevicePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly code = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set('');
    this.busy.set(true);
    try {
      await this.auth.configureDevice(this.code());
      await this.router.navigateByUrl('/connexion');
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Le code n’a pas pu être enregistré.');
    } finally {
      this.busy.set(false);
    }
  }
}
