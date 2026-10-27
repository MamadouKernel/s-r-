using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Sales;

/// <summary>
/// Validation par un gérant, prouvée par son PIN (docs/decisions/0018). Un gérant qui agit lui-même n'a rien à prouver de plus. Sinon il faut
/// l'identifiant d'un gérant actif ET son PIN, vérifié comme à la connexion (échecs comptés, compte verrouillé après cinq) : un identifiant seul ne
/// prouve rien. Le propriétaire n'a pas de PIN : il valide en agissant lui-même sur la caisse.
/// </summary>
/// <summary>Le verdict d'une validation : approuvée ou non, et par quel gérant (<c>null</c> quand l'auteur est lui-même gérant ou propriétaire).</summary>
public sealed record ApprovalOutcome(bool Approved, UserId? ApprovedBy);

/// <summary>Ce pour quoi un gérant donne son accord ; l'approbation n'est valable que pour cet usage et ce sujet (une vente).</summary>
public static class ApprovalPurposes
{
    public const string Discount = "discount";
    public const string Reversal = "reversal";

    public static bool IsKnown(string purpose) => purpose is Discount or Reversal;
}

/// <summary>Signe et relit les approbations : un gérant tape son PIN une fois, la caisse garde seulement la preuve, liée à une vente et à un usage.</summary>
public interface IApprovalSeal
{
    string Issue(TenantId tenant, UserId approver, Guid subject, string purpose, DateTimeOffset expiresAt);

    /// <returns>Le gérant qui a approuvé, ou <c>null</c> si la preuve est fausse, périmée, d'un autre commerce, d'une autre vente ou d'un autre usage.</returns>
    UserId? Open(string token, TenantId tenant, Guid subject, string purpose, DateTimeOffset now);
}

public static class ApprovalCheck
{
    public static async Task<ApprovalOutcome> CheckAsync(
        ICurrentUser user,
        IUserRepository users,
        IPinHasher hasher,
        IApprovalSeal seal,
        IClock clock,
        Approval? approval,
        Guid subject,
        string purpose,
        CancellationToken cancellationToken)
    {
        if (RolePolicy.IsManager(user.Role))
        {
            return new(true, null);
        }

        if (approval?.Token is { } token)
        {
            var approver = seal.Open(token, user.Tenant, subject, purpose, clock.UtcNow) is { } id
                ? await users.GetAsync(id, user.Tenant, cancellationToken)
                : null;
            // Un gérant désactivé depuis, ou devenu autre chose, n'approuve plus rien.
            return approver is { Role: Role.Manager, Status: UserStatus.Active } ? new(true, approver.Id) : new(false, null);
        }

        return await VerifyManagerPinAsync(user.Tenant, users, hasher, clock, approval, cancellationToken) ? new(true, approval!.Approver) : new(false, null);
    }

    public static async Task<bool> IsApprovedAsync(
        ICurrentUser user,
        IUserRepository users,
        IPinHasher hasher,
        IClock clock,
        Approval? approval,
        CancellationToken cancellationToken) =>
        RolePolicy.IsManager(user.Role) || await VerifyManagerPinAsync(user.Tenant, users, hasher, clock, approval, cancellationToken);

    /// <summary>Le PIN d'un gérant actif de ce commerce, vérifié comme à la connexion : un échec est compté, cinq verrouillent le compte.</summary>
    public static async Task<bool> VerifyManagerPinAsync(
        TenantId tenant,
        IUserRepository users,
        IPinHasher hasher,
        IClock clock,
        Approval? approval,
        CancellationToken cancellationToken)
    {
        if (approval is null)
        {
            return false;
        }

        var approver = await users.GetAsync(approval.Approver, tenant, cancellationToken);
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
