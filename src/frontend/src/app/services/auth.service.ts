import { computed, inject, Injectable, signal } from '@angular/core';
import { Device, StoredToken } from '../data/journal';
import { OnboardingApi, OwnerSession } from '../onboarding/onboarding-api';
import { ApiError } from '../sync/sync-api';
import { ApiService } from './api.service';
import { JournalService } from './journal.service';

/** Message en français pour un échec de connexion (PWA-01). */
export function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.status) {
      case 0:
        return 'Connexion impossible : le serveur est injoignable. Vérifiez le réseau.';
      case 401:
        return 'Identifiant ou code PIN incorrect.';
      case 403:
        if (error.code === 'device_not_authorized') {
          return 'Cet appareil n’est pas, ou plus, autorisé pour la caisse. Demandez au propriétaire ou au gérant de vous envoyer un nouveau lien.';
        }
        break;
      case 423:
        return 'Ce compte est verrouillé après trop d’essais. Réessayez dans quelques minutes ou demandez à votre gérant.';
      case 429:
        return 'Trop de tentatives. Patientez une minute avant de réessayer.';
    }
  }
  return 'La connexion a échoué. Réessayez.';
}

/** Appareil, utilisateur et jeton. Le PIN n'est jamais conservé (ADR 0004). */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly onboarding = inject(OnboardingApi);
  private readonly journalService = inject(JournalService);

  private readonly deviceState = signal<Device | null>(null);
  private readonly tokenState = signal<StoredToken | null>(null);
  private readonly loadedState = signal(false);
  private readonly clock = signal(Date.now());

  readonly device = this.deviceState.asReadonly();
  readonly loaded = this.loadedState.asReadonly();
  readonly token = this.tokenState.asReadonly();
  readonly user = computed(() => this.tokenState()?.user ?? null);
  /** Le jeton existe et n'est pas expiré. Un jeton expiré n'empêche pas de vendre, seulement de synchroniser. */
  readonly signedIn = computed(() => {
    const token = this.tokenState();
    return token !== null && Date.parse(token.expiresAt) > this.clock();
  });

  async load(): Promise<void> {
    const journal = await this.journalService.get();
    this.deviceState.set(await journal.getDevice());
    this.tokenState.set(await journal.getToken());
    this.loadedState.set(true);
  }

  /**
   * Le propriétaire vient de s'inscrire ou de se connecter par mot de passe : cet appareil connaît désormais son commerce (c'est ce qui
   * remplace l'ancien « code du commerce » tapé à la main) et garde la session. S'il avait déjà été enrôlé pour ce commerce, il le reste.
   */
  async acceptOwnerSession(session: OwnerSession): Promise<void> {
    const current = this.deviceState();
    const device: Device =
      current && current.tenantId === session.tenantId
        ? { ...current, commerceName: session.commerceName }
        : { tenantId: session.tenantId, registerId: crypto.randomUUID(), pointOfSaleId: crypto.randomUUID(), commerceName: session.commerceName };
    const journal = await this.journalService.get();
    await journal.saveDevice(device);
    this.deviceState.set(device);
    await this.keepToken({ value: session.accessToken, expiresAt: session.expiresAt, user: session.user });
  }

  /** L'appareil présente le lien du propriétaire : il reçoit son identité et la garde pour toutes ses connexions. */
  async enrollDevice(token: string, deviceName: string): Promise<Device> {
    const enrolled = await this.onboarding.enrollDevice(token.trim(), deviceName.trim() || null);
    const current = this.deviceState();
    const sameCommerce = current && current.tenantId === enrolled.tenantId;
    const device: Device = {
      tenantId: enrolled.tenantId,
      registerId: sameCommerce ? current.registerId : crypto.randomUUID(),
      pointOfSaleId: sameCommerce ? current.pointOfSaleId : crypto.randomUUID(),
      deviceId: enrolled.deviceId,
      deviceSecret: enrolled.deviceSecret,
      name: enrolled.deviceName,
      commerceName: enrolled.commerceName,
    };
    await (await this.journalService.get()).saveDevice(device);
    this.deviceState.set(device);
    return device;
  }

  /**
   * Le serveur ne reconnaît plus cet appareil (retiré par le gérant) : on oublie ses identifiants et la session, mais pas le journal, pour que
   * les ventes déjà enregistrées ne soient pas perdues.
   */
  async markDeviceRefused(): Promise<void> {
    const current = this.deviceState();
    if (!current) {
      return;
    }
    const device: Device = { ...current, deviceId: undefined, deviceSecret: undefined, revoked: true };
    await (await this.journalService.get()).saveDevice(device);
    this.deviceState.set(device);
    await this.logout();
  }

  async login(identifier: string, pin: string): Promise<void> {
    const device = this.deviceState();
    if (!device) {
      throw new Error('Cet appareil n’est pas configuré.');
    }
    const credentials = device.deviceId && device.deviceSecret ? { id: device.deviceId, secret: device.deviceSecret } : undefined;
    const response = await this.api.login(device.tenantId, identifier.trim(), pin, credentials);
    await this.keepToken({ value: response.accessToken, expiresAt: response.expiresAt, user: response.user });
  }

  private async keepToken(token: StoredToken): Promise<void> {
    await (await this.journalService.get()).saveToken(token);
    this.clock.set(Date.now());
    this.tokenState.set(token);
  }

  async logout(): Promise<void> {
    await (await this.journalService.get()).clearToken();
    this.tokenState.set(null);
  }

  /** Rafraîchit l'horloge qui décide de l'expiration (appelée par la synchronisation). */
  tick(): void {
    this.clock.set(Date.now());
  }
}
