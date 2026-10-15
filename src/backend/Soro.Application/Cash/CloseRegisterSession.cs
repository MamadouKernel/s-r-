using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Cash;

public sealed record CloseRegisterSessionCommand(SessionId SessionId, long CountedCashFcfa, DateTimeOffset At);

public sealed record CloseRegisterSessionResult(
    long ExpectedCashFcfa,
    long CountedCashFcfa,
    long VarianceFcfa,
    bool AlreadyProcessed);

/// <summary>Clôture de caisse (REQ-08). Rejouable avec le même comptage ; un autre comptage est refusé.</summary>
public sealed class CloseRegisterSessionHandler(
    ICurrentUser user,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CloseRegisterSessionResult>> HandleAsync(
        CloseRegisterSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return Result<CloseRegisterSessionResult>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas clôturer une caisse."));
        }

        var session = await sessions.GetAsync(command.SessionId, user.Tenant, cancellationToken);
        if (session is null)
        {
            return Result<CloseRegisterSessionResult>.Failure(ApplicationError.NotFound("Session de caisse introuvable."));
        }

        if (!SessionAccess.CanAct(user, session))
        {
            return Result<CloseRegisterSessionResult>.Failure(
                ApplicationError.Forbidden("Seul le caissier titulaire ou un gérant peut clôturer cette session."));
        }

        try
        {
            var counted = Money.Fcfa(command.CountedCashFcfa);
            if (session.Status == SessionStatus.Closed && session.CountedCash == counted)
            {
                return Result<CloseRegisterSessionResult>.Success(ToResult(session, alreadyProcessed: true));
            }

            session.Close(counted, command.At);
        }
        catch (DomainException exception)
        {
            return Result<CloseRegisterSessionResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await sessions.UpdateAsync(session, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<CloseRegisterSessionResult>.Success(ToResult(session, alreadyProcessed: false));
    }

    private static CloseRegisterSessionResult ToResult(CashRegisterSession session, bool alreadyProcessed) =>
        new(
            session.ExpectedCash.Amount,
            session.CountedCash!.Value.Amount,
            session.CashVariance!.Value,
            alreadyProcessed);
}
