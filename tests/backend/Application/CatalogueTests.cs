using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Application.Tests;

public class CatalogueTests
{
    private static CreateProductCommand Create(ProductId? id = null, string? barcode = "6111000000011") =>
        new(id ?? ProductId.New(), "Jus d'orange 1L", barcode, "Boissons", 400, 600, 5, true);

    [Fact]
    public async Task A_manager_creates_a_product_and_it_is_audited()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var command = Create();

        var result = await world.CreateProduct.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyProcessed);
        var stored = await world.Products.GetAsync(command.Id, world.Tenant, CancellationToken.None);
        Assert.Equal("Jus d'orange 1L", stored!.Name);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.ProductCreated);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Only_a_manager_creates_a_product(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, role);

        var result = await world.CreateProduct.HandleAsync(Create());

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(0, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Replaying_the_same_product_id_creates_nothing_more()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var command = Create();
        await world.CreateProduct.HandleAsync(command);

        var replay = await world.CreateProduct.HandleAsync(command);

        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Single(await world.Products.ListAsync(world.Tenant, null, true, CancellationToken.None));
    }

    [Fact]
    public async Task A_barcode_is_unique_within_the_shop()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        world.GivenProduct(barcode: "123");

        var result = await world.CreateProduct.HandleAsync(Create(barcode: "123"));

        Assert.Equal(ErrorCodes.BarcodeTaken, result.Error!.Code);
    }

    [Fact]
    public async Task The_same_barcode_is_allowed_in_another_shop()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        world.GivenProduct(barcode: "123", tenant: TenantId.New());

        var result = await world.CreateProduct.HandleAsync(Create(barcode: "123"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_rule_violation_is_reported_as_an_error()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.CreateProduct.HandleAsync(Create() with { Name = " " });

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task Update_changes_the_price_and_audits_it()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var product = world.GivenProduct();

        var result = await world.UpdateProduct.HandleAsync(
            new UpdateProductCommand(product.Id, "Jus 1L", product.Barcode, "Boissons", 400, 700, 5));

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.Fcfa(700), product.SalePrice);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.ProductUpdated);
    }

    [Fact]
    public async Task Update_cannot_take_the_barcode_of_another_product()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        world.GivenProduct(barcode: "111");
        var other = world.GivenProduct(name: "Savon", barcode: "222");

        var result = await world.UpdateProduct.HandleAsync(
            new UpdateProductCommand(other.Id, "Savon", "111", null, 100, 200, 0));

        Assert.Equal(ErrorCodes.BarcodeTaken, result.Error!.Code);
    }

    [Fact]
    public async Task Update_keeps_the_own_barcode_without_conflict()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var product = world.GivenProduct(barcode: "111");

        var result = await world.UpdateProduct.HandleAsync(
            new UpdateProductCommand(product.Id, "Jus", "111", null, 100, 200, 0));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Deactivating_hides_the_product_from_the_sale_list_but_keeps_it()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var product = world.GivenProduct();

        var result = await world.DeactivateProduct.HandleAsync(new DeactivateProductCommand(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Empty(await world.ListProducts.HandleAsync(new ListProductsQuery(null, false)));
        Assert.Single(await world.ListProducts.HandleAsync(new ListProductsQuery(null, true)));
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.ProductDeactivated);
    }

    [Fact]
    public async Task A_cashier_reads_the_catalogue_without_the_purchase_price_nor_inactive_products()
    {
        var world = new TestWorld();
        var active = world.GivenProduct(name: "Jus");
        var inactive = world.GivenProduct(name: "Ancien", barcode: "9");
        inactive.Deactivate();

        var list = await world.ListProducts.HandleAsync(new ListProductsQuery(null, IncludeInactive: true));

        var view = Assert.Single(list);
        Assert.Equal(active.Id.Value, view.Id);
        Assert.Null(view.PurchasePriceFcfa);
        Assert.Equal(600, view.SalePriceFcfa);
    }

    [Fact]
    public async Task A_stock_keeper_sees_the_purchase_price()
    {
        var world = new TestWorld();
        world.GivenProduct();
        world.ActAs(world.Cashier, Role.StockKeeper);

        var list = await world.ListProducts.HandleAsync(new ListProductsQuery(null, false));

        Assert.Equal(400, Assert.Single(list).PurchasePriceFcfa);
    }

    [Fact]
    public async Task Search_matches_name_or_exact_barcode_in_the_own_shop_only()
    {
        var world = new TestWorld();
        world.GivenProduct(name: "Jus d'orange", barcode: "111");
        world.GivenProduct(name: "Savon", barcode: "222");
        world.GivenProduct(name: "Jus étranger", barcode: "333", tenant: TenantId.New());

        var byName = await world.ListProducts.HandleAsync(new ListProductsQuery("jus", false));
        var byBarcode = await world.ListProducts.HandleAsync(new ListProductsQuery("222", false));

        Assert.Equal("Jus d'orange", Assert.Single(byName).Name);
        Assert.Equal("Savon", Assert.Single(byBarcode).Name);
    }
}
