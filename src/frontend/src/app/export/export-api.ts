import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_ROOT, toApiError } from '../services/api.service';
import { ApiError } from '../sync/sync-api';

export interface ExportedFile {
  blob: Blob;
  fileName: string;
}

/** Le nom proposé par le serveur (« soro-export-2026-10-25.xlsx »), lu dans l'en-tête Content-Disposition. */
export function fileNameOf(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const encoded = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(header)?.[1];
  const plain = /filename="?([^";]+)"?/i.exec(header)?.[1];
  const name = encoded ? decodeURIComponent(encoded.trim()) : plain?.trim();
  return name || null;
}

/** Export des données du propriétaire (docs/specs/export-donnees). En ligne seulement ; toute erreur devient une `ApiError` avec le message du serveur. */
@Injectable({ providedIn: 'root' })
export class ExportApi {
  private readonly http = inject(HttpClient);

  async download(from: string | null, to: string | null): Promise<ExportedFile> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    try {
      const response = await firstValueFrom(this.http.get(`${API_ROOT}/data-export`, { params, responseType: 'blob', observe: 'response' }));
      return { blob: response.body as Blob, fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? 'soro-export.xlsx' };
    } catch (error) {
      throw await readableError(error);
    }
  }
}

/** Avec une réponse en fichier, le détail d'une erreur arrive lui aussi sous forme de fichier : on le relit pour garder les mots du serveur. */
async function readableError(error: unknown): Promise<ApiError> {
  if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
    try {
      const body = JSON.parse(await error.error.text()) as { code?: string; detail?: string; title?: string };
      return new ApiError(error.status, body.code ?? 'http_' + error.status, body.detail ?? body.title ?? 'Erreur du serveur.');
    } catch {
      return new ApiError(error.status, 'http_' + error.status, 'Erreur du serveur.');
    }
  }
  return toApiError(error);
}
