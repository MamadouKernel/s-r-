import { TestBed } from '@angular/core/testing';
import { SubscriptionApi, SubscriptionView } from './subscription-api';
import { SubscriptionService } from './subscription.service';

const view = (overrides: Partial<SubscriptionView> = {}): SubscriptionView => ({
  status: 'Trial', endsAt: '2026-11-01T08:00:00Z', readOnlyFrom: '2026-11-08T08:00:00Z', daysLeft: 5, isReadOnly: false,
  monthlyPriceFcfa: 5000, annualPriceFcfa: 50000, payNumbers: null, payments: null, ...overrides,
});

describe('SubscriptionService', () => {
  const get = vi.fn();

  beforeEach(() => {
    vi.useFakeTimers();
    get.mockReset();
    TestBed.configureTestingModule({ providers: [{ provide: SubscriptionApi, useValue: { get } }] });
  });

  afterEach(() => vi.useRealTimers());

  it('knows nothing until it has read the state, and is not read-only by default', () => {
    const service = TestBed.inject(SubscriptionService);

    expect(service.view()).toBeNull();
    expect(service.readOnly()).toBe(false);
  });

  it('reads the state at start and every minute after, until stopped', async () => {
    get.mockResolvedValue(view());
    const service = TestBed.inject(SubscriptionService);

    service.start();
    service.start(); // un second démarrage ne double pas les lectures
    await vi.advanceTimersByTimeAsync(0);
    expect(get).toHaveBeenCalledTimes(1);
    expect(service.view()?.daysLeft).toBe(5);

    await vi.advanceTimersByTimeAsync(60_000);
    expect(get).toHaveBeenCalledTimes(2);

    service.stop();
    await vi.advanceTimersByTimeAsync(120_000);
    expect(get).toHaveBeenCalledTimes(2);
    expect(service.view()).toBeNull();
  });

  it('turns read-only on and off with the server state, so a paid subscription frees the till', async () => {
    const service = TestBed.inject(SubscriptionService);
    get.mockResolvedValueOnce(view({ status: 'ReadOnly', isReadOnly: true }));
    await service.refresh();
    expect(service.readOnly()).toBe(true);

    get.mockResolvedValueOnce(view({ status: 'Active', isReadOnly: false }));
    await service.refresh();

    expect(service.readOnly()).toBe(false);
  });

  it('keeps the last known state when the server cannot be reached', async () => {
    const service = TestBed.inject(SubscriptionService);
    get.mockResolvedValueOnce(view({ status: 'Grace' }));
    await service.refresh();

    get.mockRejectedValueOnce(new Error('hors ligne'));
    await service.refresh();

    expect(service.view()?.status).toBe('Grace');
  });
});
