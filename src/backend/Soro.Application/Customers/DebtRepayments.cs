using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Customers;
using Soro.Domain.Sales;

namespace Soro.Application.Customers;

public sealed record RecordRepaymentCommand(
    DebtRepaymentId Id,
    CustomerId Customer,
    long AmountFcfa,
    PaymentMethod Method,
    string? Reference);

public sealed record RepaymentRecorded(DebtRepaymentId Id, long DebtFcfa, bool AlreadyProcessed);

/// <summary>
/// Versement d'un client sur sa dette (CRD-05) : partiel, jamais plus que la dette, rejouable par identifiant, tracé. En ligne seulement : le serveur
/// est la source de la dette. Une référence Mobile Money ne sert qu'une fois par commerce et par opérateur, ventes et versements confondus.
/// </summary>
public sealed class RecordRepaymentHandler(
    ICurrentUser user,
    ICustomerRepository customers,
    IDebtRepaymentRepository repayments,
    ISaleRepository sales,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RepaymentRecorded>> HandleAsync(RecordRepaymentCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return Result<RepaymentRecorded>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas encaisser un versement."));
        }

        var customer = await customers.GetAsync(command.Customer, user.Tenant, cancellationToken);
        if (customer is null)
        {
            return Result<RepaymentRecorded>.Failure(ApplicationError.NotFound("Client introuvable."));
        }

        if (await repayments.ExistsAsync(command.Id, user.Tenant, cancellationToken))
        {
            return Result<RepaymentRecorded>.Success(new(command.Id, customer.Debt.Amount, AlreadyProcessed: true));
        }

        try
        {
            var now = clock.UtcNow;
            var repayment = DebtRepayment.Record(
                command.Id, user.Tenant, customer.Id, Money.Fcfa(command.AmountFcfa), command.Method, command.Reference, user.User, now);
            if (repayment.Reference is { } reference
                && (await repayments.IsReferenceUsedAsync(user.Tenant, repayment.Method, reference, cancellationToken)
                    || await sales.IsMobileMoneyReferenceUsedAsync(user.Tenant, repayment.Method, reference, cancellationToken)))
            {
                return Result<RepaymentRecorded>.Failure(new ApplicationError(
                    ErrorCodes.MobileMoneyReferenceUsed, $"La référence {reference} a déjà été utilisée."));
            }

            customer.Repay(repayment.Amount);
            await repayments.AddAsync(repayment, cancellationToken);
            await customers.UpdateAsync(customer, cancellationToken);
            await audit.RecordAsync(
                new AuditEntry(
                    user.Tenant, user.User, AuditActions.DebtRepaid, customer.Id.Value,
                    $"{customer.Name} : {repayment.Amount} ({RepaymentWording.Method(repayment.Method)}), reste {customer.Debt}", now),
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<RepaymentRecorded>.Success(new(repayment.Id, customer.Debt.Amount, AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<RepaymentRecorded>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

public sealed record RepaymentView(Guid Id, long AmountFcfa, PaymentMethod Method, string? Reference, Guid UserId, DateTimeOffset At);

public sealed record ListRepaymentsQuery(CustomerId Customer, int Limit);

/// <summary>Historique des versements d'un client (CRD-03) : propriétaire, gérant et comptable.</summary>
public sealed class ListRepaymentsHandler(ICurrentUser user, ICustomerRepository customers, IDebtRepaymentRepository repayments)
{
    public const int MaxLimit = 200;

    public async Task<Result<IReadOnlyList<RepaymentView>>> HandleAsync(ListRepaymentsQuery query, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanSeeRepayments(user.Role))
        {
            return Result<IReadOnlyList<RepaymentView>>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas consulter les versements."));
        }

        if (await customers.GetAsync(query.Customer, user.Tenant, cancellationToken) is null)
        {
            return Result<IReadOnlyList<RepaymentView>>.Failure(ApplicationError.NotFound("Client introuvable."));
        }

        var found = await repayments.ListForCustomerAsync(query.Customer, user.Tenant, Math.Clamp(query.Limit, 1, MaxLimit), cancellationToken);
        IReadOnlyList<RepaymentView> views = found
            .Select(item => new RepaymentView(item.Id.Value, item.Amount.Amount, item.Method, item.Reference, item.User.Value, item.At))
            .ToArray();
        return Result<IReadOnlyList<RepaymentView>>.Success(views);
    }
}

internal static class RepaymentWording
{
    public static string Method(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "espèces",
        PaymentMethod.OrangeMoney => "Orange Money",
        PaymentMethod.MtnMomo => "MTN MoMo",
        PaymentMethod.MoovMoney => "Moov Money",
        PaymentMethod.Wave => "Wave",
        _ => method.ToString(),
    };
}
