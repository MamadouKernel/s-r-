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
});
