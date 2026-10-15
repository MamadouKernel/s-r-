using Soro.Domain.Common;

namespace Soro.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Fcfa_rejects_a_negative_amount() =>
        Assert.Throws<DomainException>(() => Money.Fcfa(-1));

    [Fact]
    public void Default_value_is_zero() =>
        Assert.Equal(Money.Zero, default);

    [Fact]
    public void Adds_two_amounts() =>
        Assert.Equal(Money.Fcfa(1_500), Money.Fcfa(1_000) + Money.Fcfa(500));

    [Fact]
    public void Subtraction_below_zero_is_rejected() =>
        Assert.Throws<DomainException>(() => Money.Fcfa(500) - Money.Fcfa(501));

    [Fact]
    public void Multiplies_by_a_quantity() =>
        Assert.Equal(Money.Fcfa(1_500), Money.Fcfa(500) * 3);

    [Fact]
    public void Compares_amounts()
    {
        Assert.True(Money.Fcfa(500) < Money.Fcfa(600));
        Assert.True(Money.Fcfa(600) >= Money.Fcfa(600));
    }

    [Fact]
    public void Formats_thousands_with_spaces_and_no_decimals() =>
        Assert.Equal("1 500 FCFA", Money.Fcfa(1_500).ToString());
}
