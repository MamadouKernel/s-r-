using Soro.Domain.Common;

namespace Soro.Domain.Sales;

/// <summary>
/// Règlement d'une partie d'une vente. <see cref="Amount"/> est la part imputée à la vente ;
/// la monnaie rendue (espèces) n'en fait pas partie.
/// </summary>
public sealed record Payment
{
    private Payment(PaymentId id, PaymentMethod method, Money amount, string? reference, Money change)
    {
        Id = id;
        Method = method;
        Amount = amount;
        Reference = reference;
        Change = change;
    }

    public PaymentId Id { get; }

    public PaymentMethod Method { get; }

    public Money Amount { get; }

    public string? Reference { get; }

    public Money Change { get; }

    public Money Tendered => Amount + Change;

    public static Payment Cash(PaymentId id, Money amount, Money tendered)
    {
        RequirePositive(amount);
        Ensure.That(tendered >= amount, "Le montant reçu est inférieur au montant à payer.");
        return new Payment(id, PaymentMethod.Cash, amount, reference: null, change: tendered - amount);
    }

    public static Payment MobileMoney(PaymentId id, PaymentMethod method, Money amount, string reference)
    {
        Ensure.That(method.IsMobileMoney(), "Ce moyen de paiement n'est pas du Mobile Money.");
        RequirePositive(amount);
        Ensure.That(
            !string.IsNullOrWhiteSpace(reference),
            "La référence de transaction Mobile Money est obligatoire (RG-04).");
        return new Payment(id, method, amount, reference.Trim(), Money.Zero);
    }

    public static Payment Credit(PaymentId id, Money amount)
    {
        RequirePositive(amount);
        return new Payment(id, PaymentMethod.Credit, amount, reference: null, change: Money.Zero);
    }

    /// <summary>Reconstruction depuis le stockage : les données ont déjà été validées à la création.</summary>
    internal static Payment Restore(PaymentId id, PaymentMethod method, Money amount, string? reference, Money change) =>
        new(id, method, amount, reference, change);

    private static void RequirePositive(Money amount) =>
        Ensure.That(amount > Money.Zero, "Le montant d'un paiement doit être supérieur à zéro.");
}
