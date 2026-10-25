import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SubscriptionApi } from './subscription-api';

describe('SubscriptionApi', () => {
  let api: SubscriptionApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(SubscriptionApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the subscription', async () => {
    const result = api.get();
    http.expectOne('/api/v1/subscription').flush({ status: 'Trial', daysLeft: 5 });

    expect((await result).status).toBe('Trial');
  });

  it('declares a payment with its identifier', async () => {
    const input = { paymentId: 'p-1', period: 'Monthly', method: 'Wave', reference: 'W-1', amountFcfa: 5000 } as const;
    const done = api.declare(input);
    const request = http.expectOne('/api/v1/subscription/payments');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    request.flush({ paymentId: 'p-1', alreadyProcessed: false });

    await done;
  });

  it('turns a refusal into an ApiError with the code and the French message', async () => {
    const result = api.declare({ paymentId: 'p-2', period: 'Monthly', method: 'Wave', reference: 'W-1', amountFcfa: 5000 });
    http.expectOne('/api/v1/subscription/payments').flush(
      { detail: 'Cette référence de transaction a déjà été utilisée.', code: 'mobile_money_reference_used' },
      { status: 409, statusText: 'Conflict' },
    );

    await expect(result).rejects.toMatchObject({ status: 409, code: 'mobile_money_reference_used', message: 'Cette référence de transaction a déjà été utilisée.' });
  });
});
