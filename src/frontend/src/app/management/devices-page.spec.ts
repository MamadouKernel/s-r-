import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DomSanitizer } from '@angular/platform-browser';
import { QrService } from '../onboarding/qr.service';
import { NetworkService } from '../services/network.service';
import { ApiError } from '../sync/sync-api';
import { DevicesApi, ManagedDevice, PendingLink } from './devices-api';
import { DevicesPage } from './devices-page';

const device = (overrides: Partial<ManagedDevice> = {}): ManagedDevice => ({
  id: 'd-1', name: 'Caisse 1', enrolledAt: '2026-10-20T08:00:00.000Z', lastSeenAt: '2026-10-24T09:30:00.000Z', isActive: true, revokedAt: null, ...overrides,
});

async function open(devices: ManagedDevice[] = [device()], options: { online?: boolean; links?: PendingLink[] } = {}) {
  const api = {
    list: vi.fn().mockResolvedValue(devices),
    pendingInvitations: vi.fn().mockResolvedValue(options.links ?? []),
    revokeInvitation: vi.fn().mockResolvedValue(undefined),
    invite: vi.fn().mockResolvedValue({ invitationId: 'i-1', token: 'jeton-du-lien', expiresAt: '2026-10-25T08:00:00.000Z' }),
    revoke: vi.fn().mockResolvedValue(undefined),
  };
  const qr = { image: vi.fn().mockImplementation(async () => TestBed.inject(DomSanitizer).bypassSecurityTrustUrl('data:image/svg+xml;charset=utf-8,%3Csvg%2F%3E')) };
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      { provide: DevicesApi, useValue: api },
      { provide: QrService, useValue: qr },
      { provide: NetworkService, useValue: { online: signal(options.online ?? true) } },
    ],
  });
  const fixture = TestBed.createComponent(DevicesPage);
  const root = fixture.nativeElement as HTMLElement;
  const settle = async () => {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
  };
  await settle();
  return {
    api, qr, root, settle,
    has: (text: string) => (root.textContent ?? '').includes(text),
    click: async (label: string) => {
      const target = [...root.querySelectorAll('button')].find((b) => (b.textContent ?? '').includes(label) || b.getAttribute('aria-label')?.includes(label));
      expect(target, `bouton « ${label} » introuvable`).toBeTruthy();
      target!.click();
      await settle();
    },
    type: async (selector: string, value: string) => {
      const input = root.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    },
    submit: async () => {
      root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();
    },
  };
}

