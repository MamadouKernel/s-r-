import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { parseFcfa } from '../core/money';
import { CashService } from '../services/cash.service';

/** S3 : le caissier déclare son fond de caisse (REQ-01). */
@Component({
  selector: 'app-open-session-page',
  template: `
    <section class="mx-auto max-w-md panel">
      <h2 class="mb-2 text-xl font-bold">Ouvrir la caisse</h2>
      <p class="mb-4 text-base text-ardoise">Comptez les espèces présentes dans le tiroir avant de commencer.</p>
      <form (submit)="submit($event)" novalidate>
        <label class="field-label" for="fond">Fond de caisse (FCFA)</label>
        <input
          id="fond"
          class="field amount text-right text-2xl"
          type="text"
          inputmode="numeric"
          autocomplete="off"
          [value]="amount()"
          (input)="amount.set($any($event.target).value)"
          [attr.aria-invalid]="error() ? 'true' : null"
          aria-describedby="fond-erreur"
        />
        <p id="fond-erreur" class="field-error" role="alert">{{ error() }}</p>
        <button type="submit" class="btn-primary mt-4 w-full" [disabled]="busy()">Ouvrir la caisse</button>
      </form>
    </section>
  `,
})
export class OpenSessionPage implements OnInit {
  private readonly cash = inject(CashService);
  private readonly router = inject(Router);

  protected readonly amount = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  ngOnInit(): void {
    if (this.cash.session()) {
      void this.router.navigateByUrl('/vente');
    }
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    const value = parseFcfa(this.amount());
    if (value === null) {
      this.error.set('Saisissez le fond de caisse en chiffres, sans virgule (0 s’il est vide).');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    try {
      await this.cash.openSession(value);
      await this.router.navigateByUrl('/vente');
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'La caisse n’a pas pu être ouverte.');
    } finally {
      this.busy.set(false);
    }
  }
}
