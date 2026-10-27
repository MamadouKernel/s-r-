using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Domain.Tests;

public class ProductTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Actor = UserId.New();

    private static Product Given(
        string name = "Jus d'orange 1L",
        string? barcode = "6111000000011",
        long purchase = 400,
        long sale = 600,
        int threshold = 5,
        bool tracksStock = true) =>
        Product.Create(
            ProductId.New(), TenantId.New(), name, barcode, "Boissons",
            Money.Fcfa(purchase), Money.Fcfa(sale), threshold, tracksStock, At);

    [Fact]
    public void A_new_product_is_active_with_an_empty_stock()
    {
        var product = Given();

        Assert.True(product.IsActive);
        Assert.Equal(0, product.QuantityOnHand);
    }

    [Fact]
    public void Name_is_required_and_trimmed()
    {
        Assert.Throws<DomainException>(() => Given(name: "  "));
        Assert.Equal("Savon", Given(name: "  Savon ").Name);
    }

    [Fact]
    public void A_blank_barcode_means_no_barcode()
    {
        Assert.Null(Given(barcode: " ").Barcode);
        Assert.Equal("123", Given(barcode: " 123 ").Barcode);
    }

    [Fact]
    public void Alert_threshold_cannot_be_negative() =>
        Assert.Throws<DomainException>(() => Given(threshold: -1));

    [Fact]
    public void Edit_changes_the_catalogue_data()
    {
        var product = Given();

        product.Edit("Jus 1L", "999", "Jus", Money.Fcfa(450), Money.Fcfa(700), 8);

        Assert.Equal("Jus 1L", product.Name);
        Assert.Equal("999", product.Barcode);
        Assert.Equal(Money.Fcfa(700), product.SalePrice);
        Assert.Equal(8, product.AlertThreshold);
    }

    [Fact]
    public void Receive_adds_to_the_stock_and_returns_an_attributed_movement()
    {
        var product = Given();

        var movement = product.Receive(24, "Grossiste Koné", Actor, At);

        Assert.Equal(24, product.QuantityOnHand);
        Assert.Equal(StockMovementKind.Reception, movement.Kind);
        Assert.Equal(24, movement.QuantityDelta);
        Assert.Equal(Actor, movement.User);
        Assert.Equal(product.Id, movement.Product);
        Assert.Equal(product.Tenant, movement.Tenant);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Receive_requires_a_positive_quantity(int quantity) =>
        Assert.Throws<DomainException>(() => Given().Receive(quantity, null, Actor, At));

    [Fact]
    public void Loss_removes_stock_with_a_mandatory_reason()
    {
        var product = Given();
        product.Receive(10, null, Actor, At);

        var movement = product.RecordLoss(2, "Périmé", Actor, At);

        Assert.Equal(8, product.QuantityOnHand);
        Assert.Equal(-2, movement.QuantityDelta);
        Assert.Equal(StockMovementKind.Loss, movement.Kind);
        Assert.Throws<DomainException>(() => product.RecordLoss(1, " ", Actor, At));
        Assert.Equal(8, product.QuantityOnHand);
    }

    [Fact]
    public void Loss_cannot_exceed_the_stock_on_hand()
    {
        var product = Given();
        product.Receive(3, null, Actor, At);

        Assert.Throws<DomainException>(() => product.RecordLoss(4, "Cassé", Actor, At));
    }

    [Fact]
    public void A_sale_can_take_the_stock_below_zero_without_blocking()
    {
        var product = Given();
        product.Receive(1, null, Actor, At);
        var saleId = Guid.NewGuid();

        var movement = product.RecordSale(3, saleId, Actor, At);

        Assert.NotNull(movement);
        Assert.Equal(-2, product.QuantityOnHand);
        Assert.Equal(StockMovementKind.Sale, movement.Kind);
        Assert.Equal(saleId, movement.SourceId);
    }

    [Fact]
    public void A_product_without_stock_tracking_records_no_sale_movement_and_refuses_stock_operations()
    {
        var service = Given(tracksStock: false);

        Assert.Null(service.RecordSale(1, Guid.NewGuid(), Actor, At));
        Assert.Throws<DomainException>(() => service.Receive(1, null, Actor, At));
        Assert.Equal(0, service.QuantityOnHand);
    }

    [Fact]
    public void Low_stock_is_detected_at_or_below_the_threshold()
    {
        var product = Given(threshold: 5);
        product.Receive(6, null, Actor, At);
        Assert.False(product.IsLowStock);

        product.RecordSale(1, Guid.NewGuid(), Actor, At);

        Assert.True(product.IsLowStock);
    }

    [Fact]
    public void A_zero_threshold_never_raises_an_alert() =>
        Assert.False(Given(threshold: 0).IsLowStock);

    [Fact]
    public void Deactivate_hides_the_product_but_keeps_it()
    {
        var product = Given();

        product.Deactivate();

        Assert.False(product.IsActive);
        Assert.Throws<DomainException>(() => product.Receive(1, null, Actor, At));
    }

    [Fact]
    public void A_returned_article_goes_back_on_the_shelf_with_its_exact_quantity()
    {
        var product = Given();
        product.Receive(10, null, Actor, At);
        var before = product.QuantityOnHand;

        var movements = product.TakeBack(2, resellable: true, StockMovementKind.Return, "Retour", Guid.NewGuid(), Actor, At);

        Assert.Equal(before + 2, product.QuantityOnHand);
        Assert.Equal(StockMovementKind.Return, Assert.Single(movements).Kind);
    }

    [Fact]
    public void A_damaged_article_comes_back_and_leaves_at_once_so_the_stock_does_not_rise()
    {
        var product = Given();
        product.Receive(10, null, Actor, At);
        var before = product.QuantityOnHand;

        var movements = product.TakeBack(2, resellable: false, StockMovementKind.Return, "Abîmé", Guid.NewGuid(), Actor, At);

        Assert.Equal(before, product.QuantityOnHand);
        Assert.Equal([StockMovementKind.Return, StockMovementKind.Loss], movements.Select(movement => movement.Kind).ToArray());
        Assert.Equal([2, -2], movements.Select(movement => movement.QuantityDelta).ToArray());
    }

    [Fact]
    public void A_product_without_stock_tracking_is_not_touched_by_a_return_and_a_wrong_kind_is_refused()
    {
        var service = Given(tracksStock: false);

        Assert.Empty(service.TakeBack(1, true, StockMovementKind.Return, "x", Guid.NewGuid(), Actor, At));
        Assert.Throws<DomainException>(() => Given().TakeBack(1, true, StockMovementKind.Reception, "x", Guid.NewGuid(), Actor, At));
    }
}
