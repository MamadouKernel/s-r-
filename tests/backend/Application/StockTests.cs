using Soro.Application.Common;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

public class StockTests
{
    [Fact]
    public async Task A_stock_keeper_receives_goods_and_the_movement_is_attributed()
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, Role.StockKeeper);
        var product = world.GivenProduct();
        var movementId = StockMovementId.New();

        var result = await world.ReceiveStock.HandleAsync(
            new ReceiveStockCommand(movementId, product.Id, 24, "Grossiste Koné", null));

        Assert.True(result.IsSuccess);
        Assert.Equal(24, result.Value!.QuantityOnHand);
        var movement = Assert.Single(world.Movements.All);
        Assert.Equal(movementId, movement.Id);
        Assert.Equal(world.Cashier, movement.User);
        Assert.Equal(StockMovementKind.Reception, movement.Kind);
    }

    [Fact]
    public async Task Replaying_a_reception_does_not_add_stock_twice()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var product = world.GivenProduct();
        var command = new ReceiveStockCommand(StockMovementId.New(), product.Id, 10, null, null);
        await world.ReceiveStock.HandleAsync(command);

        var replay = await world.ReceiveStock.HandleAsync(command);

        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(10, product.QuantityOnHand);
        Assert.Single(world.Movements.All);
    }

    [Fact]
    public async Task A_reception_can_update_the_purchase_price()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var product = world.GivenProduct();

        await world.ReceiveStock.HandleAsync(new ReceiveStockCommand(StockMovementId.New(), product.Id, 5, null, 450));

        Assert.Equal(Money.Fcfa(450), product.PurchasePrice);
        Assert.Equal(Money.Fcfa(600), product.SalePrice);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.Accountant)]
    public async Task Cashiers_and_accountants_cannot_receive_stock(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, role);
        var product = world.GivenProduct();

        var result = await world.ReceiveStock.HandleAsync(
            new ReceiveStockCommand(StockMovementId.New(), product.Id, 5, null, null));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Empty(world.Movements.All);
    }

    [Fact]
    public async Task Receiving_an_unknown_or_foreign_product_fails()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var foreign = world.GivenProduct(tenant: TenantId.New());

        var result = await world.ReceiveStock.HandleAsync(
            new ReceiveStockCommand(StockMovementId.New(), foreign.Id, 5, null, null));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task A_loss_requires_a_reason_and_is_audited()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var product = world.GivenProduct(stock: 10);

        var refused = await world.RecordStockLoss.HandleAsync(
            new RecordStockLossCommand(StockMovementId.New(), product.Id, 2, " "));
        var accepted = await world.RecordStockLoss.HandleAsync(
            new RecordStockLossCommand(StockMovementId.New(), product.Id, 2, "Périmé"));

        Assert.Equal(ErrorCodes.RuleViolation, refused.Error!.Code);
        Assert.Equal(8, accepted.Value!.QuantityOnHand);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.StockLoss);
    }

    [Fact]
    public async Task Replaying_a_loss_does_not_remove_stock_twice()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var product = world.GivenProduct(stock: 10);
        var command = new RecordStockLossCommand(StockMovementId.New(), product.Id, 2, "Cassé");
        await world.RecordStockLoss.HandleAsync(command);

        await world.RecordStockLoss.HandleAsync(command);

        Assert.Equal(8, product.QuantityOnHand);
    }

    [Fact]
    public async Task Low_stock_alerts_list_the_products_at_or_under_their_threshold_most_urgent_first()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        world.GivenProduct(name: "Plein", barcode: "1", threshold: 5, stock: 50);
        world.GivenProduct(name: "Bas", barcode: "2", threshold: 5, stock: 4);
        world.GivenProduct(name: "Vide", barcode: "3", threshold: 5, stock: 0);

        var result = await world.ListLowStock.HandleAsync();

        Assert.Equal(["Vide", "Bas"], result.Value!.Select(view => view.Name));
    }

    [Fact]
    public async Task Alerts_are_not_visible_to_a_cashier()
    {
        var world = new TestWorld();

        var result = await world.ListLowStock.HandleAsync();

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
    }

    [Fact]
    public async Task Movements_are_listed_for_a_product_of_the_shop()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var product = world.GivenProduct();
        await world.ReceiveStock.HandleAsync(new ReceiveStockCommand(StockMovementId.New(), product.Id, 10, null, null));

        var result = await world.ListStockMovements.HandleAsync(new ListStockMovementsQuery(product.Id, 50));

        var movement = Assert.Single(result.Value!);
        Assert.Equal(10, movement.QuantityDelta);
    }

    [Fact]
    public async Task Movements_of_a_foreign_product_are_not_found()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var foreign = world.GivenProduct(tenant: TenantId.New());

        var result = await world.ListStockMovements.HandleAsync(new ListStockMovementsQuery(foreign.Id, 50));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }
}
