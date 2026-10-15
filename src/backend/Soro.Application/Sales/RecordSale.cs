using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Settings;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Identity;

namespace Soro.Application.Sales;

public sealed record SaleLineInput(ProductId Product, string Name, int Quantity, long UnitPriceFcfa);

public sealed record PaymentInput(
    PaymentId Id,
    PaymentMethod Method,
    long AmountFcfa,
    long? TenderedFcfa,
    string? Reference);

public sealed record RecordSaleCommand(
    SaleId Id,
    SessionId Session,
    long LocalSequence,
    DateTimeOffset At,
    IReadOnlyList<SaleLineInput> Lines,
    long DiscountFcfa,
    IReadOnlyList<PaymentInput> Payments,
    CustomerId? Customer,
    UserId? DiscountApprover);

public sealed record RecordSaleResult(SaleId SaleId, long NetTotalFcfa, long ChangeFcfa, bool AlreadyProcessed);

/// <summary>
/// Enregistrement d'une vente (REQ-02 à REQ-07). Rejouable : un identifiant déjà traité est acquitté
/// sans recréer la vente (RG-17). Les remises au-delà du seuil exigent un gérant (RG-03).
/// </summary>
public sealed class RecordSaleHandler(
    ICurrentUser user,
    ISessionRepository sessions,
    ISaleRepository sales,
    IShopSettings settings,
    IUserDirectory directory,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RecordSaleResult>> HandleAsync(
        RecordSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return Result<RecordSaleResult>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas encaisser."));
        }

        var session = await sessions.GetAsync(command.Session, user.Tenant, cancellationToken);
        if (session is null)
        {
            return Result<RecordSaleResult>.Failure(ApplicationError.NotFound("Session de caisse introuvable."));
        }

        if (!SessionAccess.CanAct(user, session))
        {
            return Result<RecordSaleResult>.Failure(
                ApplicationError.Forbidden("Seul le caissier titulaire ou un gérant peut vendre sur cette session."));
        }

        var existing = await sales.GetAsync(command.Id, user.Tenant, cancellationToken);
        if (existing is not null)
        {
            return Result<RecordSaleResult>.Success(ToResult(existing, alreadyProcessed: true));
        }

        try
        {
            return await RecordAsync(command, session, cancellationToken);
        }
        catch (DomainException exception)
        {
            return Result<RecordSaleResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }

    private async Task<Result<RecordSaleResult>> RecordAsync(
        RecordSaleCommand command,
        CashRegisterSession session,
        CancellationToken cancellationToken)
    {
        var draft = ToDraft(command);

        var discountError = await CheckDiscountAsync(command, draft, cancellationToken);
        if (discountError is not null)
        {
            return Result<RecordSaleResult>.Failure(discountError);
        }

        var referenceError = await CheckMobileMoneyReferencesAsync(draft, cancellationToken);
        if (referenceError is not null)
        {
            return Result<RecordSaleResult>.Failure(referenceError);
        }

        var sale = session.RecordSale(draft);
        await sales.AddAsync(sale, cancellationToken);
        await sessions.UpdateAsync(session, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<RecordSaleResult>.Success(ToResult(sale, alreadyProcessed: false));
    }

    private async Task<ApplicationError?> CheckDiscountAsync(
        RecordSaleCommand command,
        SaleDraft draft,
        CancellationToken cancellationToken)
    {
        var gross = Money.Sum(draft.Lines.Select(line => line.Total));
        var shop = await settings.GetAsync(user.Tenant, cancellationToken);
        if (draft.Discount.Amount * 100 <= gross.Amount * shop.DiscountApprovalThresholdPercent)
        {
            return null;
        }

        var approver = RolePolicy.IsManager(user.Role) ? user.User : command.DiscountApprover;
        if (approver is null || !await IsManagerAsync(approver.Value, cancellationToken))
        {
            await audit.RecordAsync(Entry(AuditActions.DiscountRefused, command, draft.Discount), cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return ApplicationError.Forbidden(
                $"Une remise supérieure à {shop.DiscountApprovalThresholdPercent} % exige la validation d'un gérant.");
        }

        await audit.RecordAsync(Entry(AuditActions.DiscountApproved, command, draft.Discount), cancellationToken);
        return null;
    }

    private async Task<bool> IsManagerAsync(UserId candidate, CancellationToken cancellationToken)
    {
        if (candidate == user.User)
        {
            return RolePolicy.IsManager(user.Role);
        }

        var role = await directory.GetRoleAsync(candidate, user.Tenant, cancellationToken);
        return role is { } found && RolePolicy.IsManager(found);
    }

    private async Task<ApplicationError?> CheckMobileMoneyReferencesAsync(SaleDraft draft, CancellationToken cancellationToken)
    {
        foreach (var payment in draft.Payments.Where(payment => payment.Method.IsMobileMoney()))
        {
            if (await sales.IsMobileMoneyReferenceUsedAsync(user.Tenant, payment.Method, payment.Reference!, cancellationToken))
            {
                return new ApplicationError(
                    ErrorCodes.MobileMoneyReferenceUsed,
                    $"La référence {payment.Reference} a déjà été utilisée.");
            }
        }

        return null;
    }

    private AuditEntry Entry(string action, RecordSaleCommand command, Money discount) =>
        new(user.Tenant, user.User, action, command.Id.Value, $"Remise de {discount}", clock.UtcNow);

    private static SaleDraft ToDraft(RecordSaleCommand command) =>
        new(
            command.Id,
            command.LocalSequence,
            command.At,
            command.Lines
                .Select(line => SaleLine.Create(line.Product, line.Name, line.Quantity, Money.Fcfa(line.UnitPriceFcfa)))
                .ToArray(),
            Money.Fcfa(command.DiscountFcfa),
            command.Payments.Select(ToPayment).ToArray(),
            command.Customer);

    private static Payment ToPayment(PaymentInput input) => input.Method switch
    {
        PaymentMethod.Cash => Payment.Cash(
            input.Id, Money.Fcfa(input.AmountFcfa), Money.Fcfa(input.TenderedFcfa ?? input.AmountFcfa)),
        PaymentMethod.Credit => Payment.Credit(input.Id, Money.Fcfa(input.AmountFcfa)),
        _ => Payment.MobileMoney(input.Id, input.Method, Money.Fcfa(input.AmountFcfa), input.Reference ?? string.Empty),
    };

    private static RecordSaleResult ToResult(Sale sale, bool alreadyProcessed) =>
        new(sale.Id, sale.NetTotal.Amount, sale.Payments.Sum(payment => payment.Change.Amount), alreadyProcessed);
}
