using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Subscription;

namespace Soro.Application.Subscription;

public sealed record SubscriptionPaymentView(
    Guid Id,
    SubscriptionPeriod Period,
    SubscriptionMethod Method,
    string Reference,
    long AmountFcfa,
    SubscriptionPaymentStatus Status,
    DateTimeOffset DeclaredAt,
    DateTimeOffset? DecidedAt,
    string? Reason,
    DateTimeOffset? CoveredTo);

/// <summary>L'état d'abonnement ; prix, numéros et paiements ne sont fournis qu'à ceux qui gèrent la boutique (docs/specs/abonnement ABO-03).</summary>
public sealed record SubscriptionView(
    SubscriptionStatus Status,
    DateTimeOffset EndsAt,
    DateTimeOffset ReadOnlyFrom,
    int DaysLeft,
    bool IsReadOnly,
    long? MonthlyPriceFcfa,
    long? AnnualPriceFcfa,
    IReadOnlyDictionary<SubscriptionMethod, string>? PayNumbers,
    IReadOnlyList<SubscriptionPaymentView>? Payments);

public sealed class GetSubscriptionHandler(
    ICurrentUser user,
    ICommerceRepository commerces,
    ISubscriptionPaymentRepository payments,
    ISubscriptionSettings settings,
    IClock clock)
{
    public async Task<Result<SubscriptionView>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var commerce = await commerces.GetAsync(user.Tenant, cancellationToken);
        if (commerce is null)
        {
            return Result<SubscriptionView>.Failure(ApplicationError.NotFound("Commerce introuvable."));
        }

        var state = commerce.SubscriptionStateAt(clock.UtcNow);
        if (!RolePolicy.IsManager(user.Role))
        {
            return Result<SubscriptionView>.Success(new(state.Status, state.EndsAt, state.ReadOnlyFrom, state.DaysLeft, state.IsReadOnly, null, null, null, null));
        }

        var history = await payments.ListAsync(user.Tenant, cancellationToken);
        return Result<SubscriptionView>.Success(
            new(
                state.Status,
                state.EndsAt,
                state.ReadOnlyFrom,
                state.DaysLeft,
                state.IsReadOnly,
                settings.MonthlyPriceFcfa,
                settings.AnnualPriceFcfa,
                settings.PayNumbers,
                history.Select(payment => new SubscriptionPaymentView(
                    payment.Id.Value, payment.Period, payment.Method, payment.Reference, payment.AmountFcfa, payment.Status,
                    payment.DeclaredAt, payment.DecidedAt, payment.Reason, payment.CoveredTo)).ToArray()));
    }
}

public sealed record DeclareSubscriptionPaymentCommand(
    SubscriptionPaymentId Id,
    SubscriptionPeriod Period,
    SubscriptionMethod Method,
    string Reference,
    long AmountFcfa);

public sealed record DeclareSubscriptionPaymentResult(SubscriptionPaymentId Id, bool AlreadyProcessed);

/// <summary>Le propriétaire déclare un paiement Mobile Money ; l'équipe Sôrô le valide ensuite (ABO-04). Rejouable par identifiant (RG-17).</summary>
public sealed class DeclareSubscriptionPaymentHandler(
    ICurrentUser user,
    ISubscriptionPaymentRepository payments,
    ISubscriptionSettings settings,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<DeclareSubscriptionPaymentResult>> HandleAsync(
        DeclareSubscriptionPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (user.Role != Domain.Identity.Role.Owner)
        {
            return Result<DeclareSubscriptionPaymentResult>.Failure(ApplicationError.Forbidden("Seul le propriétaire peut déclarer un paiement."));
        }

        if (await payments.GetAsync(command.Id, user.Tenant, cancellationToken) is not null)
        {
            return Result<DeclareSubscriptionPaymentResult>.Success(new(command.Id, AlreadyProcessed: true));
        }

        var price = command.Period == SubscriptionPeriod.Annual ? settings.AnnualPriceFcfa : settings.MonthlyPriceFcfa;
        if (command.AmountFcfa < price)
        {
            return Result<DeclareSubscriptionPaymentResult>.Failure(
                ApplicationError.RuleViolation($"Le montant est trop bas : l'abonnement {(command.Period == SubscriptionPeriod.Annual ? "annuel" : "mensuel")} coûte {Money.Fcfa(price)}."));
        }

        try
        {
            var payment = SubscriptionPayment.Declare(
                command.Id, user.Tenant, command.Period, command.Method, command.Reference, command.AmountFcfa, clock.UtcNow);
            if (await payments.IsReferenceUsedAsync(payment.Method, payment.ReferenceKey, cancellationToken))
            {
                return Result<DeclareSubscriptionPaymentResult>.Failure(
                    new ApplicationError(ErrorCodes.MobileMoneyReferenceUsed, "Cette référence de transaction a déjà été utilisée. Vérifiez-la dans le message de votre opérateur."));
            }

            await payments.AddAsync(payment, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant,
                    user.User,
                    AuditActions.SubscriptionPaymentDeclared,
                    payment.Id.Value,
                    $"{payment.Period} · {payment.Method} · {Money.Fcfa(payment.AmountFcfa)}",
                    clock.UtcNow),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<DeclareSubscriptionPaymentResult>.Success(new(command.Id, AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<DeclareSubscriptionPaymentResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

/// <summary>L'état d'abonnement d'un commerce, pour le contrôle d'accès de l'API (ABO-07). <c>null</c> : commerce inconnu.</summary>
public sealed class GetSubscriptionStateHandler(ICommerceRepository commerces, IClock clock)
{
    public async Task<SubscriptionState?> HandleAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
        (await commerces.GetAsync(tenant, cancellationToken))?.SubscriptionStateAt(clock.UtcNow);
}

/// <summary>
/// En lecture seule, une synchronisation de ventes faites hors ligne est acceptée (elles ont eu lieu) mais signalée au journal, pour que le
/// propriétaire voie ce qui s'est passé (ABO-07).
/// </summary>
public sealed class RecordExpiredSyncHandler(ICurrentUser user, IAuditTrail audit, IClock clock, IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(string path, CancellationToken cancellationToken = default)
    {
        await audit.RecordAsync(
            new AuditEntry(
                user.Tenant,
                user.User,
                AuditActions.SubscriptionExpiredSale,
                user.User.Value,
                $"Données reçues après la fin de l'abonnement ({path}).",
                clock.UtcNow),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
