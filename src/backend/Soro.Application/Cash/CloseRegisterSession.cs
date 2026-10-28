using System.Globalization;
using System.Text.Json;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Settings;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Cash;

/// <param name="Recount">Le second comptage, définitif, demandé après un écart au-delà de la tolérance.</param>
/// <param name="Denominations">Le détail par coupures (valeur → nombre), facultatif ; sa somme doit égaler le total compté.</param>
public sealed record CloseRegisterSessionCommand(
    SessionId SessionId,
    long CountedCashFcfa,
    DateTimeOffset At,
    bool Recount = false,
    IReadOnlyDictionary<int, int>? Denominations = null);

/// <summary>
/// Le résultat d'un comptage. Le montant attendu et l'écart ne sont rendus qu'à un gérant ou au propriétaire : un caissier compte à l'aveugle
/// (docs/decisions/0021).
/// </summary>
public sealed record CloseRegisterSessionResult(
    bool Closed,
    bool NeedsRecount,
    long CountedCashFcfa,
    long? ExpectedCashFcfa,
    long? VarianceFcfa,
    bool AlreadyProcessed);

/// <summary>
/// Clôture de caisse à l'aveugle (REQ-08, docs/specs/cloture-sorties-rapport CLO-04, CLO-05). Un comptage trop éloigné de l'attendu demande un
/// recomptage définitif ; un écart au-delà de la tolérance, manque ou excédent, est inscrit au journal au nom du caissier. Rejouable avec le même
/// comptage ; un autre comptage sur une session close est refusé.
/// </summary>
public sealed class CloseRegisterSessionHandler(
    ICurrentUser user,
    ISessionRepository sessions,
    IShopSettings settings,
    IAuditTrail audit,
    IClock clock,
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

        var shop = await settings.GetAsync(user.Tenant, cancellationToken);
        CountOutcome outcome;
        try
        {
            var counted = Money.Fcfa(command.CountedCashFcfa);
            if (session.Status == SessionStatus.Closed && session.CountedCash == counted)
            {
                return Result<CloseRegisterSessionResult>.Success(ToResult(session, CountOutcome.Closed, alreadyProcessed: true));
            }

            if (DetailProblem(command) is { } problem)
            {
                return Result<CloseRegisterSessionResult>.Failure(ApplicationError.RuleViolation(problem));
            }

            outcome = session.Count(counted, command.At, Money.Fcfa(shop.CashVarianceToleranceFcfa), command.Recount, Detail(command));
        }
        catch (DomainException exception)
        {
            return Result<CloseRegisterSessionResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await sessions.UpdateAsync(session, cancellationToken);
        if (outcome == CountOutcome.Closed && Math.Abs(session.CashVariance!.Value) > shop.CashVarianceToleranceFcfa)
        {
            await audit.RecordAsync(
                new AuditEntry(user.Tenant, session.Assignment.Cashier, AuditActions.SessionVariance, session.Id.Value, VarianceDetail(session), clock.UtcNow),
                cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Result<CloseRegisterSessionResult>.Success(ToResult(session, outcome, alreadyProcessed: false));
    }

    private static string? DetailProblem(CloseRegisterSessionCommand command) =>
        command.Denominations is { Count: > 0 } && command.Denominations.Sum(item => (long)item.Key * item.Value) != command.CountedCashFcfa
            ? "Le détail des billets et des pièces ne correspond pas au montant compté."
            : null;

    private static string? Detail(CloseRegisterSessionCommand command) =>
        command.Denominations is { Count: > 0 }
            ? JsonSerializer.Serialize(command.Denominations.OrderByDescending(item => item.Key).ToDictionary(item => item.Key.ToString(CultureInfo.InvariantCulture), item => item.Value))
            : null;

    private static string VarianceDetail(CashRegisterSession session)
    {
        var variance = session.CashVariance!.Value;
        var kind = variance < 0 ? "manque" : "excédent";
        return $"Écart de caisse : {kind} de {Money.Fcfa(Math.Abs(variance))} (caisse ouverte le {session.OpenedAt.ToString("dd/MM 'à' HH:mm", CultureInfo.InvariantCulture)}).";
    }

    private CloseRegisterSessionResult ToResult(CashRegisterSession session, CountOutcome outcome, bool alreadyProcessed)
    {
        var sees = RolePolicy.IsManager(user.Role);
        var closed = outcome == CountOutcome.Closed;
        var counted = closed ? session.CountedCash!.Value.Amount : session.FirstCount!.Value.Amount;
        return new(
            closed,
            !closed,
            counted,
            sees && closed ? session.ExpectedCash.Amount : null,
            sees && closed ? session.CashVariance : null,
            alreadyProcessed);
    }
}
