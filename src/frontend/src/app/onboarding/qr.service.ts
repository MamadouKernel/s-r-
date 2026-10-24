import { inject, Injectable } from '@angular/core';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';

/** Dessine un QR code en SVG (aucune dépendance au canvas), prêt pour une balise image. Chargé à la demande : seul l'écran des appareils le paie. */
@Injectable({ providedIn: 'root' })
export class QrService {
  private readonly sanitizer = inject(DomSanitizer);

  async image(text: string): Promise<SafeUrl> {
    const { toString } = await import('qrcode');
    const svg = await toString(text, { type: 'svg', margin: 1, errorCorrectionLevel: 'M' });
    // Le SVG vient de notre propre appel à la bibliothèque, avec un texte que nous avons construit : on peut le déclarer sûr.
    return this.sanitizer.bypassSecurityTrustUrl('data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg));
  }
}
