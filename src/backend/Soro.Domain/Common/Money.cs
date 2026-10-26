using System.Globalization;

namespace Soro.Domain.Common;

/// <summary>Montant en FCFA, entier et jamais négatif (RG-18).</summary>
public readonly record struct Money : IComparable<Money>
{
    public static Money Zero => default;

    public long Amount { get; private init; }

    public static Money Fcfa(long amount)
    {
        Ensure.That(amount >= 0, "Un montant ne peut pas être négatif.");
        return new Money { Amount = amount };
    }

    public static Money Sum(IEnumerable<Money> values) =>
        values.Aggregate(Zero, (total, value) => total + value);

    public static Money operator +(Money left, Money right) =>
        new() { Amount = checked(left.Amount + right.Amount) };

    public static Money operator -(Money left, Money right) =>
        Fcfa(left.Amount - right.Amount);

    public static Money operator *(Money money, int quantity)
    {
        Ensure.That(quantity >= 0, "Une quantité ne peut pas être négative.");
        return new Money { Amount = checked(money.Amount * quantity) };
    }

    /// <summary>
    /// Total d'une ligne : le prix d'<b>une</b> unité de vente fois la quantité (comptée en millièmes pour un produit pesé ou mesuré), arrondi au franc
    /// le plus proche, demi vers le haut (docs/decisions/0017).
    /// </summary>
    public Money ForQuantity(long quantity, MeasureUnit unit)
    {
        Ensure.That(quantity >= 0, "Une quantité ne peut pas être négative.");
        var scale = unit.Scale();
        return new Money { Amount = checked(((Amount * quantity) + (scale / 2)) / scale) };
    }

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public override string ToString() =>
        $"{Amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} FCFA";
}
