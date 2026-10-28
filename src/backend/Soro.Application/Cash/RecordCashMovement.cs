using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Application.Settings;
using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Application.Cash;

public sealed record RecordCashMovementCommand(
    SessionId Session,
    CashMovementId Id,
    CashMovementKind Kind,
    long AmountFcfa,
    string? Reason,
    DateTimeOffset At,
    Approval? Approval);

public sealed record RecordCashMovementResult(CashMovementId Id, bool AlreadyProcessed);

/// <summary>
/// Une dépense, un versement ou un apport d'espèces sur la session ouverte (docs/specs/cloture-sorties-rapport CLO-01 à CLO-03). Rejouable par
/// identifiant. Une grosse sortie demande l'accord d'un gérant, prouvé par sa preuve signée ; un apport n'en demande jamais.
/// </summary>
public sealed class RecordCashMovementHandler(
    ICurrentUser user,
    ISessionRepository sessions,
    ICashMovementRepository movements,
    IUserRepository users,
    IPinHasher hasher,
    IApprovalSeal seal,
    IShopSettings settings,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RecordCashMovementResult>> HandleAsync(RecordCashMovementCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return Result<RecordCashMovementResult>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas saisir de mouvement de caisse."));
        }

        var session = await sessions.GetAsync(command.Session, user.Tenant, cancellationToken);
        if (session is null)
        {
            return Result<RecordCashMovementResult>.Failure(ApplicationError.NotFound("Session de caisse introuvable."));
        }

        if (!SessionAccess.CanAct(user, session))
        {
            return Result<RecordCashMovementResult>.Failure(
                ApplicationError.Forbidden("Seul le caissier titulaire ou un gérant peut saisir un mouvement sur cette session."));
        }

        if (await movements.GetAsync(command.Id, user.Tenant, cancellationToken) is not null)
        {
            return Result<RecordCashMovementResult>.Success(new(command.Id, AlreadyProcessed: true));
        }

        var shop = await settings.GetAsync(user.Tenant, cancellationToken);
        var approvedBy = (UserId?)null;
        if (command.Kind != CashMovementKind.Topup && command.AmountFcfa > shop.CashOutApprovalThresholdFcfa)
        {
            var approval = await ApprovalCheck.CheckAsync(
                user, users, hasher, seal, clock, command.Approval, command.Id.Value, ApprovalPurposes.CashOut, command.AmountFcfa, cancellationToken);
            if (!approval.Approved)
            {
                await audit.RecordAsync(
                    new AuditEntry(user.Tenant, user.User, AuditActions.CashMovementRefused, command.Id.Value, $"{Label(command.Kind)} de {Money.Fcfa(command.AmountFcfa)} refusée : l'accord d'un gérant manque.", clock.UtcNow),
                    cancellationToken);
                await unitOfWork.CommitAsync(cancellationToken);
                return Result<RecordCashMovementResult>.Failure(
                    ApplicationError.Forbidden($"Au-delà de {Money.Fcfa(shop.CashOutApprovalThresholdFcfa)}, l'accord d'un gérant est nécessaire."));
            }

            approvedBy = approval.ApprovedBy;
        }

        try
        {
            var movement = CashMovement.Record(
                command.Id, user.Tenant, session.Id, command.Kind, Money.Fcfa(command.AmountFcfa), command.Reason, command.At, user.User, approvedBy);
            session.RecordCashMovement(command.Kind, movement.Amount);
            await movements.AddAsync(movement, cancellationToken);
            await sessions.UpdateAsync(session, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant, user.User, AuditActions.CashMovement, movement.Id.Value,
                    $"{Label(movement.Kind)} de {movement.Amount}" + (movement.Reason is null ? string.Empty : $" : {movement.Reason}") + (approvedBy is null ? string.Empty : " (validée par un gérant)"),
                    clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<RecordCashMovementResult>.Success(new(command.Id, AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<RecordCashMovementResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
        catch (ConflictException)
        {
            return await movements.GetAsync(command.Id, user.Tenant, cancellationToken) is not null
                ? Result<RecordCashMovementResult>.Success(new(command.Id, AlreadyProcessed: true))
                : Result<RecordCashMovementResult>.Failure(new ApplicationError(
                    ErrorCodes.RuleViolation, "La caisse vient de changer : rechargez-la et recommencez."));
        }
    }

    private static string Label(CashMovementKind kind) => kind switch
    {
        CashMovementKind.Expense => "Dépense",
        CashMovementKind.Drop => "Versement au coffre",
        _ => "Apport de monnaie",
    };
}
