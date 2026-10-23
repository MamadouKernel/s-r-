import { Injectable } from '@angular/core';
import { Journal } from '../data/journal';

/** Ouvre le journal IndexedDB une seule fois pour toute l'application. */
@Injectable({ providedIn: 'root' })
export class JournalService {
  private opening: Promise<Journal> | null = null;

  get(): Promise<Journal> {
    this.opening ??= Journal.open();
    return this.opening;
  }
}
