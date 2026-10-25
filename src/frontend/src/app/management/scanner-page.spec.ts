import { TestBed } from '@angular/core/testing';
import { typeScan } from '../core/scan-test-support';
import { ScanService } from '../services/scan.service';
import { ScannerPage } from './scanner-page';

/** Les touches d'un code, comme un lecteur les envoie ; `produced` est le caractère que le clavier réglé de travers écrit à la place du chiffre. */
function scan(target: EventTarget, code: string, ending: 'Enter' | 'Tab' | null = 'Enter', produced?: (digit: string) => string): void {
  typeScan(target, code, { ending, produced });
}

async function open() {
  localStorage.clear();
  TestBed.resetTestingModule();
  const fixture = TestBed.createComponent(ScannerPage);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    root,
    service: TestBed.inject(ScanService),
    has: (text: string) => (root.textContent ?? '').includes(text),
    settle: async () => {
      await fixture.whenStable();
      fixture.detectChanges();
    },
  };
}

/** Page créée sous des minuteries simulées (sans attente asynchrone) : pour les délais de plusieurs secondes. */
function openWithFakeTimers() {
  localStorage.clear();
  TestBed.resetTestingModule();
  vi.useFakeTimers();
  const fixture = TestBed.createComponent(ScannerPage);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  return { fixture, has: (text: string) => (root.textContent ?? '').includes(text) };
}

describe('ScannerPage', () => {
  afterEach(() => vi.useRealTimers());

  it('explains how to use it and offers a zone to scan into', async () => {
    const page = await open();

    expect(page.has('Branchez votre lecteur')).toBe(true);
    expect(page.has('Aucun réglage')).toBe(true);
    expect(page.root.querySelector('label[for="zone-scan"]')?.textContent).toContain('Scannez un code ici');
    expect(page.root.querySelector('#zone-scan')).toBeTruthy();
  });

  it('shows the code, its length, the ending, the speed and says the reader is recognised', async () => {
    const page = await open();

    scan(document.body, '6111000000011');
    await page.settle();

    expect(page.has('6111000000011')).toBe(true);
    expect(page.has('13 caractères')).toBe(true);
    expect(page.has('Entrée')).toBe(true);
    expect(page.has('très rapide')).toBe(true);
    expect(page.has('Votre lecteur est bien reconnu.')).toBe(true);
  });

  it('names a Tab ending and a reading without any ending', async () => {
    const page = await open();

    scan(document.body, '6111000000011', 'Tab');
    await page.settle();
    expect(page.has('Tab')).toBe(true);

    scan(document.body, '6111000000028', null);
    await new Promise((resolve) => setTimeout(resolve, 200));
    await page.settle();
    expect(page.has('aucune (reconnu par la vitesse)')).toBe(true);
  });

  it('tells when the digits were shifted and that the application fixes them', async () => {
    const page = await open();
    const layout: Record<string, string> = { 1: '&', 6: '-', 0: 'à' };

    scan(document.body, '6111000000011', 'Enter', (digit) => layout[digit] ?? digit);
    await page.settle();

    expect(page.has('Les chiffres sont décalés')).toBe(true);
    expect(page.has('6111000000011')).toBe(true);
  });

  it('warns about several readings for a single scan', async () => {
    const page = await open();

    scan(document.body, '6111000000011');
    await new Promise((resolve) => setTimeout(resolve, 80));
    scan(document.body, '6111000000011');
    await page.settle();

    expect(page.has('Plusieurs lectures d’affilée')).toBe(true);
    expect(page.has('lecture manuelle')).toBe(true);
  });

  it('keeps the five last scans only', async () => {
    const page = await open();

    for (let index = 0; index < 6; index++) {
      scan(document.body, `61110000000${10 + index}`);
      await new Promise((resolve) => setTimeout(resolve, 20));
    }
    await page.settle();

    expect(page.root.querySelectorAll('[data-testid="historique"] li')).toHaveLength(5);
  });

  it('says nothing arrives when no scan comes within 15 seconds', async () => {
    const page = openWithFakeTimers();
    expect(page.has('Rien n’arrive')).toBe(false);

    vi.advanceTimersByTime(15_000);
    page.fixture.detectChanges();

    expect(page.has('Rien n’arrive')).toBe(true);
    expect(page.has('mode clavier')).toBe(true);
    expect(page.has('USB HID')).toBe(true);
  });

  it('does not say nothing arrives once a scan has come', async () => {
    const page = openWithFakeTimers();

    scan(document.body, '6111000000011');
    vi.advanceTimersByTime(15_000);
    page.fixture.detectChanges();

    expect(page.has('Rien n’arrive')).toBe(false);
  });

  it('says a code typed too slowly cannot be recognised as a scan', async () => {
    const page = await open();
    const zone = page.root.querySelector<HTMLInputElement>('#zone-scan')!;
    zone.focus();
    zone.value = '6111000000011';
    zone.dispatchEvent(new Event('input'));

    zone.closest('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await page.settle();

    expect(page.has('trop lentement')).toBe(true);
  });

  it('lets the user turn the sound off and on, and remembers it', async () => {
    const page = await open();
    const button = () => [...page.root.querySelectorAll('button')].find((candidate) => (candidate.textContent ?? '').includes('Son du lecteur'))!;

    expect(button().textContent).toContain('activé');
    button().click();
    await page.settle();

    expect(button().textContent).toContain('désactivé');
    expect(page.service.soundOn()).toBe(false);
    expect(localStorage.getItem('soro.scanSound')).toBe('off');

    button().click();
    await page.settle();
    expect(button().textContent).toContain('activé');
  });

  it('does not show a scan typed into another field of the page', async () => {
    const page = await open();
    const other = document.createElement('input');
    document.body.append(other);
    other.focus();

    scan(other, '6111000000011');
    await new Promise((resolve) => setTimeout(resolve, 200));
    await page.settle();
    other.remove();

    expect(page.has('6111000000011')).toBe(false);
  });
});
