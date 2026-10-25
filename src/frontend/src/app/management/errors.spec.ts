import { ApiError } from '../sync/sync-api';
import { describeError } from './errors';

describe('describeError', () => {
  it('explains the network, session and rights failures', () => {
    expect(describeError(new ApiError(0, 'network', ''))).toMatch(/injoignable/);
    expect(describeError(new ApiError(401, 'x', ''))).toMatch(/expiré/);
    expect(describeError(new ApiError(403, 'forbidden', 'Seul un gérant…'))).toMatch(/pas le droit/);
    expect(describeError(new ApiError(429, 'x', ''))).toMatch(/Trop de demandes/);
  });

  it('keeps the French detail of the server for a refused request', () => {
    expect(describeError(new ApiError(422, 'rule_violation', 'La perte dépasse le stock disponible.'))).toBe(
      'La perte dépasse le stock disponible.',
    );
    expect(describeError(new ApiError(409, 'barcode_taken', 'Ce code-barres est déjà utilisé.'))).toBe('Ce code-barres est déjà utilisé.');
  });

  it('never leaks a server error, and has a fallback for anything else', () => {
    expect(describeError(new ApiError(500, 'internal_error', 'Stack trace…'))).toBe('L’opération a échoué. Réessayez dans un instant.');
    expect(describeError(new Error('boom'))).toBe('L’opération a échoué. Réessayez dans un instant.');
  });

  it('keeps the server wording when the subscription has ended, instead of a generic « no right » message', () => {
    const error = new ApiError(403, 'subscription_expired', 'Votre abonnement est terminé : la boutique est en lecture seule.');

    expect(describeError(error)).toBe('Votre abonnement est terminé : la boutique est en lecture seule.');
    expect(describeError(new ApiError(403, 'forbidden', 'x'))).toContain('n’a pas le droit');
  });

  it('keeps the server wording, with the waiting time, when too many exports were asked', () => {
    const error = new ApiError(429, 'too_many_requests', 'Vous avez demandé plusieurs exports : réessayez dans 12 minutes.');

    expect(describeError(error)).toBe('Vous avez demandé plusieurs exports : réessayez dans 12 minutes.');
    expect(describeError(new ApiError(429, 'http_429', ''))).toContain('Trop de demandes');
  });
});
