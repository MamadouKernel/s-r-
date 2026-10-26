using Soro.Application.Cash;
using Soro.Application.Common;
using Soro.Application.Customers;
using Soro.Application.Identity;
using Soro.Application.Settings;
using Soro.Application.Stock;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Customers;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Application.Sales;

/// <summary>Approbation d'un gérant : son identifiant ET son PIN, que le serveur vérifie (docs/decisions/0018).</summary>
public sealed record Approval(UserId Approver, string Pin);

public sealed record SaleLineInput(ProductId Product, string Name, int Quantity, long UnitPriceFcfa)
{
    /// <summary>
    /// Unité de la ligne (docs/decisions/0017). Une caisse qui ne la connaît pas ne l'envoie pas : sa ligne est lue en pièces, et refusée si le produit
    /// est vendu au poids ou à la mesure (jamais une quantité mal interprétée).
    /// </summary>
    public MeasureUnit? Unit { get; init; }
}

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
    Approval? DiscountApproval,
    Approval? CreditApproval = null,
    bool EnforceCreditCeiling = true,
    bool FromOfflineQueue = false);

public sealed record RecordSaleResult(SaleId SaleId, long NetTotalFcfa, long ChangeFcfa, bool AlreadyProcessed);

/// <summary>
/// Enregistrement d'une vente (REQ-02 à REQ-07). Rejouable : un identifiant déjà traité est acquitté
/// sans recréer la vente (RG-17). Les remises au-delà du seuil exigent un gérant (RG-03).
/// </summary>
public sealed class RecordSaleHandler(
    ICurrentUser user,
    ISessionRepository sessions,
    ISaleRepository sales,
    IProductRepository products,
    IStockMovementRepository stockMovements,
    ICustomerRepository customers,
    IShopSettings settings,
    IUserRepository users,
    IPinHasher hasher,
    IDebtRepaymentRepository repayments,
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
        var catalogue = await LoadProductsAsync(command, cancellationToken);
        var unitError = CheckUnits(command, catalogue);
        if (unitError is not null)
        {
            return Result<RecordSaleResult>.Failure(unitError);
        }

        var draft = WithCosts(ToDraft(command), catalogue);

        // Les inscriptions au journal d'une vente ne sont écrites qu'une fois la vente acceptée par la session : une vente refusée ne laisse
        // ni approbation, ni dépassement, ni dette derrière elle (docs/decisions/0018). Les refus, eux, s'écrivent aussitôt.
        var journal = new List<AuditEntry>();

        var priceError = CheckPrices(command, draft, catalogue, journal);
        if (priceError is not null)
        {
            return Result<RecordSaleResult>.Failure(priceError);
        }

        var discountError = await CheckDiscountAsync(command, draft, journal, cancellationToken);
        if (discountError is not null)
        {
            return Result<RecordSaleResult>.Failure(discountError);
        }

        var referenceError = await CheckMobileMoneyReferencesAsync(draft, cancellationToken);
        if (referenceError is not null)
        {
            return Result<RecordSaleResult>.Failure(referenceError);
        }

        var credit = await CheckCreditAsync(command, draft, journal, cancellationToken);
        if (credit.Error is not null)
        {
            return Result<RecordSaleResult>.Failure(credit.Error);
        }

        var sale = session.RecordSale(draft);
        if (credit.Charge is { } charge)
        {
            charge.Customer.Charge(charge.Amount);
            await customers.UpdateAsync(charge.Customer, cancellationToken);
        }

        foreach (var entry in journal)
        {
            await audit.RecordAsync(entry, cancellationToken);
        }

        await sales.AddAsync(sale, cancellationToken);
        await sessions.UpdateAsync(session, cancellationToken);
        await DecrementStockAsync(sale, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<RecordSaleResult>.Success(ToResult(sale, alreadyProcessed: false));
    }

    /// <summary>
    /// Une vente fait baisser le stock des produits suivis (STK-04). Elle ne se bloque jamais sur le stock, et les
    /// produits absents du catalogue sont ignorés. Les lignes d'un même produit sont regroupées en un mouvement.
    /// </summary>
    private async Task DecrementStockAsync(Sale sale, CancellationToken cancellationToken)
    {
        foreach (var group in sale.Lines.GroupBy(line => line.ProductId))
        {
            var product = await products.GetAsync(group.Key, user.Tenant, cancellationToken);
            var movement = product?.RecordSale(group.Sum(line => line.Quantity), sale.Id.Value, user.User, clock.UtcNow);
            if (movement is not null)
            {
                await stockMovements.AddAsync(movement, cancellationToken);
                await products.UpdateAsync(product!, cancellationToken);
            }
        }
    }

    /// <summary>Les produits des lignes, une seule lecture chacun : pour fixer les coûts et comparer les prix.</summary>
    private async Task<Dictionary<ProductId, Product?>> LoadProductsAsync(RecordSaleCommand command, CancellationToken cancellationToken)
    {
        var loaded = new Dictionary<ProductId, Product?>();
        foreach (var productId in command.Lines.Select(line => line.Product).Distinct())
        {
            loaded[productId] = await products.GetAsync(productId, user.Tenant, cancellationToken);
        }

        return loaded;
    }

    /// <summary>
    /// L'unité d'une ligne est celle de la fiche (docs/decisions/0017, VPO-09). Une caisse pas à jour n'envoie pas d'unité : sa ligne vaut « pièces »,
    /// ce qui, pour un produit au poids, ferait lire « 2 » comme 2 pièces au lieu de 2 grammes ; on la refuse avec un message clair.
    /// </summary>
    private static ApplicationError? CheckUnits(RecordSaleCommand command, Dictionary<ProductId, Product?> catalogue)
    {
        foreach (var line in command.Lines)
        {
            if (catalogue[line.Product] is not { } product || (line.Unit ?? MeasureUnit.Piece) == product.Unit)
            {
                continue;
            }

            return ApplicationError.RuleViolation(
                line.Unit is null
                    ? $"« {product.Name} » se vend au poids ou à la mesure : mettre à jour l'application de cette caisse, puis recommencer."
                    : $"L'unité de vente de « {product.Name} » ne correspond pas à sa fiche : actualisez le catalogue de la caisse.");
        }

        return null;
    }

    /// <summary>
    /// Fige sur chaque ligne le prix d'achat moyen du produit à cet instant (RG-11, docs/decisions/0007) : la marge d'une vente ne bougera plus
    /// quand les prix des fournisseurs changeront. Un produit inconnu du commerce n'a pas de coût.
    /// </summary>
    private static SaleDraft WithCosts(SaleDraft draft, Dictionary<ProductId, Product?> catalogue) =>
        draft with
        {
            Lines = draft.Lines
                .Select(line => SaleLine.Create(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, catalogue[line.ProductId]?.PurchasePrice, line.Unit))
                .ToArray(),
        };

    /// <summary>
    /// Un prix de ligne <b>inférieur</b> à celui du catalogue est une perte possible (docs/decisions/0018). Envoyée en ligne par un profil qui n'est
    /// ni propriétaire ni gérant, la vente est refusée ; venue de la file hors ligne, elle a eu lieu et s'enregistre, avec une inscription au journal
    /// pour que le propriétaire la voie. Un prix supérieur, ou un produit inconnu, ne dit rien.
    /// </summary>
    private ApplicationError? CheckPrices(
        RecordSaleCommand command,
        SaleDraft draft,
        Dictionary<ProductId, Product?> catalogue,
        List<AuditEntry> journal)
    {
        var lowered = draft.Lines
            .Where(line => catalogue[line.ProductId] is { } product && line.UnitPrice.Amount < product.SalePrice.Amount)
            .Select(line => $"{line.ProductName} à {line.UnitPrice} au lieu de {catalogue[line.ProductId]!.SalePrice}")
            .ToArray();
        if (lowered.Length == 0)
        {
            return null;
        }

        if (!command.FromOfflineQueue && !RolePolicy.IsManager(user.Role))
        {
            return ApplicationError.Forbidden("Seul un gérant peut vendre à un prix inférieur à celui du catalogue.");
        }

        var shown = string.Join(" ; ", lowered.Take(3));
        var more = lowered.Length > 3 ? $" (+{lowered.Length - 3} autres)" : string.Empty;
        journal.Add(new AuditEntry(user.Tenant, user.User, AuditActions.SalePriceLowered, command.Id.Value, $"Vente à prix réduit : {shown}{more}", clock.UtcNow));
        return null;
    }

    private sealed record CreditCharge(Customer Customer, Money Amount);

    private sealed record CreditCheck(ApplicationError? Error, CreditCharge? Charge);

    /// <summary>
    /// Vente à crédit (CRD-04, RG-06) : vérifie le client et le plafond, sans rien modifier. Le crédit s'ajoute à la dette du client, dans la même
    /// transaction que la vente, <b>après</b> que la session a accepté la vente (docs/decisions/0018). Au-delà du plafond, un gérant doit valider
    /// (son PIN) ; une vente venue de la synchronisation a déjà eu lieu, on l'enregistre toujours et on signale le dépassement.
    /// </summary>
    private async Task<CreditCheck> CheckCreditAsync(RecordSaleCommand command, SaleDraft draft, List<AuditEntry> journal, CancellationToken cancellationToken)
    {
        var credit = Money.Sum(draft.Payments.Where(payment => payment.Method == PaymentMethod.Credit).Select(payment => payment.Amount));
        if (credit == Money.Zero || draft.Customer is not { } customerId)
        {
            return new(null, null); // sans client, le domaine refuse la vente à crédit avec son propre message
        }

        var customer = await customers.GetAsync(customerId, user.Tenant, cancellationToken);
        if (customer is null)
        {
            return new(ApplicationError.NotFound("Client introuvable : la vente à crédit ne peut pas être enregistrée."), null);
        }

        if (!customer.IsActive)
        {
            return new(ApplicationError.RuleViolation($"Le client {customer.Name} est désactivé : il ne peut plus acheter à crédit."), null);
        }

        if (customer.WouldExceedCeiling(credit))
        {
            var detail = $"{customer.Name} : dette {customer.Debt}, plafond {customer.CreditCeiling}, crédit demandé {credit}";
            if (await IsApprovedAsync(command.CreditApproval, cancellationToken))
            {
                journal.Add(CreditEntry(AuditActions.CreditApproved, command, detail));
            }
            else if (command.EnforceCreditCeiling)
            {
                await audit.RecordAsync(CreditEntry(AuditActions.CreditRefused, command, detail), cancellationToken);
                await unitOfWork.CommitAsync(cancellationToken);
                return new(
                    new ApplicationError(
                        ErrorCodes.CreditLimitExceeded,
                        $"{customer.Name} a atteint son plafond de crédit ({customer.CreditCeiling}) : dette {customer.Debt}, il reste {customer.AvailableCredit}. "
                        + "La validation d'un gérant est nécessaire."),
                    null);
            }
            else
            {
                journal.Add(CreditEntry(AuditActions.CreditExceeded, command, detail));
            }
        }

        return new(null, new CreditCharge(customer, credit));
    }

    private AuditEntry CreditEntry(string action, RecordSaleCommand command, string detail) =>
        new(user.Tenant, user.User, action, command.Id.Value, detail, clock.UtcNow);

    private async Task<ApplicationError?> CheckDiscountAsync(
        RecordSaleCommand command,
        SaleDraft draft,
        List<AuditEntry> journal,
        CancellationToken cancellationToken)
    {
        var gross = Money.Sum(draft.Lines.Select(line => line.Total));
        var shop = await settings.GetAsync(user.Tenant, cancellationToken);
        if (draft.Discount.Amount * 100 <= gross.Amount * shop.DiscountApprovalThresholdPercent)
        {
            return null;
        }

        if (!await IsApprovedAsync(command.DiscountApproval, cancellationToken))
        {
            await audit.RecordAsync(Entry(AuditActions.DiscountRefused, command, draft.Discount), cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return ApplicationError.Forbidden(
                $"Une remise supérieure à {shop.DiscountApprovalThresholdPercent} % exige la validation d'un gérant.");
        }

        journal.Add(Entry(AuditActions.DiscountApproved, command, draft.Discount));
        return null;
    }

    /// <summary>
    /// Un gérant qui vend lui-même n'a rien à prouver. Sinon l'approbation exige l'identifiant d'un gérant actif ET son PIN, vérifié comme à la
    /// connexion (échecs comptés, compte verrouillé après cinq) : un identifiant seul ne prouve rien. Le propriétaire n'a pas de PIN : il approuve
    /// en vendant lui-même sur la caisse.
    /// </summary>
    private async Task<bool> IsApprovedAsync(Approval? approval, CancellationToken cancellationToken)
    {
        if (RolePolicy.IsManager(user.Role))
        {
            return true;
        }

        if (approval is null)
        {
            return false;
        }

        var approver = await users.GetAsync(approval.Approver, user.Tenant, cancellationToken);
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

    private async Task<ApplicationError?> CheckMobileMoneyReferencesAsync(SaleDraft draft, CancellationToken cancellationToken)
    {
        foreach (var payment in draft.Payments.Where(payment => payment.Method.IsMobileMoney()))
        {
            if (await sales.IsMobileMoneyReferenceUsedAsync(user.Tenant, payment.Method, payment.Reference!, cancellationToken)
                || await repayments.IsReferenceUsedAsync(user.Tenant, payment.Method, payment.Reference!, cancellationToken))
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
                .Select(line => SaleLine.Create(line.Product, line.Name, line.Quantity, Money.Fcfa(line.UnitPriceFcfa), null, line.Unit ?? MeasureUnit.Piece))
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
