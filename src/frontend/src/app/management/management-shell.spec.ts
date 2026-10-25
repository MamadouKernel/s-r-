import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { ManagementShell } from './management-shell';

async function render(role: string) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AuthService, useValue: { user: signal({ id: 'u', displayName: 'X', role }) } }],
  });
  const fixture = TestBed.createComponent(ManagementShell);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

describe('ManagementShell', () => {
  it('shows the team section to the owner and the manager', async () => {
    expect(await render('Owner')).toContain('Équipe');
    expect(await render('Manager')).toContain('Équipe');
  });

  it('shows the devices section to the owner and the manager only', async () => {
    expect(await render('Owner')).toContain('Appareils');
    expect(await render('Manager')).toContain('Appareils');
    expect(await render('StockKeeper')).not.toContain('Appareils');
  });

  it('shows « Mon abonnement » to the owner and the manager, not to the stock keeper', async () => {
    expect(await render('Owner')).toContain('Mon abonnement');
    expect(await render('Manager')).toContain('Mon abonnement');
    expect(await render('StockKeeper')).not.toContain('Mon abonnement');
  });

  it('shows « Exporter mes données » to the owner only', async () => {
    expect(await render('Owner')).toContain('Exporter mes données');
    expect(await render('Manager')).not.toContain('Exporter mes données');
    expect(await render('StockKeeper')).not.toContain('Exporter mes données');
  });

  it('keeps the team section away from the stock keeper, who still sees the products (SC-E3)', async () => {
    const text = await render('StockKeeper');
    expect(text).toContain('Produits et stock');
    expect(text).not.toContain('Équipe');
  });
});