describe('DevicesPage', () => {
  it('lists the devices by name with their state in words and their last activity, and no identifier (SC-U3)', async () => {
    const page = await open([
      device(),
      device({ id: 'd-2', name: 'Caisse du fond', isActive: false, revokedAt: '2026-10-22T10:00:00.000Z' }),
    ]);

    expect(page.has('Caisse 1')).toBe(true);
    expect(page.has('Actif')).toBe(true);
    expect(page.has('Retiré le')).toBe(true);
    expect(page.has('dernière activité le')).toBe(true);
    expect(page.has('d-1')).toBe(false);
    expect(page.root.querySelectorAll('[aria-label^="Retirer"]')).toHaveLength(1); // un appareil déjà retiré n'offre rien
  });

  it('says what to do when there is no device yet', async () => {
    const page = await open([]);
    expect(page.has('Aucun appareil pour le moment')).toBe(true);
  });

  it('shows a loading error with a retry, in French', async () => {
    const page = await open();
    page.api.list.mockRejectedValue(new ApiError(0, 'network', ''));
    await page.click('Ajouter un appareil');
    await page.click('Annuler');

    expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('injoignable');
    page.api.list.mockResolvedValue([device()]);
    await page.click('Réessayer');
    expect(page.has('Caisse 1')).toBe(true);
  });

  it('creates an invitation and shows a QR code, the link and how long it lasts (SC-U3)', async () => {
    const page = await open();

    await page.click('Ajouter un appareil');
    await page.type('#nom-invitation', ' Caisse 2 ');
    await page.submit();

    expect(page.api.invite).toHaveBeenCalledWith('Caisse 2');
    expect(page.qr.image).toHaveBeenCalledWith(`${location.origin}/rejoindre#jeton-du-lien`);
    expect(page.root.querySelector('img')?.getAttribute('alt')).toBe('QR code pour ajouter l’appareil');
    expect(page.root.querySelector('#lien-appareil')?.textContent).toContain('/rejoindre#jeton-du-lien');
    expect(page.has('valable 24 heures')).toBe(true);
    expect(page.has('une seule fois')).toBe(true);
  });

  it('sends no name when the field is left empty', async () => {
    const page = await open();
    await page.click('Ajouter un appareil');
    await page.submit();
    expect(page.api.invite).toHaveBeenCalledWith(null);
  });

  it('copies the link and says so', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const page = await open();
    await page.click('Ajouter un appareil');
    await page.submit();

    await page.click('Copier le lien');

    expect(writeText).toHaveBeenCalledWith(`${location.origin}/rejoindre#jeton-du-lien`);
    expect(page.has('Lien copié')).toBe(true);
  });

  it('explains when the link could not be copied automatically', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn().mockRejectedValue(new Error('refusé')) }, configurable: true });
    const page = await open();
    await page.click('Ajouter un appareil');
    await page.submit();

    await page.click('Copier le lien');

    expect(page.has('appuyez longuement sur le lien')).toBe(true);
  });

  it('shows the refusal of the server in plain words and keeps the form', async () => {
    const page = await open();
    page.api.invite.mockRejectedValue(new ApiError(403, 'forbidden', 'Seuls le propriétaire et le gérant peuvent ajouter un appareil.'));
    await page.click('Ajouter un appareil');
    await page.submit();

    expect(page.root.querySelector('#invitation-erreur')?.textContent).toContain('Votre profil n’a pas le droit');
    expect(page.root.querySelector('form')).toBeTruthy();
  });

  it('asks for a confirmation that says what removing means, then removes and reloads (SC-U5)', async () => {
    const page = await open();

    await page.click('Retirer Caisse 1');
    expect(page.has('Retirer Caisse 1 ? Cet appareil ne pourra plus se connecter.')).toBe(true);
    expect(page.has('ventes qu’il n’a pas encore envoyées ne partiront plus')).toBe(true);
    expect(page.api.revoke).not.toHaveBeenCalled();

    await page.click('Non, le garder');
    expect(page.api.revoke).not.toHaveBeenCalled();

    page.api.list.mockResolvedValue([device({ isActive: false, revokedAt: '2026-10-24T10:00:00.000Z' })]);
    await page.click('Retirer Caisse 1');
    await page.click('Oui, retirer');

    expect(page.api.revoke).toHaveBeenCalledWith('d-1');
    expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Appareil retiré : Caisse 1.');
    expect(page.has('Retiré le')).toBe(true);
  });

  it('needs the network to add a device and says so', async () => {
    const page = await open([device()], { online: false });

    expect(page.has('hors ligne')).toBe(true);
    expect(page.root.querySelector<HTMLButtonElement>('button.btn-primary')!.disabled).toBe(true);
  });

  describe('pending links', () => {
    const link = (overrides: Partial<PendingLink> = {}): PendingLink => ({
      id: 'l-1', name: 'Caisse 2', createdByName: 'Issa Traoré', createdAt: '2026-10-25T08:00:00.000Z', expiresAt: new Date(Date.now() + 5 * 3600_000).toISOString(), ...overrides,
    });

    it('lists the links not yet used with who made them and when they expire, and no code', async () => {
      const page = await open([device()], { links: [link(), link({ id: 'l-2', name: 'Appareil sans nom', createdByName: 'Mamadou Konaté', expiresAt: new Date(Date.now() + 22 * 3600_000).toISOString() })] });

      expect(page.has('Liens en attente')).toBe(true);
      expect(page.has('Caisse 2')).toBe(true);
      expect(page.has('Créé par Issa Traoré')).toBe(true);
      expect(page.has('expire dans 5 h')).toBe(true);
      expect(page.has('expire dans 22 h')).toBe(true);
      expect(page.has('Appareil sans nom')).toBe(true);
    });

    it('says when there is none', async () => {
      const page = await open();
      expect(page.has('Aucun lien en attente.')).toBe(true);
    });

    it('asks for a confirmation, then cancels the link and says so', async () => {
      const page = await open([device()], { links: [link()] });

      await page.click('Annuler le lien Caisse 2');
      expect(page.has('le lien répondra qu’il a expiré')).toBe(true);
      expect(page.api.revokeInvitation).not.toHaveBeenCalled();

      await page.click('Non, le garder');
      expect(page.api.revokeInvitation).not.toHaveBeenCalled();

      page.api.pendingInvitations.mockResolvedValue([]);
      await page.click('Annuler le lien Caisse 2');
      await page.click('Oui, annuler ce lien');

      expect(page.api.revokeInvitation).toHaveBeenCalledWith('l-1');
      expect(page.root.querySelector('[role="status"]')?.textContent).toContain('Le lien a été annulé.');
      expect(page.has('Aucun lien en attente.')).toBe(true);
    });

    it('shows the refusal of the server', async () => {
      const page = await open([device()], { links: [link()] });
      page.api.revokeInvitation.mockRejectedValue(new ApiError(422, 'rule_violation', 'Ce lien a déjà servi : l’appareil figure dans la liste des appareils, où vous pouvez le retirer.'));

      await page.click('Annuler le lien Caisse 2');
      await page.click('Oui, annuler ce lien');

      expect(page.root.querySelector('[role="alert"]')?.textContent).toContain('liste des appareils');
    });
  });
});
