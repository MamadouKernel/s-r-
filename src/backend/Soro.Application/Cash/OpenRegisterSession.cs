using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Application.Cash;

public sealed record OpenRegisterSessionCommand(
    SessionId SessionId,
    PointOfSaleId PointOfSale,
    RegisterId Register,
    long OpeningFloatFcfa,
    DateTimeOffset OpenedAt);

public sealed record OpenRegisterSessionResult(SessionId SessionId, bool AlreadyProcessed);

/// <summary>Ouverture de caisse (REQ-01). Rejouable : le même identifiant de session est acquitté sans doublon.</summary>
public sealed class OpenRegisterSessionHandler(
    ICurrentUser user,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<OpenRegisterSessionResult>> HandleAsync(
        OpenRegisterSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return Result<OpenRegisterSessionResult>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas ouvrir une caisse."));
        }

        if (await sessions.GetAsync(command.SessionId, user.Tenant, cancellationToken) is not null)
        {
            return Result<OpenRegisterSessionResult>.Success(new(command.SessionId, AlreadyProcessed: true));
        }

        if (await sessions.FindOpenForRegisterAsync(command.Register, user.Tenant, cancellationToken) is not null)
        {
            return Result<OpenRegisterSessionResult>.Failure(
                new ApplicationError(ErrorCodes.RegisterAlreadyOpen, "Cette caisse a déjà une session ouverte."));
        }

        try
        {
            var session = CashRegisterSession.Open(
                command.SessionId,
                new RegisterAssignment(user.Tenant, command.PointOfSale, command.Register, user.User),
                Money.Fcfa(command.OpeningFloatFcfa),
                command.OpenedAt);

            await sessions.AddAsync(session, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<OpenRegisterSessionResult>.Success(new(command.SessionId, AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<OpenRegisterSessionResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}
