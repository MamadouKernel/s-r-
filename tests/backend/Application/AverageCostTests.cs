using Soro.Application.Common;
using Soro.Application.Sales;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

/// <summary>RG-11 : prix d'achat moyen à la réception et coût figé à la vente (docs/specs/cout-moyen).</summary>
public class AverageCostReceptionTests
{
    private static TestWorld AsManager()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        return world;
    }

    private static ReceiveStockCommand Receive(Product product, int quantity, long? price, StockMovementId? id = null) =>
        new(id ?? StockMovementId.New(), product.Id, quantity, null, price);

    [Fact]
    public async Task A_delivery_with_a_price_recomputes_the_weighted_average_and_reports_it_SC_M1()
    {
        var world = AsManager();
        var product = world.GivenProduct(stock: 10); // 10 à 400

        var result = await world.ReceiveStock.HandleAsync(Receive(product, 10, 500));

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.Fcfa(450), product.PurchasePrice);
        Assert.Equal(450, result.Value!.PurchasePriceFcfa);
        Assert.Equal(20, result.Value.QuantityOnHand);
        Assert.Equal(Money.Fcfa(600), product.SalePrice); // le prix de vente ne bouge pas
    }

    [Fact]
    public async Task A_delivery_without_a_price_keeps_the_average_and_still_reports_it_SC_M4()
    {
        var world = AsManager();
        var product = world.GivenProduct(stock: 10);

        var result = await world.ReceiveStock.HandleAsync(Receive(product, 10, null));

        Assert.Equal(Money.Fcfa(400), product.PurchasePrice);
        Assert.Equal(400, result.Value!.PurchasePriceFcfa);
        Assert.Null(Assert.Single(world.Movements.All, movement => movement.Kind == StockMovementKind.Reception && movement.QuantityDelta == 10).UnitCost);
    }

    [Fact]
    public async Task The_movement_keeps_the_price_of_its_delivery()
    {
        var world = AsManager();
        var product = world.GivenProduct(stock: 0);

        await world.ReceiveStock.HandleAsync(Receive(product, 10, 500));

        Assert.Equal(Money.Fcfa(500), Assert.Single(world.Movements.All).UnitCost);
    }

    [Fact]
    public async Task Replaying_a_reception_does_not_recompute_the_average_twice_SC_M9()
    {
        var world = AsManager();
        var product = world.GivenProduct(stock: 10);
        var id = StockMovementId.New();

        await world.ReceiveStock.HandleAsync(Receive(product, 10, 500, id));
        var replay = await world.ReceiveStock.HandleAsync(Receive(product, 10, 500, id));

        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(Money.Fcfa(450), product.PurchasePrice);
        Assert.Equal(450, replay.Value.PurchasePriceFcfa);
        Assert.Equal(20, product.QuantityOnHand);
    }

    [Fact]
    public async Task A_negative_price_is_refused_and_changes_nothing_SC_M8()
    {
        var world = AsManager();
        var product = world.GivenProduct(stock: 10);

        var result = await world.ReceiveStock.HandleAsync(Receive(product, 10, -5));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal(Money.Fcfa(400), product.PurchasePrice);
        Assert.Equal(10, product.QuantityOnHand);
    }

    [Fact]
    public async Task The_history_shows_the_price_of_each_delivery()
    {
        var world = AsManager();
        var product = world.GivenProduct(stock: 0);
        await world.ReceiveStock.HandleAsync(Receive(product, 10, 500));
        await world.ReceiveStock.HandleAsync(Receive(product, 5, null));

        var history = (await world.ListStockMovements.HandleAsync(new ListStockMovementsQuery(product.Id, 50))).Value!;

        Assert.Equal([null, 500L], history.Where(view => view.Kind == StockMovementKind.Reception).OrderBy(view => view.QuantityDelta).Select(view => view.UnitCostFcfa));
    }
}

public class AverageCostSaleTests
{
    private static RecordSaleCommand SaleOf(TestWorld world, params (ProductId Product, int Quantity)[] lines) =>
        world.CashSale(world.GivenOpenSession()) with
        {
            Lines = lines.Select(line => new SaleLineInput(line.Product, "Produit", line.Quantity, 600)).ToArray(),
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, lines.Sum(line => 600L * line.Quantity), null, null)],
        };

    [Fact]
    public async Task A_sale_keeps_the_average_cost_of_the_moment_SC_M5()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10); // 400
        world.ActAs(world.Manager, Role.Manager);

        await world.RecordSale.HandleAsync(SaleOf(world, (product.Id, 2)));
        await world.ReceiveStock.HandleAsync(new ReceiveStockCommand(StockMovementId.New(), product.Id, 8, null, 600)); // 8 à 400 + 8 à 600 = 500

        await world.RecordSale.HandleAsync(SaleOf(world, (product.Id, 1)));

        var costs = world.Sales.All.SelectMany(sale => sale.Lines).Select(line => line.UnitCost).ToList();
        Assert.Equal([Money.Fcfa(400), Money.Fcfa(500)], costs);
        Assert.Equal(Money.Fcfa(500), product.PurchasePrice);
    }

    [Fact]
    public async Task Every_line_of_the_same_product_gets_the_cost()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(stock: 10);

        await world.RecordSale.HandleAsync(SaleOf(world, (product.Id, 2), (product.Id, 3)));

        Assert.All(world.Sales.All.Single().Lines, line => Assert.Equal(Money.Fcfa(400), line.UnitCost));
    }

    [Fact]
    public async Task A_service_without_stock_tracking_still_gets_its_cost()
    {
        var world = new TestWorld();
        var service = world.GivenProduct(name: "Coupe", barcode: null, tracksStock: false);

        await world.RecordSale.HandleAsync(SaleOf(world, (service.Id, 1)));

        Assert.Equal(Money.Fcfa(400), world.Sales.All.Single().Lines.Single().UnitCost);
    }

    [Fact]
    public async Task A_product_missing_from_the_catalogue_has_no_cost()
    {
        var world = new TestWorld();

        await world.RecordSale.HandleAsync(SaleOf(world, (ProductId.New(), 1)));

        Assert.Null(world.Sales.All.Single().Lines.Single().UnitCost);
    }

    [Fact]
    public async Task A_product_of_another_commerce_gives_no_cost()
    {
        var world = new TestWorld();
        var foreign = world.GivenProduct(tenant: TenantId.New());

        await world.RecordSale.HandleAsync(SaleOf(world, (foreign.Id, 1)));

        Assert.Null(world.Sales.All.Single().Lines.Single().UnitCost);
    }
}
