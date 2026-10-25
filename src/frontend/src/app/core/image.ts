import { Injectable } from '@angular/core';

/** Plus grand côté d'une photo envoyée : assez pour une carte de caisse, léger pour une connexion faible (docs/specs/formats-produits FMT-04). */
export const MAX_SIDE_PX = 480;
/** Poids visé après réduction ; le serveur refuse au-delà de 200 Ko. */
export const TARGET_BYTES = 60 * 1024;
export const MAX_BYTES = 200 * 1024;

export function fitWithin(width: number, height: number, max: number): { width: number; height: number } {
  const ratio = Math.min(1, max / Math.max(width, height));
  return { width: Math.max(1, Math.round(width * ratio)), height: Math.max(1, Math.round(height * ratio)) };
}

/** Réduit une image choisie par l'utilisateur avant l'envoi. Isolé dans un service pour que les écrans se testent sans navigateur graphique. */
@Injectable({ providedIn: 'root' })
export class ImageService {
  /**
   * Renvoie un JPEG réduit. Si le navigateur ne sait pas redessiner l'image, le fichier d'origine passe tel quel pourvu qu'il reste léger ;
   * sinon l'erreur le dit en français.
   */
  async shrink(file: File): Promise<Blob> {
    if (!file.type.startsWith('image/')) {
      throw new Error('Ce fichier n’est pas une image.');
    }
    try {
      const bitmap = await createImageBitmap(file);
      const { width, height } = fitWithin(bitmap.width, bitmap.height, MAX_SIDE_PX);
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      canvas.getContext('2d')!.drawImage(bitmap, 0, 0, width, height);
      bitmap.close();
      let blob: Blob | null = null;
      for (const quality of [0.85, 0.7, 0.55, 0.4]) {
        blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
        if (blob && blob.size <= TARGET_BYTES) {
          break;
        }
      }
      if (blob && blob.size <= MAX_BYTES) {
        return blob;
      }
    } catch {
      // Le navigateur ne sait pas lire ou redessiner ce fichier : on essaie de l'envoyer tel quel plus bas.
    }
    if (file.size <= MAX_BYTES) {
      return file;
    }
    throw new Error('Cette image est trop lourde. Choisissez-en une plus petite.');
  }
}
