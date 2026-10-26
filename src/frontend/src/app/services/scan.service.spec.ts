import { TestBed } from '@angular/core/testing';
import { ScanResult } from '../core/scan';
import { typeScan as scanKeys } from '../core/scan-test-support';
import { ScanService } from './scan.service';

/** Envoie les touches d'un code comme un lecteur, sur la cible donnée ; rend les événements envoyés. */
function typeScan(target: EventTarget, code: string, ending: 'Enter' | 'Tab' | null = 'Enter'): KeyboardEvent[] {
  const events: KeyboardEvent[] = [];
  const record = (event: Event) => events.push(event as KeyboardEvent);
  document.addEventListener('keydown', record);
  scanKeys(target, code, { ending });
  document.removeEventListener('keydown', record);
  return events;
}

describe('ScanService', () => {
  let service: ScanService;
  let scans: ScanResult[];
  let stop: () => void;

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    TestBed.resetTestingModule();
    service = TestBed.inject(ScanService);
    scans = [];
    stop = service.listen({ accept: () => true, onScan: (scan) => scans.push(scan) });
  });

  afterEach(() => {
    stop();
    vi.useRealTimers();
  });

  it('delivers a scan ended by Enter and keeps the Enter from submitting anything', () => {
    const events = typeScan(document.body, '6111000000011');

    expect(scans).toHaveLength(1);
    expect(scans[0]).toMatchObject({ code: '6111000000011', end: 'enter' });
    expect(events[events.length - 1].defaultPrevented).toBe(true);
    expect(events[0].defaultPrevented).toBe(false); // les chiffres, eux, s'écrivent normalement dans la zone qui a le focus
  });

  it('delivers a scan with no ending after the silence', () => {
    typeScan(document.body, '6111000000011', null);
    expect(scans).toHaveLength(0);

    vi.advanceTimersByTime(100);

    expect(scans).toHaveLength(1);
    expect(scans[0].end).toBe('silence');
  });

  it('gives the element that had the focus to the listener', () => {
    stop();
    const targets: (EventTarget | null)[] = [];
    stop = service.listen({ accept: () => true, onScan: (_scan, target) => targets.push(target) });
    const field = document.createElement('input');
    document.body.append(field);

    typeScan(field, '6111000000011');
    field.remove();

    expect(targets).toEqual([field]);
  });

  it('ignores the targets the listener does not accept', () => {
    stop();
    stop = service.listen({ accept: (target) => !(target instanceof HTMLInputElement), onScan: (scan) => scans.push(scan) });
    const field = document.createElement('input');
    document.body.append(field);

    const events = typeScan(field, '6111000000011');
    vi.advanceTimersByTime(200);
    field.remove();

    expect(scans).toHaveLength(0);
    expect(events[events.length - 1].defaultPrevented).toBe(false);
  });

  it('stops listening once disposed', () => {
    stop();
    typeScan(document.body, '6111000000011');
    vi.advanceTimersByTime(200);
    expect(scans).toHaveLength(0);
  });

  it('keeps the sound on by default, remembers the choice, and makes no noise when off', () => {
    const created: unknown[] = [];
    class FakeAudio {
      currentTime = 0;
      destination = {};
      constructor() {
        created.push(this);
      }
      createOscillator() {
        return { frequency: { value: 0 }, connect: vi.fn(), start: vi.fn(), stop: vi.fn() };
      }
      createGain() {
        return { gain: { value: 0 }, connect: vi.fn() };
      }
    }
    vi.stubGlobal('AudioContext', FakeAudio);

    expect(service.soundOn()).toBe(true);
    service.feedback(true);
    expect(created).toHaveLength(1);

    service.setSound(false);
    service.feedback(false);
    expect(created).toHaveLength(1);
    expect(localStorage.getItem('soro.scanSound')).toBe('off');

    TestBed.resetTestingModule();
    expect(TestBed.inject(ScanService).soundOn()).toBe(false);
    vi.unstubAllGlobals();
  });

  it('never fails when the browser has no audio', () => {
    vi.stubGlobal('AudioContext', undefined);
    expect(() => service.feedback(true)).not.toThrow();
    vi.unstubAllGlobals();
  });
});
