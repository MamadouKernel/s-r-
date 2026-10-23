import { computed, inject, Injectable, signal } from '@angular/core';
import { Device, StoredToken } from '../data/journal';
import { ApiError } from '../sync/sync-api';
import { ApiService } from './api.service';
import { JournalService } from './journal.service';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Message en français pour un échec de connexion (PWA-01). */
export function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.status) {
      case 0:
        return 'Connexion impossible : le serveur est injoignable. Vérifiez le réseau.';
      case 401:
        return 'Identifiant ou code PIN incorrect.';
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

  async configureDevice(tenantId: string): Promise<void> {
    const trimmed = tenantId.trim();
    if (!GUID.test(trimmed)) {
      throw new Error('Le code du commerce n’est pas valide : il ressemble à 3f2b8c1e-….');
    }
    const device: Device = { tenantId: trimmed, registerId: crypto.randomUUID(), pointOfSaleId: crypto.randomUUID() };
    await (await this.journalService.get()).saveDevice(device);
    this.deviceState.set(device);
  }

  async login(identifier: string, pin: string): Promise<void> {
    const device = this.deviceState();
    if (!device) {
      throw new Error('Cet appareil n’est pas configuré.');
    }
    const response = await this.api.login(device.tenantId, identifier.trim(), pin);
    const token: StoredToken = { value: response.accessToken, expiresAt: response.expiresAt, user: response.user };
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
