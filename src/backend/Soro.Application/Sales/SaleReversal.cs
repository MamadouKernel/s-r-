using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Customers;
using Soro.Application.Identity;
using Soro.Application.Stock;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Application.Sales;

/// <summary>Ce que le serveur a calculé : de quoi dire au caissier « rendez 3 000 FCFA » ou « remboursez 1 500 FCFA par Wave ».</summary>
public sealed record ReversalResult(
    SaleReturnId Id,
    long TotalFcfa,
    long CashFcfa,
    long CreditFcfa,
    IReadOnlyList<RefundPart> Refund,
    bool AlreadyProcessed);

/// <summary>
/// L'opération inverse d'une vente, commune à l'annulation et au retour (docs/decisions/0020) : validation d'un gérant, calcul du remboursement,
/// retour en stock, argent du tiroir, dette du client, journal, le tout dans une seule transaction.
/// </summary>
public sealed class SaleReversal(
    ICurrentUser user,
    ISaleRepository sales,
    ISaleReturnRepository returns,
    ISessionRepository sessions,
    IProductRepository products,
    IStockMovementRepository stockMovements,
    ICustomerRepository customers,
    IUserRepository users,
    IPinHasher hasher,
    IApprovalSeal seal,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public sealed record Request(
        SaleId SaleId,
        SaleReturnId OperationId,
        ReturnKind Kind,
        IReadOnlyList<ReturnRequest>? Lines,
        bool Resellable,
        string Reason,
        RegisterId? Register,
        Approval? Approval);

    public async Task<Result<ReversalResult>> ExecuteAsync(Request request, CancellationToken cancellationToken)
    {
        var refusedAction = request.Kind == ReturnKind.Cancellation ? AuditActions.CancelRefused : AuditActions.ReturnRefused;
        var doneAction = request.Kind == ReturnKind.Cancellation ? AuditActions.SaleCancelled : AuditActions.SaleReturned;

        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return await RefuseAsync(request, refusedAction, "Ce profil ne peut pas annuler ni reprendre une vente.", cancellationToken);
        }

        var sale = await sales.GetAsync(request.SaleId, user.Tenant, cancellationToken);
        if (sale is null)
        {
            return Result<ReversalResult>.Failure(ApplicationError.NotFound("Vente introuvable."));
        }

        if (await returns.GetAsync(request.OperationId, user.Tenant, cancellationToken) is { } done)
        {
            return Result<ReversalResult>.Success(ToResult(done, alreadyProcessed: true));
        }

        var approval = await ApprovalCheck.CheckAsync(user, users, hasher, seal, clock, request.Approval, request.SaleId.Value, ApprovalPurposes.Reversal, cancellationToken);
        if (!approval.Approved)
        {
            return await RefuseAsync(request, refusedAction, "Un gérant doit valider : son identifiant et son code PIN sont nécessaires.", cancellationToken);
        }

        try
        {
            var result = await ReverseAsync(request, sale, doneAction, approval.ApprovedBy, cancellationToken);
            return result;
        }
        catch (DomainException exception)
        {
            return Result<ReversalResult>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
        catch (ConflictException)
        {
            return await returns.GetAsync(request.OperationId, user.Tenant, cancellationToken) is { } already
                ? Result<ReversalResult>.Success(ToResult(already, alreadyProcessed: true))
                : Result<ReversalResult>.Failure(new ApplicationError(
                    ErrorCodes.RuleViolation, "Une autre opération vient d'être faite sur cette vente : rechargez-la et recommencez."));
        }
    }

    private async Task<Result<ReversalResult>> ReverseAsync(Request request, Sale sale, string doneAction, UserId? approvedBy, CancellationToken cancellationToken)
    {
        var history = await returns.ListForSaleAsync(sale.Id, user.Tenant, cancellationToken);
        var plan = request.Kind == ReturnKind.Cancellation
            ? ReturnPlanner.PlanAll(sale, history, request.Resellable)
            : ReturnPlanner.Plan(sale, history, request.Lines ?? []);

        var session = plan.Cash > Money.Zero ? await FindRefundSessionAsync(request, sale, cancellationToken) : null;
        if (plan.Cash > Money.Zero && session is null)
        {
            return Result<ReversalResult>.Failure(ApplicationError.RuleViolation("Ouvrez la caisse pour rembourser en espèces."));
        }

        var now = clock.UtcNow;
        var saleReturn = SaleReturn.Record(request.OperationId, sale, request.Kind, now, user.User, approvedBy, request.Reason, plan, session?.Id);

        await TakeBackStockAsync(sale, plan, request, now, cancellationToken);
        await DischargeCreditAsync(sale, plan, cancellationToken);
        if (session is not null)
        {
            session.RecordCashRefund(plan.Cash);
            await sessions.UpdateAsync(session, cancellationToken);
        }

        if (request.Kind == ReturnKind.Cancellation)
        {
            await sales.UpdateAsync(sale.Cancel(user.User, request.Reason, now), cancellationToken);
        }

        await returns.AddAsync(saleReturn, cancellationToken);
        await audit.RecordAsync(
            new AuditEntry(user.Tenant, user.User, doneAction, sale.Id.Value, Detail(sale, saleReturn, request), now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<ReversalResult>.Success(ToResult(saleReturn, alreadyProcessed: false));
    }

    /// <summary>
    /// Les espèces sortent du tiroir de la session ouverte de la caisse qui rembourse ; sans caisse indiquée, de la session de la vente si elle est
    /// encore ouverte. Un caissier n'agit que sur sa propre session, un gérant sur toutes.
    /// </summary>
    private async Task<CashRegisterSession?> FindRefundSessionAsync(Request request, Sale sale, CancellationToken cancellationToken)
    {
        var session = request.Register is { } register
            ? await sessions.FindOpenForRegisterAsync(register, user.Tenant, cancellationToken)
            : await sessions.GetAsync(sale.SessionId, user.Tenant, cancellationToken);
        return session is { Status: SessionStatus.Open } && SessionAccess.CanAct(user, session) ? session : null;
    }

    private async Task TakeBackStockAsync(Sale sale, ReturnPlan plan, Request request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var kind = request.Kind == ReturnKind.Cancellation ? StockMovementKind.SaleCancellation : StockMovementKind.Return;
        foreach (var line in plan.Lines)
        {
            var product = await products.GetAsync(sale.Lines[line.LineIndex].ProductId, user.Tenant, cancellationToken);
            if (product is null)
            {
                continue;
            }

            var movements = product.TakeBack(line.Quantity, line.Resellable, kind, request.Reason, request.OperationId.Value, user.User, now);
            foreach (var movement in movements)
            {
                await stockMovements.AddAsync(movement, cancellationToken);
            }

            if (movements.Count > 0)
            {
                await products.UpdateAsync(product, cancellationToken);
            }
        }
    }

    private async Task DischargeCreditAsync(Sale sale, ReturnPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Credit == Money.Zero || sale.CustomerId is not { } customerId)
        {
            return;
        }

        var customer = await customers.GetAsync(customerId, user.Tenant, cancellationToken);
        if (customer is not null)
        {
            customer.Discharge(plan.Credit);
            await customers.UpdateAsync(customer, cancellationToken);
        }
    }

    private async Task<Result<ReversalResult>> RefuseAsync(Request request, string action, string message, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(new AuditEntry(user.Tenant, user.User, action, request.SaleId.Value, request.Reason, clock.UtcNow), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<ReversalResult>.Failure(ApplicationError.Forbidden(message));
    }

    private static string Detail(Sale sale, SaleReturn saleReturn, Request request)
    {
        var what = request.Kind == ReturnKind.Cancellation ? "Vente annulée" : "Retour";
        var parts = string.Join(", ", saleReturn.Refund.Select(part => $"{part.Amount} {Label(part.Method)}"));
        return $"{what} (ticket {sale.LocalSequence}) : {request.Reason.Trim()} — rendu {saleReturn.Total}" + (parts.Length > 0 ? $" ({parts})" : string.Empty);
    }

    private static string Label(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "en espèces",
        PaymentMethod.Credit => "de dette effacée",
        _ => $"par {method}",
    };

    private static ReversalResult ToResult(SaleReturn saleReturn, bool alreadyProcessed) =>
        new(
            saleReturn.Id,
            saleReturn.Total.Amount,
            saleReturn.Cash.Amount,
            Money.Sum(saleReturn.Refund.Where(part => part.Method == PaymentMethod.Credit).Select(part => part.Amount)).Amount,
            saleReturn.Refund,
            alreadyProcessed);
}

public sealed record ReturnSaleCommand(
    SaleId SaleId,
    SaleReturnId OperationId,
    IReadOnlyList<ReturnRequest> Lines,
    string Reason,
    RegisterId? Register = null,
    Approval? Approval = null);

/// <summary>Retour d'une partie d'une vente (docs/decisions/0020, ARR-02). Rejouable par identifiant d'opération.</summary>
public sealed class ReturnSaleHandler(SaleReversal reversal)
{
    public Task<Result<ReversalResult>> HandleAsync(ReturnSaleCommand command, CancellationToken cancellationToken = default) =>
        reversal.ExecuteAsync(
            new SaleReversal.Request(command.SaleId, command.OperationId, ReturnKind.Return, command.Lines, true, command.Reason, command.Register, command.Approval),
            cancellationToken);
}
