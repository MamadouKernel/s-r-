using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Identity;

namespace Soro.Application.Sales;

/// <summary>
/// Validation par un gérant, prouvée par son PIN (docs/decisions/0018). Un gérant qui agit lui-même n'a rien à prouver de plus. Sinon il faut
/// l'identifiant d'un gérant actif ET son PIN, vérifié comme à la connexion (échecs comptés, compte verrouillé après cinq) : un identifiant seul ne
/// prouve rien. Le propriétaire n'a pas de PIN : il valide en agissant lui-même sur la caisse.
/// </summary>
public static class ApprovalCheck
{
    public static async Task<bool> IsApprovedAsync(
        ICurrentUser user,
        IUserRepository users,
        IPinHasher hasher,
        IClock clock,
        Approval? approval,
        CancellationToken cancellationToken)
    {
        if (RolePolicy.IsManager(user.Role))
        {
            return true;
        }

        if (approval is null)
        {
            return false;
        }

        var approver = await users.GetAsync(approval.Approver, user.Tenant, cancellationToken);
        var now = clock.UtcNow;
        if (approver is null || approver.Role != Role.Manager || approver.Status != UserStatus.Active || approver.IsLocked(now))
        {
            hasher.Verify(approval.Pin ?? string.Empty, null);
            return false;
        }

        if (!hasher.Verify(approval.Pin ?? string.Empty, approver.PinHash))
        {
            approver.RecordFailedAttempt(now);
            await users.UpdateAsync(approver, cancellationToken);
            return false;
        }

        return true;
    }
}
