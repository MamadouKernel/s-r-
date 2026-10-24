using Soro.Domain.Common;

namespace Soro.Domain.Customers;

/// <summary>
/// Client du commerce, avec son plafond de crédit et sa dette (docs/specs/clients-credit, ADR 0008). La dette est une valeur tenue par le serveur
/// dans la transaction de chaque vente à crédit et de chaque versement ; elle ne se saisit jamais. Un client ne se supprime pas : il se désactive,
/// sans dette.
/// </summary>
public sealed class Customer
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    private Customer(CustomerId id, TenantId tenant, string name, PhoneNumber? phone, Money creditCeiling, DateTimeOffset createdAt)
    {
        Id = id;
        Tenant = tenant;
        Name = name;
        Phone = phone;
        CreditCeiling = creditCeiling;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public CustomerId Id { get; }

    public TenantId Tenant { get; }

    public string Name { get; private set; }

    public PhoneNumber? Phone { get; private set; }

    public Money CreditCeiling { get; private set; }

    public Money Debt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsActive { get; private set; }

    /// <summary>Ce que le client peut encore prendre à crédit : jamais négatif, même si le plafond a été baissé sous la dette.</summary>
    public Money AvailableCredit => CreditCeiling > Debt ? CreditCeiling - Debt : Money.Zero;

    public static Customer Create(CustomerId id, TenantId tenant, string name, string? phone, Money creditCeiling, DateTimeOffset createdAt) =>
        new(id, tenant, NormalizeName(name), ParsePhone(phone), creditCeiling, createdAt);

    public void Edit(string name, string? phone, Money creditCeiling)
    {
        var cleanName = NormalizeName(name);
        var cleanPhone = ParsePhone(phone);
        Name = cleanName;
        Phone = cleanPhone;
        CreditCeiling = creditCeiling;
    }

    public bool WouldExceedCeiling(Money amount) => Debt + amount > CreditCeiling;

    /// <summary>
    /// Vente à crédit : la dette augmente. Le plafond n'est pas contrôlé ici : c'est une décision de l'appelant (refus, validation d'un gérant, ou vente
    /// hors ligne déjà faite qu'on enregistre et qu'on signale).
    /// </summary>
    public void Charge(Money amount)
    {
        Ensure.That(IsActive, "Ce client est désactivé : il ne peut plus acheter à crédit.");
        Ensure.That(amount > Money.Zero, "Le montant à crédit doit être supérieur à zéro.");
        Debt += amount;
    }

    /// <summary>Versement du client : au plus la dette.</summary>
    public void Repay(Money amount)
    {
        Ensure.That(amount > Money.Zero, "Le versement doit être supérieur à zéro.");
        Ensure.That(amount <= Debt, $"Le versement dépasse la dette du client ({Debt}).");
        Debt -= amount;
    }

    /// <summary>
    /// Annulation d'une vente à crédit : la dette baisse du montant à crédit, sans passer sous zéro (si le client avait déjà payé, ce qui reste à lui
    /// rendre est une affaire de remboursement, T017, non modélisée ici).
    /// </summary>
    public void Discharge(Money amount) => Debt = amount >= Debt ? Money.Zero : Debt - amount;

    public void Deactivate()
    {
        Ensure.That(Debt == Money.Zero, "Ce client a une dette : il ne peut pas être désactivé tant qu'elle n'est pas payée.");
        IsActive = false;
    }

    /// <summary>Reconstruction depuis le stockage : l'état a déjà été validé par les opérations métier.</summary>
    internal static Customer Restore(
        CustomerId id,
        TenantId tenant,
        string name,
        PhoneNumber? phone,
        Money creditCeiling,
        Money debt,
        DateTimeOffset createdAt,
        bool isActive) =>
        new(id, tenant, name, phone, creditCeiling, createdAt) { Debt = debt, IsActive = isActive };

    private static string NormalizeName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        Ensure.That(
            trimmed.Length is >= MinNameLength and <= MaxNameLength,
            $"Le nom du client doit faire {MinNameLength} à {MaxNameLength} caractères.");
        return trimmed;
    }

    private static PhoneNumber? ParsePhone(string? phone) => string.IsNullOrWhiteSpace(phone) ? null : PhoneNumber.Parse(phone);
}
