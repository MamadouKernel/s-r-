using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Domain.Customers;

/// <summary>
/// Versement d'un client sur sa dette : qui, quand, combien, comment. Jamais modifié ni supprimé (une erreur se corrige par une écriture motivée, hors lot).
/// </summary>
public sealed class DebtRepayment
{
    private DebtRepayment(
        DebtRepaymentId id,
        TenantId tenant,
        CustomerId customer,
        Money amount,
        PaymentMethod method,
        string? reference,
        UserId user,
        DateTimeOffset at)
    {
        Id = id;
        Tenant = tenant;
        Customer = customer;
        Amount = amount;
        Method = method;
        Reference = reference;
        User = user;
        At = at;
    }

    public DebtRepaymentId Id { get; }

    public TenantId Tenant { get; }

    public CustomerId Customer { get; }

    public Money Amount { get; }

    public PaymentMethod Method { get; }

    /// <summary>Référence de la transaction Mobile Money ; <c>null</c> pour des espèces.</summary>
    public string? Reference { get; }

    public UserId User { get; }

    public DateTimeOffset At { get; }

    public static DebtRepayment Record(
        DebtRepaymentId id,
        TenantId tenant,
        CustomerId customer,
        Money amount,
        PaymentMethod method,
        string? reference,
        UserId user,
        DateTimeOffset at)
    {
        Ensure.That(amount > Money.Zero, "Le versement doit être supérieur à zéro.");
        Ensure.That(method != PaymentMethod.Credit, "Un versement ne peut pas se faire à crédit.");
        string? cleanReference = null;
        if (method.IsMobileMoney())
        {
            cleanReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
            Ensure.That(cleanReference is not null, "La référence de la transaction Mobile Money est obligatoire.");
        }

        return new DebtRepayment(id, tenant, customer, amount, method, cleanReference, user, at);
    }

    /// <summary>Reconstruction depuis le stockage : l'état a déjà été validé à l'enregistrement.</summary>
    internal static DebtRepayment Restore(
        DebtRepaymentId id,
        TenantId tenant,
        CustomerId customer,
        Money amount,
        PaymentMethod method,
        string? reference,
        UserId user,
        DateTimeOffset at) =>
        new(id, tenant, customer, amount, method, reference, user, at);
}
