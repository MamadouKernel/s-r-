import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NetworkService } from '../services/network.service';
import { describeError } from './errors';
import { FlashService } from './flash.service';
import { roleLabel } from './role-labels';
import { TeamApi, TeamMember } from './team-api';

type ActionKind = 'pin' | 'role' | 'enable';

/** « d’Awa », « de Moussa » : l'élision devant une voyelle ou un h. */
function de(name: string): string {
  return /^[aeiouyhàâéèêëîïôùû]/i.test(name) ? `d’${name}` : `de ${name}`;
}

/** EQ-03 et EQ-05 : l'équipe, avec l'état de chacun en toutes lettres et la désactivation confirmée. En ligne seulement. */
@Component({
  selector: 'app-team-page',
  imports: [RouterLink],
  template: `
    <section aria-labelledby="equipe-titre">
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <h2 id="equipe-titre" class="text-xl font-bold">Équipe</h2>
        <a class="btn-primary ml-auto" routerLink="/gestion/equipe/nouveau">Ajouter un membre</a>
      </div>

      @if (notice()) {
        <p class="panel mb-4 font-semibold text-succes" role="status">{{ notice() }}</p>
      }
      @if (!network.online()) {
        <p class="panel mb-4 font-semibold text-avertissement" role="note">
          Vous êtes hors ligne : cet écran a besoin du réseau. La caisse, elle, continue de fonctionner.
        </p>
      }

      @if (action(); as current) {
        <form class="panel mb-4" (submit)="submitAction($event)" novalidate aria-labelledby="action-titre">
          <h3 id="action-titre" class="mb-2 text-lg font-bold">{{ title(current) }}</h3>
          @if (current.kind === 'role') {
            <p class="mb-2">Rôle actuel : <strong>{{ role(current.member.role) }}</strong></p>
            <label class="field-label" for="nouveau-role">Nouveau rôle</label>
            <select id="nouveau-role" class="field" (change)="newRole.set($any($event.target).value)">
              <option value="">Choisir un rôle</option>
              @for (option of rolesFor(current.member); track option) {
                <option [value]="option">{{ role(option) }}</option>
              }
            </select>
            @if (newRole()) {
              <p class="mt-3 rounded border border-avertissement bg-ivoire p-3 font-semibold text-avertissement">
                {{ current.member.displayName }} devra se reconnecter pour avoir ses nouveaux droits. Sa session ouverte prendra fin dans les secondes qui suivent.
              </p>
            }
          } @else {
            <label class="field-label" for="nouveau-pin">Nouveau PIN (4 à 6 chiffres)</label>
            <input id="nouveau-pin" class="field amount" type="password" inputmode="numeric" autocomplete="off" maxlength="6" [value]="pin()" (input)="pin.set($any($event.target).value)" />
            <label class="field-label mt-3" for="nouveau-pin-bis">Écrivez-le encore une fois</label>
            <input id="nouveau-pin-bis" class="field amount" type="password" inputmode="numeric" autocomplete="off" maxlength="6" [value]="pinAgain()" (input)="pinAgain.set($any($event.target).value)" />
            <p class="field-help">
              Dites ce PIN à la personne de vive voix : il n’est ni envoyé ni affiché. Si elle est connectée en ce moment, elle devra se reconnecter ; son ancien PIN ne marchera plus.
            </p>
          }
          <p class="field-error" role="alert">{{ actionError() }}</p>
          <div class="mt-3 flex flex-wrap gap-2">
            <button type="submit" class="btn-primary" [disabled]="busy()">{{ submitLabel(current) }}</button>
            <button type="button" class="btn" (click)="closeAction()">Annuler</button>
          </div>
        </form>
      }

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement de l’équipe…</p>
      } @else if (error()) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ error() }}</p>
          <button type="button" class="btn mt-3" (click)="load()">Réessayer</button>
        </div>
      } @else if (members().length === 0 && !hasDisabled()) {
        <p class="panel text-ardoise">Aucun membre pour le moment. Ajoutez votre premier caissier avec « Ajouter un membre ».</p>
      } @else {
        @if (hasDisabled()) {
          <label class="mb-3 flex min-h-11 items-center gap-2">
            <input id="voir-desactives" type="checkbox" class="h-5 w-5" [checked]="showDisabled()" (change)="showDisabled.set($any($event.target).checked)" />
            Afficher les membres désactivés
          </label>
        }
        <div class="relative overflow-x-auto rounded border border-sauge bg-surface">
          <table class="w-full min-w-[40rem] text-left text-base">
            <caption class="sr-only">Membres de l’équipe</caption>
            <thead class="border-b border-sauge text-sm text-ardoise">
              <tr>
                <th scope="col" class="px-3 py-2">Membre</th>
                <th scope="col" class="px-3 py-2">Profil</th>
                <th scope="col" class="px-3 py-2">État</th>
                <th scope="col" class="px-3 py-2">Dernière connexion</th>
                <th scope="col" class="px-3 py-2"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody class="divide-y divide-sauge">
              @for (member of visible(); track member.id) {
                <tr>
                  <th scope="row" class="px-3 py-3 font-semibold">
                    {{ member.displayName }}
                    @if (member.isSelf) {
                      <span class="text-sm font-normal text-ardoise">(vous)</span>
                    }
                    <span class="block font-mono text-sm font-normal text-ardoise">{{ member.identifier }}</span>
                  </th>
                  <td class="px-3 py-3">{{ role(member.role) }}</td>
                  <td class="px-3 py-3">
                    <span class="font-semibold" [class.text-erreur]="member.status === 'Disabled'" [class.text-avertissement]="isLocked(member)">{{ state(member) }}</span>
                  </td>
                  <td class="px-3 py-3 text-sm">{{ lastLogin(member) }}</td>
                  <td class="px-3 py-3">
                    <div class="flex flex-wrap justify-end gap-2">
                      @if (member.canRenewCode) {
                        <button type="button" class="btn" [attr.aria-label]="'Nouveau PIN ' + member.displayName" (click)="openAction('pin', member)">Nouveau PIN</button>
                      }
                      @if (member.canChangeRole) {
                        <button type="button" class="btn" [attr.aria-label]="'Changer le rôle ' + member.displayName" (click)="openAction('role', member)">Changer le rôle</button>
                      }
                      @if (member.canEnable) {
                        <button type="button" class="btn" [attr.aria-label]="'Réactiver ' + member.displayName" (click)="openAction('enable', member)">Réactiver</button>
                      }
                    </div>
                    @if (member.canDisable) {
                      @if (confirming() === member.id) {
                        <div class="flex flex-wrap items-center justify-end gap-2">
                          <span class="w-full text-right font-semibold text-erreur" role="alert">
                            Désactiver {{ member.displayName }} ? Ce compte ne pourra plus se connecter.
                          </span>
                          <button type="button" class="btn" (click)="confirming.set(null)">Non, le garder</button>
                          <button type="button" class="btn-primary !border-erreur !bg-erreur" (click)="disable(member)" [disabled]="busy()">Oui, désactiver</button>
                        </div>
                      } @else {
                        <div class="flex justify-end">
                          <button type="button" class="btn" [attr.aria-label]="'Désactiver ' + member.displayName" (click)="confirming.set(member.id)">Désactiver</button>
                        </div>
                      }
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (confirming()) {
          <p class="field-help">
            Les ventes passées restent attribuées à ce nom. Sa session ouverte prendra fin dans les secondes qui suivent.
          </p>
        }
      }
    </section>
  `,
})
export class TeamPage implements OnInit {
  private readonly api = inject(TeamApi);
  private readonly flash = inject(FlashService);
  protected readonly network = inject(NetworkService);

