import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NetworkService } from '../services/network.service';
import { describeError } from './errors';
import { FlashService } from './flash.service';
import { roleLabel } from './role-labels';
import { TeamApi, TeamMember } from './team-api';

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

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement de l’équipe…</p>
      } @else if (error()) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ error() }}</p>
          <button type="button" class="btn mt-3" (click)="load()">Réessayer</button>
        </div>
      } @else if (members().length === 0) {
        <p class="panel text-ardoise">Aucun membre pour le moment. Ajoutez votre premier caissier avec « Ajouter un membre ».</p>
      } @else {
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
              @for (member of members(); track member.id) {
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
            Les ventes passées restent attribuées à ce nom. Un accès déjà ouvert reste valable jusqu’à sa fin (8 heures au plus).
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

  async ngOnInit(): Promise<void> {
    this.notice.set(this.flash.consume());
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      this.members.set((await this.api.team()).users);
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
      this.members.set((await this.api.team()).users);
    } catch (error) {
      this.confirming.set(null);
      this.error.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
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
