import { TestBed } from '@angular/core/testing';
import { QrService } from './qr.service';

describe('QrService', () => {
  it('draws a QR code as an SVG image URL without needing a canvas', async () => {
    const image = await TestBed.inject(QrService).image('https://soro.exemple.ci/rejoindre#abc');

    // Angular enveloppe l'URL déclarée sûre ; sa valeur brute est lisible sous cette propriété.
    const text = (image as unknown as { changingThisBreaksApplicationSecurity: string }).changingThisBreaksApplicationSecurity;
    expect(text.startsWith('data:image/svg+xml;charset=utf-8,')).toBe(true);
    expect(decodeURIComponent(text.split(',')[1])).toContain('<svg');
  });

  it('gives different pictures for different links', async () => {
    const service = TestBed.inject(QrService);
    const raw = (image: unknown) => (image as { changingThisBreaksApplicationSecurity: string }).changingThisBreaksApplicationSecurity;
    expect(raw(await service.image('https://a.ci/rejoindre#1'))).not.toBe(raw(await service.image('https://a.ci/rejoindre#2')));
  });
});