  protected readonly members = signal<TeamMember[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly confirming = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly showDisabled = signal(false);
  protected readonly action = signal<{ kind: ActionKind; member: TeamMember } | null>(null);
  protected readonly pin = signal('');
  protected readonly pinAgain = signal('');
  protected readonly newRole = signal('');
  protected readonly actionError = signal('');
  private readonly assignable = signal<string[]>([]);

  protected readonly hasDisabled = computed(() => this.members().some((member) => member.status === 'Disabled'));
  protected readonly visible = computed(() =>
    this.showDisabled() ? this.members() : this.members().filter((member) => member.status === 'Active'),
  );

  async ngOnInit(): Promise<void> {
    this.notice.set(this.flash.consume());
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const team = await this.api.team();
      this.members.set(team.users);
      this.assignable.set(team.assignableRoles);
    } catch (error) {
      this.members.set([]);
      this.error.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async disable(member: TeamMember): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.disable(member.id);
      this.confirming.set(null);
      this.notice.set(`Compte désactivé : ${member.displayName}.`);
      await this.reload();
    } catch (error) {
      this.confirming.set(null);
      this.error.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected openAction(kind: ActionKind, member: TeamMember): void {
    this.notice.set('');
    this.confirming.set(null);
    this.pin.set('');
    this.pinAgain.set('');
    this.newRole.set('');
    this.actionError.set('');
    this.action.set({ kind, member });
  }

  protected closeAction(): void {
    this.action.set(null);
    this.pin.set('');
    this.pinAgain.set('');
  }

  protected rolesFor(member: TeamMember): string[] {
    return this.assignable().filter((candidate) => candidate !== member.role);
  }

  protected title(current: { kind: ActionKind; member: TeamMember }): string {
    const name = current.member.displayName;
    return current.kind === 'pin' ? `Nouveau PIN pour ${name}` : current.kind === 'enable' ? `Réactiver ${name}` : `Changer le rôle de ${name}`;
  }

  protected submitLabel(current: { kind: ActionKind }): string {
    return current.kind === 'pin' ? 'Enregistrer le nouveau PIN' : current.kind === 'enable' ? 'Réactiver ce membre' : 'Changer le rôle';
  }

  protected async submitAction(event: Event): Promise<void> {
    event.preventDefault();
    const current = this.action();
    if (!current || this.busy()) {
      return;
    }
    const { kind, member } = current;
    if (kind === 'role') {
      if (!this.newRole()) {
        this.actionError.set('Choisissez le nouveau rôle.');
        return;
      }
    } else if (!/^\d{4,6}$/.test(this.pin())) {
      this.actionError.set('Le PIN doit comporter 4 à 6 chiffres.');
      return;
    } else if (this.pin() !== this.pinAgain()) {
      this.actionError.set('Les deux PIN ne sont pas identiques.');
      return;
    }

    this.actionError.set('');
    this.busy.set(true);
    try {
      if (kind === 'pin') {
        await this.api.resetPin(member.id, this.pin());
        this.notice.set(`Le PIN ${de(member.displayName)} a été changé. Dites-le-lui de vive voix.`);
      } else if (kind === 'enable') {
        await this.api.enable(member.id, this.pin());
        this.notice.set(`${member.displayName} est de nouveau actif. Dites-lui son nouveau PIN de vive voix.`);
      } else {
        await this.api.changeRole(member.id, this.newRole());
        this.notice.set(`Le rôle ${de(member.displayName)} est maintenant ${roleLabel(this.newRole())}.`);
      }
      this.closeAction();
      await this.reload();
    } catch (error) {
      this.actionError.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    const team = await this.api.team();
    this.members.set(team.users);
    this.assignable.set(team.assignableRoles);
  }

  protected role = roleLabel;

  protected isLocked(member: TeamMember): boolean {
    return member.status === 'Active' && member.lockedUntil !== null;
  }

  /** L'état en toutes lettres : la couleur seule ne porte jamais le sens (DESIGN.md §7). */
  protected state(member: TeamMember): string {
    if (member.status === 'Disabled') {
      return 'Désactivé';
    }
    if (member.lockedUntil) {
      return `Verrouillé jusqu’à ${new Date(member.lockedUntil).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`;
    }
    return 'Actif';
  }

  protected lastLogin(member: TeamMember): string {
    return member.lastLoginAt
      ? new Date(member.lastLoginAt).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' })
      : 'Jamais connecté';
  }
}
