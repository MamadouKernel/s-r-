import { ApiError } from '../sync/sync-api';
import { ACTIVITIES, looksLikeEmail, onboardingError, TERMS_VERSION } from './wording';

describe('onboarding wording', () => {
  it('offers every activity the server knows, in words a trader uses', () => {
    expect(ACTIVITIES.map((activity) => activity.value).sort()).toEqual(
      ['Hardware', 'Other', 'Pharmacy', 'Restaurant', 'Salon', 'Shop', 'Supermarket', 'Workshop'],
    );
    expect(ACTIVITIES.find((activity) => activity.value === 'Hardware')?.label).toBe('Quincaillerie');
    expect(ACTIVITIES.find((activity) => activity.value === 'Restaurant')?.label).toBe('Restaurant ou maquis');
    expect(ACTIVITIES.every((activity) => !/[A-Z][a-z]+[A-Z]/.test(activity.label))).toBe(true);
  });

  it('knows the terms version the server expects', () => {
    expect(TERMS_VERSION).toBe('2026-10-brouillon');
  });

  it('spots an address that cannot be one, without being strict about the rest', () => {
    expect(['a@b.ci', ' mamadou@exemple.ci ', 'x.y+z@sous.domaine.com'].map(looksLikeEmail)).toEqual([true, true, true]);
    expect(['', 'mamadou', 'a@b', '@b.ci', 'a b@c.ci', 'a@b.c'].map(looksLikeEmail)).toEqual([false, false, false, false, false, false]);
  });

  it('gives the server message when it speaks to the trader, and a plain sentence otherwise', () => {
    expect(onboardingError(new ApiError(422, 'invalid_code', 'Code incorrect. Il vous reste 4 essais.'))).toBe('Code incorrect. Il vous reste 4 essais.');
    expect(onboardingError(new ApiError(503, 'email_not_sent', 'Nous n’avons pas pu envoyer l’e-mail.'))).toBe('Nous n’avons pas pu envoyer l’e-mail.');
    expect(onboardingError(new ApiError(0, 'network', ''))).toMatch(/injoignable.*connexion internet/);
    expect(onboardingError(new ApiError(429, 'x', ''))).toMatch(/Patientez une minute/);
    expect(onboardingError(new ApiError(500, 'x', 'Erreur interne'))).toBe('Une erreur est survenue. Réessayez dans un instant.');
    expect(onboardingError(new Error('boom'))).toBe('Une erreur est survenue. Réessayez dans un instant.');
  });

  it('never shows a code, a status or a stack to the trader', () => {
    const messages = [new ApiError(0, 'network', ''), new ApiError(500, 'internal_error', 'x'), new Error('TypeError at x.ts:1')].map(onboardingError);
    for (const message of messages) {
      expect(message).not.toMatch(/\b(5\d\d|4\d\d|null|undefined|TypeError|internal_error|network)\b/);
    }
  });
});
