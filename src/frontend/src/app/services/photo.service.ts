import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_ROOT } from './api.service';

const CACHE_NAME = 'soro-photos';

/**
 * Charge les photos des produits (docs/specs/formats-produits FMT-04). Une photo est privée au commerce : elle se demande avec le jeton, puis
 * se garde sur l'appareil. Hors ligne, une photo déjà vue s'affiche ; une photo jamais vue donne « rien », sans erreur : l'écran montre alors
 * les initiales. Une photo ne change jamais (une nouvelle photo a un nouvel identifiant), donc la garder ne risque pas d'afficher du périmé.
 */
@Injectable({ providedIn: 'root' })
export class PhotoService {
  private readonly http = inject(HttpClient);
  private readonly known = new Map<string, Promise<string | null>>();

  load(photoId: string | null | undefined): Promise<string | null> {
    if (!photoId) {
      return Promise.resolve(null);
    }
    let pending = this.known.get(photoId);
    if (!pending) {
      pending = this.fetch(photoId).then((url) => {
        if (url === null) {
          this.known.delete(photoId); // un échec n'est pas retenu : on réessaiera à la prochaine demande
        }
        return url;
      });
      this.known.set(photoId, pending);
    }
    return pending;
  }

  private async fetch(photoId: string): Promise<string | null> {
    const address = `${API_ROOT}/photos/${photoId}`;
    try {
      const cached = await this.fromCache(address);
      if (cached) {
        return URL.createObjectURL(cached);
      }
      const blob = await firstValueFrom(this.http.get(address, { responseType: 'blob' }));
      await this.toCache(address, blob);
      return URL.createObjectURL(blob);
    } catch {
      return null;
    }
  }

  private async fromCache(address: string): Promise<Blob | null> {
    try {
      const cache = await globalThis.caches?.open(CACHE_NAME);
      const response = await cache?.match(address);
      return response ? await response.blob() : null;
    } catch {
      return null;
    }
  }

  private async toCache(address: string, blob: Blob): Promise<void> {
    try {
      const cache = await globalThis.caches?.open(CACHE_NAME);
      await cache?.put(address, new Response(blob, { headers: { 'Content-Type': blob.type } }));
    } catch {
      // Pas de cache disponible (navigation privée, stockage plein) : la photo s'affiche quand même cette fois.
    }
  }
}
