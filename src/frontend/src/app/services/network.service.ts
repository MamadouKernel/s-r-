import { Injectable, signal } from '@angular/core';

/** État du réseau tel que le navigateur le voit. L'état réel du serveur se déduit des échecs d'envoi (voir `SyncService`). */
@Injectable({ providedIn: 'root' })
export class NetworkService {
  private readonly onlineState = signal(typeof navigator === 'undefined' ? true : navigator.onLine);
  readonly online = this.onlineState.asReadonly();

  constructor() {
    if (typeof window !== 'undefined') {
      window.addEventListener('online', () => this.onlineState.set(true));
      window.addEventListener('offline', () => this.onlineState.set(false));
    }
  }
}
