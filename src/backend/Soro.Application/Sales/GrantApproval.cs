using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Sales;

public sealed record GrantApprovalCommand(string Identifier, string Pin, Guid Subject, string Purpose);

public sealed record ApprovalGranted(string Token, UserId Approver, string ApproverName, DateTimeOffset ExpiresAt);

/// <summary>
/// Un gérant tape son identifiant et son PIN sur la caisse du caissier ; en échange, la caisse reçoit une approbation signée, liée à une vente et à
/// un usage, valable un temps limité (docs/decisions/0020). Le PIN ne reste ni sur la caisse ni dans la file d'envoi. Mêmes règles que la connexion :
/// compte inconnu, mauvais PIN, profil qui n'est pas gérant ou compte désactivé donnent la même réponse, et cinq échecs verrouillent le compte.
/// </summary>
public sealed class GrantApprovalHandler(
    ICurrentUser user,
    IUserRepository users,
    IPinHasher hasher,
    IApprovalSeal seal,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    /// <summary>Une remise part avec la vente, qui peut attendre le retour du réseau (trois jours, comme la connexion sans réseau) ; un retour se fait sur le champ.</summary>
    public static readonly TimeSpan DiscountValidity = TimeSpan.FromDays(3);

    public static readonly TimeSpan ReversalValidity = TimeSpan.FromMinutes(15);

    private static readonly ApplicationError Refused = new(ErrorCodes.InvalidCredentials, "Identifiant ou code PIN incorrect.");

    public async Task<Result<ApprovalGranted>> HandleAsync(GrantApprovalCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return Result<ApprovalGranted>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas demander la validation d'un gérant."));
        }

        if (!ApprovalPurposes.IsKnown(command.Purpose))
        {
            return Result<ApprovalGranted>.Failure(ApplicationError.RuleViolation("Cette validation n'existe pas."));
        }

        var approver = await users.FindByIdentifierAsync(user.Tenant, User.Normalize(command.Identifier ?? string.Empty), cancellationToken);
        if (approver is null)
        {
            hasher.Verify(command.Pin ?? string.Empty, null);
        }

        var approved = approver is not null
            && await ApprovalCheck.VerifyManagerPinAsync(user.Tenant, users, hasher, clock, new Approval(approver.Id, command.Pin ?? string.Empty), cancellationToken);
        if (!approved || approver is null)
        {
            await audit.RecordAsync(
                new AuditEntry(user.Tenant, user.User, AuditActions.ApprovalRefused, command.Subject, $"Validation refusée ({command.Purpose}).", clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<ApprovalGranted>.Failure(Refused);
        }

        var expires = clock.UtcNow + (command.Purpose == ApprovalPurposes.Discount ? DiscountValidity : ReversalValidity);
        return Result<ApprovalGranted>.Success(new ApprovalGranted(
            seal.Issue(user.Tenant, approver.Id, command.Subject, command.Purpose, expires), approver.Id, approver.DisplayName, expires));
    }
}
