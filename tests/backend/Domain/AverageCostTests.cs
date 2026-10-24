using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Domain.Tests;

/// <summary>RG-11 : le prix d'achat est la moyenne pondérée des livraisons (docs/specs/cout-moyen).</summary>
public class AverageCostTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Actor = UserId.New();

    private static Product WithStock(int stock, long purchase = 400)
    {
        var product = Product.Create(
            ProductId.New(), TenantId.New(), "Jus d'orange 1L", "611", "Boissons", Money.Fcfa(purchase), Money.Fcfa(600), 5, true, At);
        if (stock > 0)
        {
            product.Receive(stock, null, Actor, At);
        }
        else if (stock < 0)
        {
            product.RecordSale(-stock, Guid.NewGuid(), Actor, At);
        }

        return product;
    }

    [Fact]
    public void The_average_follows_the_weight_of_each_delivery_SC_M1()
    {
        var product = WithStock(10, 400);

        product.Receive(10, null, Actor, At, null, Money.Fcfa(500));

        Assert.Equal(Money.Fcfa(450), product.PurchasePrice);
        Assert.Equal(20, product.QuantityOnHand);
    }

    [Fact]
    public void A_small_delivery_moves_the_average_little()
    {
        var product = WithStock(3, 400);

        product.Receive(1, null, Actor, At, null, Money.Fcfa(500));

        Assert.Equal(Money.Fcfa(425), product.PurchasePrice);
    }

    [Theory]
    [InlineData(1, 400, 2, 401, 401)] // 400,67 → 401
    [InlineData(1, 400, 1, 401, 401)] // 400,5 → 401 : 0,5 vers le haut
    [InlineData(3, 400, 1, 401, 400)] // 400,25 → 400
    [InlineData(2, 400, 1, 400, 400)]
    public void The_average_is_rounded_to_the_nearest_franc_SC_M2(int stock, long price, int received, long cost, long expected)
    {
        var product = WithStock(stock, price);

        product.Receive(received, null, Actor, At, null, Money.Fcfa(cost));

        Assert.Equal(Money.Fcfa(expected), product.PurchasePrice);
    }

    [Fact]
    public void An_empty_shelf_takes_the_price_of_the_delivery()
    {
        var product = WithStock(0, 400);

        product.Receive(10, null, Actor, At, null, Money.Fcfa(500));

        Assert.Equal(Money.Fcfa(500), product.PurchasePrice);
    }

    [Fact]
    public void A_negative_stock_does_not_weigh_on_the_average_SC_M3()
    {
        var product = WithStock(-4, 400);
        Assert.Equal(-4, product.QuantityOnHand);

        product.Receive(10, null, Actor, At, null, Money.Fcfa(500));

        Assert.Equal(Money.Fcfa(500), product.PurchasePrice);
        Assert.Equal(6, product.QuantityOnHand);
    }

    [Fact]
    public void A_delivery_without_a_price_leaves_the_average_alone_SC_M4()
    {
        var product = WithStock(10, 400);

        var movement = product.Receive(10, "Grossiste", Actor, At);

        Assert.Equal(Money.Fcfa(400), product.PurchasePrice);
        Assert.Null(movement.UnitCost);
    }

    [Fact]
    public void The_movement_keeps_the_price_of_its_delivery_for_the_history()
    {
        var product = WithStock(10, 400);

        var movement = product.Receive(10, null, Actor, At, null, Money.Fcfa(500));

        Assert.Equal(Money.Fcfa(500), movement.UnitCost);
        Assert.Equal(10, movement.QuantityDelta);
    }

    [Fact]
    public void A_free_delivery_lowers_the_average()
    {
        var product = WithStock(10, 400);

        product.Receive(10, null, Actor, At, null, Money.Zero);

        Assert.Equal(Money.Fcfa(200), product.PurchasePrice);
    }

    [Fact]
    public void Successive_deliveries_chain_on_the_previous_average()
    {
        var product = WithStock(10, 400);

        product.Receive(10, null, Actor, At, null, Money.Fcfa(500)); // 450 sur 20
        product.Receive(20, null, Actor, At, null, Money.Fcfa(600)); // (20×450 + 20×600) / 40 = 525

        Assert.Equal(Money.Fcfa(525), product.PurchasePrice);
        Assert.Equal(40, product.QuantityOnHand);
    }

    [Fact]
    public void Sales_between_deliveries_change_the_weight_but_not_the_average()
    {
        var product = WithStock(10, 400);
        product.RecordSale(8, Guid.NewGuid(), Actor, At); // reste 2 à 400

        product.Receive(2, null, Actor, At, null, Money.Fcfa(500));

        Assert.Equal(Money.Fcfa(450), product.PurchasePrice);
    }

    [Fact]
    public void A_manual_edit_replaces_the_average()
    {
        var product = WithStock(10, 400);
        product.Receive(10, null, Actor, At, null, Money.Fcfa(500));

        product.Edit(product.Name, product.Barcode, product.Category, Money.Fcfa(480), product.SalePrice, product.AlertThreshold);

        Assert.Equal(Money.Fcfa(480), product.PurchasePrice);
    }

    [Fact]
    public void Huge_quantities_do_not_overflow()
    {
        var product = WithStock(1, 400);
        product.Receive(int.MaxValue - 1, null, Actor, At, null, Money.Fcfa(1_000_000_000_000));

        Assert.True(product.PurchasePrice.Amount > 999_999_999_000);
    }

    [Fact]
    public void A_sale_line_keeps_the_cost_it_was_given_and_the_total_ignores_it()
    {
        var line = SaleLine.Create(ProductId.New(), "Jus", 3, Money.Fcfa(600), Money.Fcfa(450));

        Assert.Equal(Money.Fcfa(450), line.UnitCost);
        Assert.Equal(Money.Fcfa(1_800), line.Total);
    }

    [Fact]
    public void A_sale_line_may_have_no_cost_for_an_unknown_product_or_an_old_sale()
    {
        var line = SaleLine.Create(ProductId.New(), "Jus", 3, Money.Fcfa(600));

        Assert.Null(line.UnitCost);
    }
}
