import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_ROOT, toApiError } from '../services/api.service';

export interface ImportProblem {
  line: number;
  column: string;
  message: string;
  /** « Ligne 12 : … », tel que le serveur l'écrit. */
  text: string;
}

export interface ImportSkip {
  line: number;
  name: string;
  reason: string;
}

/** Compte rendu de la vérification ou de l'importation d'un fichier Excel (docs/specs/import-excel). */
export interface ImportReport {
  rowsRead: number;
  newProducts: number;
  newFormats: number;
  stockMovements: number;
  skippedCount: number;
  skipped: ImportSkip[];
  problemCount: number;
  problems: ImportProblem[];
  canImport: boolean;
  imported: boolean;
  alreadyProcessed: boolean;
}

const XLSX = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/** Appels de l'import Excel : directs au serveur, en ligne seulement. Toute erreur devient une `ApiError`. */
@Injectable({ providedIn: 'root' })
export class ImportApi {
  private readonly http = inject(HttpClient);

  async template(): Promise<Blob> {
    try {
      return await firstValueFrom(this.http.get(`${API_ROOT}/product-imports/template`, { responseType: 'blob' }));
    } catch (error) {
      throw toApiError(error);
    }
  }

  /** Vérifie le fichier sans rien enregistrer. */
  preview(file: Blob): Promise<ImportReport> {
    return this.send(`${API_ROOT}/product-imports/preview`, file);
  }

  /**
   * Importe le fichier, tout ou rien. Quand des lignes sont à corriger, le serveur répond 422 avec le compte rendu : on le rend comme un résultat,
   * pas comme une panne. `importId` est fixé par l'écran : un nouvel essai après une coupure ne crée rien en double.
   */
  import(importId: string, file: Blob): Promise<ImportReport> {
    return this.send(`${API_ROOT}/product-imports?importId=${encodeURIComponent(importId)}`, file);
  }

  private async send(url: string, file: Blob): Promise<ImportReport> {
    try {
      return await firstValueFrom(this.http.post<ImportReport>(url, file, { headers: { 'Content-Type': XLSX } }));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 422 && typeof (error.error as ImportReport | null)?.problemCount === 'number') {
        return error.error as ImportReport;
      }
      throw toApiError(error);
    }
  }
}
