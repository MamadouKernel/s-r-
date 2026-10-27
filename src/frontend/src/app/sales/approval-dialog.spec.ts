import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { ApprovalDialog } from './approval-dialog';
import { SalesApi } from './sales-api';

async function render(options: { online?: boolean; grant?: ReturnType<typeof vi.fn> } = {}) {
  const grant = options.grant ?? vi.fn().mockResolvedValue({ token: 'sceau', approverId: 'm-1', approverName: 'Kofi Diaby', expiresAt: 'x' });
  TestBed.configureTestingModule({
    providers: [
      { provide: SalesApi, useValue: { grant } },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
    ],
  });
  const fixture = TestBed.createComponent(ApprovalDialog);
  fixture.componentRef.setInput('saleId', 's-1');
  fixture.componentRef.setInput('purpose', 'discount');
  fixture.componentRef.setInput('amountFcfa', 300);
  const granted = vi.fn();
  const cancelled = vi.fn();
  fixture.componentInstance.granted.subscribe(granted);
  fixture.componentInstance.cancelled.subscribe(cancelled);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const type = async (selector: string, value: string) => {
    const input = root.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const submit = async () => {
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
  };
  return { root, type, submit, grant, granted, cancelled, fixture };
}

describe('ApprovalDialog (ARR-07, ARR-08)', () => {
  it('asks the identifier and the code of a manager and hands back the signed approval, never the code', async () => {
    const { type, submit, grant, granted, root } = await render();
    await type('#validation-identifiant', ' gerant ');
    await type('#validation-pin', '482163');

    await submit();

    expect(grant).toHaveBeenCalledWith({ identifier: 'gerant', pin: '482163', saleId: 's-1', purpose: 'discount', amountFcfa: 300 });
    expect(granted).toHaveBeenCalledWith({ token: 'sceau', approverId: 'm-1', approverName: 'Kofi Diaby', expiresAt: 'x' });
    expect(root.querySelector<HTMLInputElement>('#validation-pin')!.value).toBe('');
  });

  it('asks for both fields before calling the server', async () => {
    const { submit, grant, root } = await render();

    await submit();

    expect(grant).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('identifiant et le code PIN du gérant');
  });

  it('says the identifier or the code is wrong, clears the code and does not hand anything back', async () => {
    const refused = vi.fn().mockRejectedValue(new ApiError(401, 'invalid_credentials', 'Identifiant ou code PIN incorrect.'));
    const { type, submit, granted, root } = await render({ grant: refused });
    await type('#validation-identifiant', 'gerant');
    await type('#validation-pin', '0000');

    await submit();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Identifiant ou code PIN incorrect.');
    expect(root.querySelector<HTMLInputElement>('#validation-pin')!.value).toBe('');
    expect(granted).not.toHaveBeenCalled();
  });

  it('needs the network and says so, without trying', async () => {
    const { root, grant } = await render({ online: false });

    expect(root.textContent).toContain('Il faut du réseau');
    expect(root.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    expect(grant).not.toHaveBeenCalled();
  });

  it('can be dismissed', async () => {
    const { root, cancelled, fixture } = await render();

    [...root.querySelectorAll('button')].find((button) => button.textContent?.includes('Annuler'))!.click();
    await fixture.whenStable();

    expect(cancelled).toHaveBeenCalled();
  });
});
