import { Component, inject, OnInit, signal } from '@angular/core';
import { SafeUrl } from '@angular/platform-browser';
import { QrService } from '../onboarding/qr.service';
import { NetworkService } from '../services/network.service';
import { DeviceInvitation, DevicesApi, ManagedDevice, PendingLink } from './devices-api';
import { describeError } from './errors';

/**
 * INS-09 : les appareils de la boutique. Le gérant ajoute un appareil (QR code et lien à envoyer) et retire ceux qu'il ne veut plus voir se
 * connecter. En ligne seulement. Aucun identifiant technique n'est montré : l'appareil est reconnu par son nom et son activité.
 */
@Component({
  selector: 'app-devices-page',
  template: `
    <section aria-labelledby="appareils-titre">
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <h2 id="appareils-titre" class="text-xl font-bold">Appareils</h2>
        <button type="button" class="btn-primary ml-auto" (click)="startInvite()" [disabled]="!network.online()">Ajouter un appareil</button>
      </div>

      @if (notice()) {
        <p class="panel mb-4 font-semibold text-succes" role="status">{{ notice() }}</p>
      }
      @if (!network.online()) {
        <p class="panel mb-4 font-semibold text-avertissement" role="note">
          Vous êtes hors ligne : cet écran a besoin du réseau. La caisse, elle, continue de fonctionner.
        </p>
      }

      @if (inviting()) {
        <div class="panel mb-4" aria-labelledby="inviter-titre">
          <h3 id="inviter-titre" class="mb-2 text-lg font-bold">Ajouter un appareil</h3>
          @if (!invitation()) {
            <form (submit)="createInvitation($event)" novalidate>
              <p class="mb-3 text-base">Donnez un nom à cet appareil si vous voulez le reconnaître plus tard. Vous recevrez un QR code et un lien à envoyer à la personne.</p>
              <label class="field-label" for="nom-invitation">Nom de l’appareil (facultatif)</label>
              <input id="nom-invitation" class="field" type="text" autocomplete="off" [value]="suggestedName()" (input)="suggestedName.set($any($event.target).value)" aria-describedby="nom-invitation-aide invitation-erreur" />
              <p id="nom-invitation-aide" class="field-help">Par exemple : Caisse 1, Caisse du fond.</p>
              <p id="invitation-erreur" class="field-error" role="alert">{{ inviteError() }}</p>
              <div class="mt-4 flex flex-wrap gap-2">
                <button type="submit" class="btn-primary" [disabled]="busy()">{{ busy() ? 'Création…' : 'Créer le lien' }}</button>
                <button type="button" class="btn" (click)="cancelInvite()">Annuler</button>
              </div>
            </form>
          } @else {
            <p class="mb-3 text-base">
              Faites scanner ce QR code avec l’appareil photo du téléphone à ajouter, ou envoyez-lui le lien. Il est
              <strong>valable 24 heures et ne sert qu’une seule fois</strong>.
            </p>
            @if (qr()) {
              <img [src]="qr()" alt="QR code pour ajouter l’appareil" class="mx-auto mb-3 h-56 w-56 max-w-full rounded border border-sauge bg-surface p-2" />
            }
            <p class="mb-3 break-all rounded bg-ivoire p-2 font-mono text-sm" id="lien-appareil">{{ link() }}</p>
            <div class="flex flex-wrap gap-2">
              <button type="button" class="btn-primary" (click)="copy()">Copier le lien</button>
              @if (canShare) {
                <button type="button" class="btn" (click)="share()">Partager</button>
              }
              <button type="button" class="btn" (click)="cancelInvite()">Terminé</button>
            </div>
            @if (copied()) {
              <p class="mt-2 font-semibold text-succes" role="status">{{ copied() }}</p>
            }
          }
        </div>
      }

      @if (!loading() && !error()) {
        <section class="mb-4" aria-labelledby="liens-titre">
          <h3 id="liens-titre" class="mb-2 text-sm font-bold uppercase tracking-wide text-ardoise">Liens en attente</h3>
          @if (links().length === 0) {
            <p class="panel text-ardoise">Aucun lien en attente.</p>
          } @else {
            <ul class="flex flex-col gap-3">
              @for (link of links(); track link.id) {
                <li class="panel">
                  <div class="flex flex-wrap items-start gap-3">
                    <div class="min-w-0 flex-1">
                      <p class="break-words text-lg font-semibold">{{ link.name }}</p>
                      <p class="text-sm text-ardoise">Créé par {{ link.createdByName }} · {{ expires(link) }}</p>
                    </div>
                    @if (cancelling() === link.id) {
                      <div class="flex w-full flex-wrap items-center gap-2">
                        <p class="w-full font-semibold text-erreur" role="alert">
                          Annuler ce lien ? La personne qui l’a reçu ne pourra plus s’en servir : le lien répondra qu’il a expiré. Vous pourrez toujours en créer un autre.
                        </p>
                        <button type="button" class="btn" (click)="cancelling.set(null)">Non, le garder</button>
                        <button type="button" class="btn-primary !border-erreur !bg-erreur" (click)="cancelLink(link)" [disabled]="busy()">Oui, annuler ce lien</button>
                      </div>
                    } @else {
                      <button type="button" class="btn" [attr.aria-label]="'Annuler le lien ' + link.name" (click)="cancelling.set(link.id)">Annuler ce lien</button>
                    }
                  </div>
                </li>
              }
            </ul>
          }
        </section>
      }

      @if (loading()) {
        <p class="panel text-ardoise" role="status">Chargement des appareils…</p>
      } @else if (error()) {
        <div class="panel" role="alert">
          <p class="font-semibold text-erreur">{{ error() }}</p>
          <button type="button" class="btn mt-3" (click)="load()">Réessayer</button>
        </div>
      } @else if (devices().length === 0) {
        <p class="panel text-ardoise">Aucun appareil pour le moment. Ajoutez le téléphone ou la tablette de votre caissier avec « Ajouter un appareil ».</p>
      } @else {
        <ul class="flex flex-col gap-3">
          @for (device of devices(); track device.id) {
            <li class="panel">
              <div class="flex flex-wrap items-start gap-3">
                <div class="min-w-0 flex-1">
                  <p class="break-words text-lg font-semibold">{{ device.name }}</p>
                  <p class="font-semibold" [class.text-succes]="device.isActive" [class.text-erreur]="!device.isActive">{{ state(device) }}</p>
                  <p class="text-sm text-ardoise">Ajouté le {{ day(device.enrolledAt) }} · {{ lastSeen(device) }}</p>
                </div>
                @if (device.isActive) {
                  @if (confirming() === device.id) {
                    <div class="flex w-full flex-wrap items-center gap-2">
                      <p class="w-full font-semibold text-erreur" role="alert">
                        Retirer {{ device.name }} ? Cet appareil ne pourra plus se connecter. Les ventes qu’il n’a pas encore envoyées ne partiront plus.
                      </p>
                      <button type="button" class="btn" (click)="confirming.set(null)">Non, le garder</button>
                      <button type="button" class="btn-primary !border-erreur !bg-erreur" (click)="revoke(device)" [disabled]="busy()">Oui, retirer</button>
                    </div>
                  } @else {
                    <button type="button" class="btn" [attr.aria-label]="'Retirer ' + device.name" (click)="confirming.set(device.id)">Retirer</button>
                  }
                }
              </div>
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class DevicesPage implements OnInit {
  private readonly api = inject(DevicesApi);
  private readonly qrService = inject(QrService);
  protected readonly network = inject(NetworkService);

  protected readonly devices = signal<ManagedDevice[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly busy = signal(false);
  protected readonly confirming = signal<string | null>(null);
  protected readonly links = signal<PendingLink[]>([]);
  protected readonly cancelling = signal<string | null>(null);

  protected readonly inviting = signal(false);
  protected readonly suggestedName = signal('');
  protected readonly invitation = signal<DeviceInvitation | null>(null);
  protected readonly link = signal('');
  protected readonly qr = signal<SafeUrl | null>(null);
  protected readonly inviteError = signal('');
  protected readonly copied = signal('');

  protected readonly canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function';

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const [devices, links] = await Promise.all([this.api.list(), this.api.pendingInvitations()]);
      this.devices.set(devices);
      this.links.set(links);
    } catch (error) {
      this.error.set(describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected startInvite(): void {
    this.notice.set('');
    this.invitation.set(null);
    this.suggestedName.set('');
    this.inviteError.set('');
    this.copied.set('');
    this.inviting.set(true);
  }

  protected cancelInvite(): void {
    this.inviting.set(false);
    this.invitation.set(null);
    this.qr.set(null);
    void this.load();
  }

  protected async createInvitation(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.inviteError.set('');
    this.busy.set(true);
    try {
      const invitation = await this.api.invite(this.suggestedName().trim() || null);
      const link = `${location.origin}/rejoindre#${invitation.token}`;
      this.link.set(link);
      this.qr.set(await this.qrService.image(link));
      this.invitation.set(invitation);
    } catch (error) {
      this.inviteError.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.link());
      this.copied.set('Lien copié. Collez-le dans votre messagerie.');
    } catch {
      this.copied.set('Impossible de copier automatiquement : appuyez longuement sur le lien pour le copier.');
    }
  }

  protected async share(): Promise<void> {
    try {
      await navigator.share({ title: 'Sôrô', text: 'Voici le lien pour configurer la caisse :', url: this.link() });
    } catch {
      // Partage annulé par l'utilisateur : rien à dire.
    }
  }

  protected async revoke(device: ManagedDevice): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.notice.set('');
    try {
      await this.api.revoke(device.id);
      this.confirming.set(null);
      this.notice.set(`Appareil retiré : ${device.name}.`);
      await this.load();
    } catch (error) {
      this.confirming.set(null);
      this.error.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancelLink(link: PendingLink): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.notice.set('');
    try {
      await this.api.revokeInvitation(link.id);
      this.cancelling.set(null);
      this.notice.set('Le lien a été annulé.');
      await this.load();
    } catch (error) {
      this.cancelling.set(null);
      this.error.set(describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** « expire dans 5 h » : le temps restant, arrondi à l'heure, jamais une date technique. */
  protected expires(link: PendingLink): string {
    const minutes = Math.round((Date.parse(link.expiresAt) - Date.now()) / 60_000);
    if (minutes < 60) {
      return `expire dans ${Math.max(minutes, 1)} min`;
    }
    return `expire dans ${Math.round(minutes / 60)} h`;
  }

  protected state(device: ManagedDevice): string {
    return device.isActive ? 'Actif' : `Retiré le ${this.day(device.revokedAt ?? device.lastSeenAt)}`;
  }

  protected lastSeen(device: ManagedDevice): string {
    const seen = new Date(device.lastSeenAt);
    return `dernière activité le ${this.day(device.lastSeenAt)} à ${seen.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`;
  }

  protected day(iso: string): string {
    return new Date(iso).toLocaleDateString('fr-FR');
  }
}
