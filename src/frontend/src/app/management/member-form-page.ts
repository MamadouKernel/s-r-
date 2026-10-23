import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '../sync/sync-api';
import { describeError } from './errors';
import { FlashService } from './flash.service';
import { roleLabel } from './role-labels';
import { TeamApi } from './team-api';

const IDENTIFIER = /^[a-z0-9][a-z0-9._-]{2,31}$/;
const PIN = /^\d{4,6}$/;

interface FieldErrors {
  identifier?: string;
  displayName?: string;
  role?: string;
  pin?: string;
  confirmation?: string;
}

/** EQ-04 : ajout d'un membre. Le PIN est saisi deux fois, jamais affiché ni conservé, et vidé après chaque envoi. */
@Component({
  selector: 'app-member-form-page',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-xl" aria-labelledby="membre-titre">
      <a class="btn mb-4" routerLink="/gestion/equipe">← Retour à l’équipe</a>
      <h2 id="membre-titre" class="mb-4 text-xl font-bold">Ajouter un membre</h2>

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement…</p>
      } @else if (loadError()) {
        <p class="panel font-semibold text-erreur" role="alert">{{ loadError() }}</p>
      } @else {
        <form class="panel" (submit)="save($event)" novalidate>
          <label class="field-label" for="nom-membre">Nom</label>
          <input id="nom-membre" class="field" type="text" maxlength="100" autocomplete="off" [value]="displayName()" (input)="displayName.set($any($event.target).value)" [attr.aria-invalid]="errors().displayName ? 'true' : null" aria-describedby="nom-membre-erreur" />
          <p id="nom-membre-erreur" class="field-error">{{ errors().displayName }}</p>

          <label class="field-label mt-4" for="identifiant-membre">Identifiant de connexion</label>
          <input id="identifiant-membre" class="field font-mono" type="text" maxlength="32" autocomplete="off" autocapitalize="off" spellcheck="false" [value]="identifier()" (input)="identifier.set($any($event.target).value)" [attr.aria-invalid]="errors().identifier ? 'true' : null" aria-describedby="identifiant-aide identifiant-erreur" />
          <p id="identifiant-aide" class="field-help">3 à 32 caractères : lettres, chiffres, point, tiret ou souligné. Le membre le saisira pour se connecter.</p>
          <p id="identifiant-erreur" class="field-error">{{ errors().identifier }}</p>

          <label class="field-label mt-4" for="profil-membre">Profil</label>
          <!-- [selected] sur chaque option : [value] sur le select est appliqué avant la création des options. -->
          <select id="profil-membre" class="field" (change)="role.set($any($event.target).value)" [attr.aria-invalid]="errors().role ? 'true' : null" aria-describedby="profil-erreur">
            <option value="" [selected]="role() === ''">Choisir un profil</option>
            @for (option of roles(); track option) {
              <option [value]="option" [selected]="role() === option">{{ label(option) }}</option>
            }
          </select>
          <p id="profil-erreur" class="field-error">{{ errors().role }}</p>

          <label class="field-label mt-4" for="pin-membre">Code PIN (4 à 6 chiffres)</label>
          <input id="pin-membre" class="field amount" type="password" inputmode="numeric" autocomplete="new-password" maxlength="6" [value]="pin()" (input)="pin.set($any($event.target).value)" [attr.aria-invalid]="errors().pin ? 'true' : null" aria-describedby="pin-erreur" />
          <p id="pin-erreur" class="field-error">{{ errors().pin }}</p>

          <label class="field-label mt-4" for="pin-confirmation">Confirmer le code PIN</label>
          <input id="pin-confirmation" class="field amount" type="password" inputmode="numeric" autocomplete="new-password" maxlength="6" [value]="confirmation()" (input)="confirmation.set($any($event.target).value)" [attr.aria-invalid]="errors().confirmation ? 'true' : null" aria-describedby="pin-confirmation-erreur" />
          <p id="pin-confirmation-erreur" class="field-error">{{ errors().confirmation }}</p>
          <p class="field-help">Communiquez le code au membre de vive voix : il n’est ni affiché ni conservé en clair.</p>

          @if (failure()) {
            <p class="field-error mt-4" role="alert">{{ failure() }}</p>
          }
          <button type="submit" class="btn-primary mt-6 w-full" [disabled]="saving()">{{ saving() ? 'Enregistrement…' : 'Ajouter le membre' }}</button>
        </form>
      }
    </section>
  `,
})
export class MemberFormPage implements OnInit {
  private readonly api = inject(TeamApi);
  private readonly router = inject(Router);
  private readonly flash = inject(FlashService);

  /** Fixé à l'ouverture : un nouvel essai après une coupure ne peut jamais créer deux comptes (SC-E8). */
  private readonly userId = crypto.randomUUID();

  protected readonly roles = signal<string[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal('');
  protected readonly saving = signal(false);
  protected readonly failure = signal('');
  protected readonly errors = signal<FieldErrors>({});

  protected readonly displayName = signal('');
  protected readonly identifier = signal('');
  protected readonly role = signal('');
  protected readonly pin = signal('');
  protected readonly confirmation = signal('');

  protected label = roleLabel;

  async ngOnInit(): Promise<void> {
    try {
      // Les profils proposés viennent du serveur : il sait ce que le profil connecté a le droit de créer.
      this.roles.set((await this.api.team()).assignableRoles);
    } catch (error) {
      this.loadError.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.failure.set('');
    const identifier = this.identifier().trim().toLowerCase();
    const displayName = this.displayName().trim();
    const errors = this.validate(identifier, displayName);
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.clearPins();
      return;
    }

    this.saving.set(true);
    const pin = this.pin();
    // Le PIN quitte l'écran dès l'envoi, quel qu'en soit le résultat.
    this.clearPins();
    try {
      await this.api.create(this.userId, { identifier, displayName, role: this.role(), pin });
      this.flash.show(`Membre ajouté : ${displayName}.`);
      await this.router.navigateByUrl('/gestion/equipe');
    } catch (error) {
      if (error instanceof ApiError && error.code === 'identifier_taken') {
        this.errors.set({ identifier: 'Cet identifiant est déjà utilisé dans ce commerce.' });
      } else {
        this.failure.set(describeError(error));
      }
    } finally {
      this.saving.set(false);
    }
  }

  private clearPins(): void {
    this.pin.set('');
    this.confirmation.set('');
  }

  private validate(identifier: string, displayName: string): FieldErrors {
    const errors: FieldErrors = {};
    if (displayName === '') {
      errors.displayName = 'Le nom est obligatoire.';
    }
    if (!IDENTIFIER.test(identifier)) {
      errors.identifier = 'L’identifiant doit faire 3 à 32 caractères : lettres, chiffres, point, tiret ou souligné, en commençant par une lettre ou un chiffre.';
    }
    if (this.role() === '') {
      errors.role = 'Choisissez un profil.';
    }
    if (!PIN.test(this.pin())) {
      errors.pin = 'Le code PIN doit comporter 4 à 6 chiffres.';
    } else if (this.pin() !== this.confirmation()) {
      errors.confirmation = 'Les deux codes PIN ne sont pas identiques.';
    }
    return errors;
  }
}
