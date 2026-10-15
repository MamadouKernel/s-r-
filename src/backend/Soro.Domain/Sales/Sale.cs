using Soro.Domain.Cash;
using Soro.Domain.Common;

namespace Soro.Domain.Sales;

/// <summary>
/// Vente validée. Immuable : l'annulation produit une nouvelle version et ne supprime rien (RG-02).
/// Une vente ne se crée que par <see cref="CashRegisterSession.RecordSale"/>, ce qui garantit
/// qu'elle est rattachée à une session ouverte (RG-01) et attribuée à une personne (RG-19).
/// </summary>
public sealed record Sale
{
    private Sale(
        SessionId sessionId,
        RegisterAssignment assignment,
        SaleDraft draft,
        SaleLine[] lines,
        Payment[] payments,
        Money grossTotal)
    {
        Id = draft.Id;
        SessionId = sessionId;
        Assignment = assignment;
        LocalSequence = draft.LocalSequence;
        RecordedAt = draft.At;
        Lines = Array.AsReadOnly(lines);
        Discount = draft.Discount;
        Payments = Array.AsReadOnly(payments);
        CustomerId = draft.Customer;
        GrossTotal = grossTotal;
        NetTotal = grossTotal - draft.Discount;
    }

    private Sale(
        SaleId id,
        SessionId sessionId,
        RegisterAssignment assignment,
        long localSequence,
        DateTimeOffset recordedAt,
        SaleLine[] lines,
        Money discount,
        Payment[] payments,
        CustomerId? customerId,
        Money grossTotal,
        Money netTotal,
        SaleStatus status,
        Cancellation? cancellation)
    {
        Id = id;
        SessionId = sessionId;
        Assignment = assignment;
        LocalSequence = localSequence;
        RecordedAt = recordedAt;
        Lines = Array.AsReadOnly(lines);
        Discount = discount;
        Payments = Array.AsReadOnly(payments);
        CustomerId = customerId;
        GrossTotal = grossTotal;
        NetTotal = netTotal;
        Status = status;
        Cancellation = cancellation;
    }

    public SaleId Id { get; }

    public SessionId SessionId { get; }

    public RegisterAssignment Assignment { get; }

    public long LocalSequence { get; }

    public DateTimeOffset RecordedAt { get; }

    public IReadOnlyList<SaleLine> Lines { get; }

    public Money Discount { get; }

    public IReadOnlyList<Payment> Payments { get; }

    public CustomerId? CustomerId { get; }

    public Money GrossTotal { get; }

    public Money NetTotal { get; }

    public SaleStatus Status { get; private init; } = SaleStatus.Validated;

    public Cancellation? Cancellation { get; private init; }

    /// <summary>Espèces conservées en caisse, monnaie rendue déduite.</summary>
    public Money CashReceived =>
        Money.Sum(Payments.Where(payment => payment.Method == PaymentMethod.Cash).Select(payment => payment.Amount));

    public Sale Cancel(UserId cancelledBy, string reason, DateTimeOffset at)
    {
        Ensure.That(Status == SaleStatus.Validated, "Cette vente est déjà annulée.");
        Ensure.That(!string.IsNullOrWhiteSpace(reason), "Le motif d'annulation est obligatoire.");
        Ensure.That(at >= RecordedAt, "Une annulation ne peut pas précéder la vente.");

        return this with
        {
            Status = SaleStatus.Cancelled,
            Cancellation = new Cancellation(cancelledBy, reason.Trim(), at),
        };
    }

    internal static Sale Record(SessionId sessionId, RegisterAssignment assignment, SaleDraft draft)
    {
        var lines = draft.Lines.ToArray();
        var payments = draft.Payments.ToArray();

        Ensure.That(lines.Length > 0, "Une vente doit contenir au moins une ligne.");
        Ensure.That(payments.Length > 0, "Une vente doit avoir au moins un paiement.");

        var grossTotal = Money.Sum(lines.Select(line => line.Total));
        Ensure.That(draft.Discount <= grossTotal, "La remise ne peut pas dépasser le total de la vente.");
        Ensure.That(draft.Discount < grossTotal, "Le total net d'une vente doit être supérieur à zéro.");

        var netTotal = grossTotal - draft.Discount;
        Ensure.That(
            Money.Sum(payments.Select(payment => payment.Amount)) == netTotal,
            "La somme des paiements doit être égale au total net (RG-20).");
        Ensure.That(
            draft.Customer is not null || payments.All(payment => payment.Method != PaymentMethod.Credit),
            "Une vente à crédit exige un client.");
        Ensure.That(
            !HasDuplicateMobileMoneyReference(payments),
            "Une même référence Mobile Money ne peut pas servir deux fois (RG-04).");

        return new Sale(sessionId, assignment, draft, lines, payments, grossTotal);
    }

    /// <summary>Reconstruction depuis le stockage : les données ont déjà été validées à la création.</summary>
    internal static Sale Restore(
        SaleId id,
        SessionId sessionId,
        RegisterAssignment assignment,
        long localSequence,
        DateTimeOffset recordedAt,
        IEnumerable<SaleLine> lines,
        Money discount,
        IEnumerable<Payment> payments,
        CustomerId? customerId,
        Money grossTotal,
        Money netTotal,
        SaleStatus status,
        Cancellation? cancellation) =>
        new(
            id,
            sessionId,
            assignment,
            localSequence,
            recordedAt,
            lines.ToArray(),
            discount,
            payments.ToArray(),
            customerId,
            grossTotal,
            netTotal,
            status,
            cancellation);

    private static bool HasDuplicateMobileMoneyReference(IEnumerable<Payment> payments) =>
        payments
            .Where(payment => payment.Method.IsMobileMoney())
            .GroupBy(payment => payment.Reference, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
}
