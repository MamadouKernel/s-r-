using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Domain.Tests;

public class SaleLineTests
{
    [Fact]
    public void Total_is_unit_price_times_quantity() =>
        Assert.Equal(Money.Fcfa(1_500), TestData.Line(unitPrice: 500, quantity: 3).Total);

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Quantity_must_be_strictly_positive(int quantity) =>
        Assert.Throws<DomainException>(() => TestData.Line(500, quantity));

    [Fact]
    public void Product_name_is_required() =>
        Assert.Throws<DomainException>(() =>
            SaleLine.Create(ProductId.New(), " ", 1, Money.Fcfa(500)));
}
