import { computed, inject, Injectable, signal } from '@angular/core';
import { failedAttempt, isExpired, isLocked, makeProof, minutesLeft, OFFLINE_DAYS, OFFLINE_ITERATIONS, OFFLINE_ROLES, succeededOffline, verifyPin } from '../core/offline-proof';
import { Device, OfflineSession, StoredToken } from '../data/journal';
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

/** Refus d'une connexion sans réseau : le message dit quoi faire (docs/specs/connexion-hors-ligne HLC-11). */
export class OfflineLoginError extends Error {}

const WRONG = 'Identifiant ou code incorrect.';

/** Appareil, utilisateur et jeton. Le PIN n'est jamais conservé (ADR 0004). */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly onboarding = inject(OnboardingApi);
  private readonly journalService = inject(JournalService);
  private readonly iterations = inject(OFFLINE_ITERATIONS);

  private readonly deviceState = signal<Device | null>(null);
  private readonly tokenState = signal<StoredToken | null>(null);
  private readonly loadedState = signal(false);
  private readonly offlineState = signal<OfflineSession | null>(null);
  private readonly clock = signal(Date.now());

  readonly device = this.deviceState.asReadonly();
  readonly loaded = this.loadedState.asReadonly();
  readonly token = this.tokenState.asReadonly();
  /** Qui est au comptoir sans réseau : pas de jeton, donc ni envoi au serveur ni écran de gestion. */
  readonly offlineSession = this.offlineState.asReadonly();
  readonly user = computed(() => {
    const online = this.tokenState()?.user;
    if (online) {
      return online;
    }
    const offline = this.offlineState();
    return offline ? { id: offline.userId, displayName: offline.displayName, role: offline.role } : null;
  });
  /** Le jeton existe et n'est pas expiré. Un jeton expiré n'empêche pas de vendre, seulement de synchroniser. */
  readonly signedIn = computed(() => {
    const token = this.tokenState();
    return token !== null && Date.parse(token.expiresAt) > this.clock();
  });

  async load(): Promise<void> {
    const journal = await this.journalService.get();
    this.deviceState.set(await journal.getDevice());
    this.tokenState.set(await journal.getToken());
    const offline = await journal.getOfflineSession();
    // Une session sans réseau ne survit pas à sa preuve : au-delà de trois jours il faut du réseau.
    if (offline && Date.parse(offline.validUntil) > Date.now()) {
      this.offlineState.set(offline);
    } else if (offline) {
      await journal.saveOfflineSession(null);
    }
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
    await this.forgetAllProofs();
  }

  async login(identifier: string, pin: string): Promise<void> {
    const device = this.deviceState();
    if (!device) {
      throw new Error('Cet appareil n’est pas configuré.');
    }
    const credentials = device.deviceId && device.deviceSecret ? { id: device.deviceId, secret: device.deviceSecret } : undefined;
    let response;
    try {
      response = await this.api.login(device.tenantId, identifier.trim(), pin, credentials);
    } catch (error) {
      // Le serveur a répondu et refuse ce compte (désactivé, nouveau PIN) : la preuve locale ne vaut plus rien. Sans réponse (réseau), on la garde.
      if (error instanceof ApiError && error.status === 401) {
        await this.forgetProof(identifier);
      }
      throw error;
    }
    await this.keepToken({ value: response.accessToken, expiresAt: response.expiresAt, user: response.user });
    await this.rememberProof(identifier, pin, response.user);
  }

  /**
   * Entrée sans réseau : le PIN tapé est comparé à la preuve gardée lors de la dernière connexion en ligne. Ne s'appelle que quand le serveur n'a
   * pas répondu ; rien n'est envoyé au serveur.
   */
  async loginOffline(identifier: string, pin: string): Promise<void> {
    const journal = await this.journalService.get();
    const proofs = await journal.getProofs();
    const key = identifier.trim().toLowerCase();
    const found = proofs.find((candidate) => candidate.identifier === key);
    if (!found) {
      throw new OfflineLoginError(WRONG);
    }
    const now = Date.now();
    if (isExpired(found, now)) {
      await journal.saveProofs(proofs.filter((candidate) => candidate !== found));
      throw new OfflineLoginError('Il faut du réseau pour vous reconnecter.');
    }
    if (found.blocked) {
      throw new OfflineLoginError('Ce compte demande le réseau pour se reconnecter.');
    }
    if (isLocked(found, now)) {
      throw new OfflineLoginError(waitMessage(minutesLeft(found, now)));
    }

    const ok = await verifyPin(pin, found);
    const updated = ok ? succeededOffline(found) : failedAttempt(found, now);
    await journal.saveProofs(proofs.map((candidate) => (candidate === found ? updated : candidate)));
    if (!ok) {
      throw new OfflineLoginError(isLocked(updated, now) ? (updated.blocked ? 'Ce compte demande le réseau pour se reconnecter.' : waitMessage(minutesLeft(updated, now))) : WRONG);
    }

    const session: OfflineSession = {
      userId: found.userId,
      displayName: found.displayName,
      role: found.role,
      startedAt: new Date(now).toISOString(),
      validUntil: new Date(Date.parse(found.createdAt) + OFFLINE_DAYS * 24 * 3_600_000).toISOString(),
    };
    await journal.saveOfflineSession(session);
    this.offlineState.set(session);
  }

  /** Efface la preuve locale de tous les comptes de cet appareil (appareil retiré, ou « Oublier les comptes »). */
  async forgetAllProofs(): Promise<void> {
    const journal = await this.journalService.get();
    await journal.saveProofs([]);
    await journal.saveOfflineSession(null);
    this.offlineState.set(null);
  }

  private async rememberProof(identifier: string, pin: string, user: { id: string; displayName: string; role: string }): Promise<void> {
    if (!OFFLINE_ROLES.includes(user.role)) {
      return;
    }
    try {
      const journal = await this.journalService.get();
      const proof = await makeProof(pin, { id: user.id, identifier, displayName: user.displayName, role: user.role }, Date.now(), this.iterations);
      const others = (await journal.getProofs()).filter((candidate) => candidate.userId !== user.id && candidate.identifier !== proof.identifier);
      await journal.saveProofs([...others, proof]);
    } catch {
      // La preuve est un confort : sans elle la connexion en ligne réussit quand même.
    }
  }

  private async forgetProof(identifier: string): Promise<void> {
    const journal = await this.journalService.get();
    const key = identifier.trim().toLowerCase();
    const proofs = await journal.getProofs();
    if (proofs.some((candidate) => candidate.identifier === key)) {
      await journal.saveProofs(proofs.filter((candidate) => candidate.identifier !== key));
    }
  }

  private async keepToken(token: StoredToken): Promise<void> {
    await (await this.journalService.get()).saveToken(token);
    this.clock.set(Date.now());
    this.tokenState.set(token);
  }

  async logout(): Promise<void> {
    const journal = await this.journalService.get();
    await journal.clearToken();
    await journal.saveOfflineSession(null);
    this.tokenState.set(null);
    this.offlineState.set(null);
  }

  /** Rafraîchit l'horloge qui décide de l'expiration (appelée par la synchronisation). */
  tick(): void {
    this.clock.set(Date.now());
  }
}

function waitMessage(minutes: number): string {
  return `Réessayez dans ${minutes} minute${minutes > 1 ? 's' : ''}.`;
}
