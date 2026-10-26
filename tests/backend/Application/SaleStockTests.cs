using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

public class SaleStockTests
{
    private static RecordSaleCommand SaleOf(TestWorld world, params (ProductId Product, int Quantity)[] lines) =>
        world.CashSale(world.GivenOpenSession()) with
        {
            Lines = lines.Select(line => new SaleLineInput(line.Product, "Produit", line.Quantity, 600)).ToArray(),
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, lines.Sum(line => 600L * line.Quantity), null, null)],
        };

    [Fact]
    public async Task A_sale_decrements_the_stock_of_a_tracked_product()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 24);
        var command = SaleOf(world, (product.Id, 3));

        var result = await world.RecordSale.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(21, product.QuantityOnHand);
        var movement = Assert.Single(world.Movements.All);
        Assert.Equal(StockMovementKind.Sale, movement.Kind);
        Assert.Equal(-3, movement.QuantityDelta);
        Assert.Equal(command.Id.Value, movement.SourceId);
        Assert.Equal(world.Cashier, movement.User);
    }

    [Fact]
    public async Task Two_lines_of_the_same_product_make_a_single_movement()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10);

        await world.RecordSale.HandleAsync(SaleOf(world, (product.Id, 2), (product.Id, 3)));

        Assert.Equal(5, product.QuantityOnHand);
        Assert.Equal(-5, Assert.Single(world.Movements.All).QuantityDelta);
    }

    [Fact]
    public async Task A_sale_is_never_blocked_by_an_empty_stock()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 1);

        var result = await world.RecordSale.HandleAsync(SaleOf(world, (product.Id, 4)));

        Assert.True(result.IsSuccess);
        Assert.Equal(-3, product.QuantityOnHand);
    }

    [Fact]
    public async Task Unknown_and_untracked_products_leave_no_movement()
    {
        var world = new TestWorld();
        var service = world.GivenProduct(name: "Coupe", barcode: null, tracksStock: false);

        await world.RecordSale.HandleAsync(SaleOf(world, (service.Id, 1), (ProductId.New(), 1)));

        Assert.Empty(world.Movements.All);
    }

    [Fact]
    public async Task Replaying_a_sale_does_not_decrement_the_stock_again()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10);
        var command = SaleOf(world, (product.Id, 2));
        await world.RecordSale.HandleAsync(command);

        var replay = await world.RecordSale.HandleAsync(command);

        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(8, product.QuantityOnHand);
        Assert.Single(world.Movements.All);
    }
}
