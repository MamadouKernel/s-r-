using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Domain.Tests;

/// <summary>Vente au poids et à la mesure (docs/specs/vente-au-poids VPO-01 à VPO-05) : quantités entières en millièmes, total arrondi au franc.</summary>
public class MeasureUnitTests
{
    [Theory]
    [InlineData(MeasureUnit.Piece, 1)]
    [InlineData(MeasureUnit.Kilogram, 1_000)]
    [InlineData(MeasureUnit.Litre, 1_000)]
    public void A_quantity_is_stored_in_thousandths_for_a_weighed_or_measured_unit(MeasureUnit unit, int scale) =>
        Assert.Equal(scale, unit.Scale());

    [Theory]
    [InlineData(800, 1_250, MeasureUnit.Kilogram, 1_000)] // 1,250 kg de riz à 800 FCFA/kg
    [InlineData(800, 333, MeasureUnit.Kilogram, 266)] // 266,4 → 266
    [InlineData(500, 1, MeasureUnit.Kilogram, 1)] // 0,5 → 1 : demi vers le haut
    [InlineData(700, 1_429, MeasureUnit.Kilogram, 1_000)] // 1 000,3 → 1 000
    [InlineData(1_500, 2_000, MeasureUnit.Litre, 3_000)]
    [InlineData(600, 2, MeasureUnit.Piece, 1_200)] // une pièce : aucun arrondi
    [InlineData(0, 5_000, MeasureUnit.Kilogram, 0)]
    public void The_total_of_a_line_is_the_price_of_one_unit_times_the_quantity_rounded_to_the_franc(long price, int quantity, MeasureUnit unit, long expected) =>
        Assert.Equal(Money.Fcfa(expected), Money.Fcfa(price).ForQuantity(quantity, unit));

    [Fact]
    public void A_negative_quantity_is_refused() =>
        Assert.Throws<DomainException>(() => Money.Fcfa(800).ForQuantity(-1, MeasureUnit.Kilogram));

    [Fact]
    public void The_french_names_are_the_ones_the_shopkeeper_reads()
    {
        Assert.Equal("kg", MeasureUnit.Kilogram.Symbol());
        Assert.Equal("L", MeasureUnit.Litre.Symbol());
        Assert.Equal(string.Empty, MeasureUnit.Piece.Symbol());
    }
}

public class WeighedSaleLineTests
{
    [Fact]
    public void A_line_keeps_the_unit_it_was_sold_in_and_totals_with_it()
    {
        var line = SaleLine.Create(ProductId.New(), "Riz parfumé", 1_250, Money.Fcfa(800), Money.Fcfa(600), MeasureUnit.Kilogram);

        Assert.Equal(MeasureUnit.Kilogram, line.Unit);
        Assert.Equal(Money.Fcfa(1_000), line.Total);
    }

    [Fact]
    public void A_line_is_in_pieces_unless_said_otherwise()
    {
        var line = SaleLine.Create(ProductId.New(), "Jus", 2, Money.Fcfa(600));

        Assert.Equal(MeasureUnit.Piece, line.Unit);
        Assert.Equal(Money.Fcfa(1_200), line.Total);
    }

    [Fact]
    public void The_total_of_a_sale_is_the_sum_of_its_rounded_lines()
    {
        var lines = new[]
        {
            SaleLine.Create(ProductId.New(), "Riz", 333, Money.Fcfa(800), null, MeasureUnit.Kilogram), // 266
            SaleLine.Create(ProductId.New(), "Sucre", 333, Money.Fcfa(800), null, MeasureUnit.Kilogram), // 266
            SaleLine.Create(ProductId.New(), "Jus", 1, Money.Fcfa(600)),
        };

        Assert.Equal(Money.Fcfa(1_132), Money.Sum(lines.Select(line => line.Total)));
    }
}

public class ProductUnitTests
{
    private static Product NewProduct(MeasureUnit unit = MeasureUnit.Piece) =>
        Product.Create(
            ProductId.New(), TenantId.New(), "Riz", null, null, Money.Fcfa(600), Money.Fcfa(800), 0, true,
            new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero), null, null, unit);

    [Fact]
    public void A_product_is_sold_by_the_piece_unless_said_otherwise() => Assert.Equal(MeasureUnit.Piece, NewProduct().Unit);

    [Fact]
    public void A_product_can_be_sold_by_the_kilogram_or_the_litre()
    {
        Assert.Equal(MeasureUnit.Kilogram, NewProduct(MeasureUnit.Kilogram).Unit);
        Assert.Equal(MeasureUnit.Litre, NewProduct(MeasureUnit.Litre).Unit);
    }

    [Fact]
    public void The_unit_can_change_only_while_there_is_no_stock()
    {
        var product = NewProduct();
        product.ChangeUnit(MeasureUnit.Kilogram);
        Assert.Equal(MeasureUnit.Kilogram, product.Unit);

        product.Receive(25_000, null, UserId.New(), DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => product.ChangeUnit(MeasureUnit.Piece));
        Assert.Equal(MeasureUnit.Kilogram, product.Unit);
    }

    [Fact]
    public void The_weighted_average_cost_works_per_kilogram_with_quantities_in_grams()
    {
        var product = NewProduct(MeasureUnit.Kilogram);
        var now = DateTimeOffset.UtcNow;
        product.Receive(10_000, null, UserId.New(), now, null, Money.Fcfa(800)); // 10 kg à 800 FCFA/kg
        product.Receive(10_000, null, UserId.New(), now, null, Money.Fcfa(900)); // 10 kg à 900 FCFA/kg

        Assert.Equal(Money.Fcfa(850), product.PurchasePrice);
        Assert.Equal(20_000, product.QuantityOnHand);
    }
}
