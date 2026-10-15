using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Domain.Tests;

public class PaymentTests
{
    [Fact]
    public void Cash_computes_the_change_to_give_back()
    {
        var payment = TestData.Cash(amount: 2_300, tendered: 5_000);

        Assert.Equal(Money.Fcfa(2_700), payment.Change);
        Assert.Equal(Money.Fcfa(5_000), payment.Tendered);
    }

    [Fact]
    public void Cash_rejects_an_insufficient_tendered_amount() =>
        Assert.Throws<DomainException>(() => TestData.Cash(amount: 2_300, tendered: 2_000));

    [Fact]
    public void Mobile_money_requires_a_reference() =>
        Assert.Throws<DomainException>(() => TestData.Mobile(1_000, reference: "  "));

    [Fact]
    public void Mobile_money_trims_its_reference() =>
        Assert.Equal("ABC123", TestData.Mobile(1_000, reference: "  ABC123 ").Reference);

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Credit)]
    public void Mobile_money_factory_rejects_a_non_mobile_method(PaymentMethod method) =>
        Assert.Throws<DomainException>(() => TestData.Mobile(1_000, method: method));

    [Fact]
    public void Payment_amount_must_be_positive() =>
        Assert.Throws<DomainException>(() => TestData.Cash(amount: 0));
}
