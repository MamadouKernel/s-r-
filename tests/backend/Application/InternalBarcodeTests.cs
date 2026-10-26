using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

public class InternalBarcodeHandlerTests
{
    private static TestWorld AsManager()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        return world;
    }

    [Fact]
    public async Task A_manager_gives_a_product_without_a_code_a_valid_internal_ean13_and_it_is_audited()
    {
        var world = AsManager();
        var product = world.GivenProduct(name: "Pain de mie", barcode: null);

        var result = await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(result.Value, product.Barcode);
        Assert.StartsWith("29", product.Barcode, StringComparison.Ordinal);
        Assert.True(InternalBarcode.IsValidEan13(product.Barcode!));
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.ProductBarcodeCreated, entry.Action);
        Assert.Equal("Code interne créé : Pain de mie", entry.Detail);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_code_found_by_the_till_scan_is_the_one_created()
    {
        var world = AsManager();
        var product = world.GivenProduct(barcode: null);

        var result = await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(product.Id));

        Assert.True(await world.Products.IsBarcodeUsedAsync(world.Tenant, result.Value!, null, CancellationToken.None));
    }

    [Fact]
    public async Task Two_products_never_get_the_same_code_and_the_sequence_goes_up_by_one()
    {
        var world = AsManager();
        var first = world.GivenProduct(name: "Pain", barcode: null);
        var second = world.GivenProduct(name: "Riz", barcode: null);

        var one = (await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(first.Id))).Value!;
        var two = (await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(second.Id))).Value!;

        Assert.NotEqual(one, two);
        Assert.True(InternalBarcode.TryGetSequence(one, out var a));
        Assert.True(InternalBarcode.TryGetSequence(two, out var b));
        Assert.Equal(a + 1, b);
    }

    [Fact]
    public async Task The_sequence_continues_after_the_highest_internal_code_of_the_commerce_ignoring_factory_codes_and_invalid_ones()
    {
        var world = AsManager();
        world.GivenProduct(name: "Ancien", barcode: InternalBarcode.Compose(41));
        world.GivenProduct(name: "Usine", barcode: "6111000000011");
        world.GivenProduct(name: "Tapé à la main", barcode: "2999999999999"); // 13 chiffres en 29…, mais clé fausse
        var product = world.GivenProduct(name: "Nouveau", barcode: null);

        var result = await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(product.Id));

        Assert.Equal(InternalBarcode.Compose(42), result.Value);
    }

    [Fact]
    public async Task Another_commerce_has_its_own_sequence()
    {
        var world = AsManager();
        world.GivenProduct(name: "Chez l'autre", barcode: InternalBarcode.Compose(500), tenant: TenantId.New());
        var product = world.GivenProduct(name: "Chez moi", barcode: null);

        var result = await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(product.Id));

        Assert.Equal(InternalBarcode.Compose(1), result.Value);
    }

    [Theory]
    [InlineData("6111000000011")]
    [InlineData("2900000000018")]
    public async Task A_product_that_already_has_a_code_is_refused_and_keeps_it(string existing)
    {
        var world = AsManager();
        var product = world.GivenProduct(barcode: existing);

        var result = await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(product.Id));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("déjà un code-barres", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(existing, product.Barcode);
        Assert.Empty(world.Audit.Entries);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Only_the_owner_and_the_manager_create_a_code(Role role)
    {
        var world = new TestWorld();
        var product = world.GivenProduct(barcode: null);
        world.ActAs(world.Cashier, role);

        var result = await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(product.Id));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Null(product.Barcode);
    }

    [Fact]
    public async Task The_owner_may_create_a_code_too()
    {
        var world = new TestWorld();
        var product = world.GivenProduct(barcode: null);
        world.ActAs(world.Owner, Role.Owner);

        Assert.True((await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(product.Id))).IsSuccess);
    }

    [Fact]
    public async Task Fails_for_an_unknown_product_or_one_of_another_commerce()
    {
        var world = AsManager();
        var foreign = world.GivenProduct(barcode: null, tenant: TenantId.New());

        var unknown = await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(ProductId.New()));
        var other = await world.CreateInternalBarcode.HandleAsync(new CreateInternalBarcodeCommand(foreign.Id));

        Assert.Equal(ErrorCodes.NotFound, unknown.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, other.Error!.Code);
    }
}
