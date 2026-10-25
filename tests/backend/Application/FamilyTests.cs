using Soro.Application.Common;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

public class FamilyTests
{
    private static FormatInput Format(string? label, string? barcode = null, long sale = 300, ProductId? id = null) =>
        new(id ?? ProductId.New(), label, barcode, 200, sale, 5, true);

    private static CreateFamilyCommand Water(FamilyId? id = null, params FormatInput[] formats) =>
        new(id ?? FamilyId.New(), "Eau minérale", "Boissons",
            formats.Length > 0 ? formats : [Format("1,5 L", "111", 400), Format("1 L", "222", 300), Format("30 cl", "333", 150)]);

    private static TestWorld ManagerWorld()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        return world;
    }

    private static async Task<IReadOnlyList<Product>> AllAsync(TestWorld world) =>
        await world.Products.ListAsync(world.Tenant, null, true, CancellationToken.None);

    // 1. Eau minérale
    [Fact]
    public async Task A_family_is_created_with_its_formats_in_one_operation()
    {
        var world = ManagerWorld();

        var result = await world.CreateFamily.HandleAsync(Water());

        Assert.True(result.IsSuccess);
        var products = await AllAsync(world);
        Assert.Equal(3, products.Count);
        Assert.Contains(products, product => product.Name == "Eau minérale · 1,5 L" && product.SalePrice.Amount == 400);
        Assert.Contains(products, product => product.Name == "Eau minérale · 30 cl" && product.SalePrice.Amount == 150);
        Assert.All(products, product => Assert.Equal("Boissons", product.Category));
        Assert.Equal(1, world.UnitOfWork.Commits);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.FamilyCreated);
    }

    [Fact]
    public async Task Each_format_keeps_its_own_stock_and_barcode()
    {
        var world = ManagerWorld();
        var command = Water();
        await world.CreateFamily.HandleAsync(command);
        var litre = command.Formats[1];

        await world.ReceiveStock.HandleAsync(new ReceiveStockCommand(StockMovementId.New(), litre.Id, 10, null, null));

        var products = await AllAsync(world);
        Assert.Equal(10, products.Single(product => product.Id == litre.Id).QuantityOnHand);
        Assert.Equal(0, products.Single(product => product.Id == command.Formats[0].Id).QuantityOnHand);
        Assert.Equal("222", products.Single(product => product.Id == litre.Id).Barcode);
    }

    // 13. Rejeu
    [Fact]
    public async Task Replaying_the_same_family_creates_nothing_more()
    {
        var world = ManagerWorld();
        var command = Water();
        await world.CreateFamily.HandleAsync(command);

        var replay = await world.CreateFamily.HandleAsync(command);

        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(3, (await AllAsync(world)).Count);
    }

    // 4. Libellé en double
    [Theory]
    [InlineData("1 L")]
    [InlineData("1 l")]
    [InlineData("  1   L ")]
    public async Task A_label_must_be_unique_within_the_family(string duplicate)
    {
        var world = ManagerWorld();

        var result = await world.CreateFamily.HandleAsync(Water(null, Format("1 L", "1"), Format(duplicate, "2")));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(await AllAsync(world));
        Assert.Equal(0, world.UnitOfWork.Commits);
    }

    // 5. Famille à un format
    [Fact]
    public async Task A_family_with_one_format_may_have_no_label()
    {
        var world = ManagerWorld();

        var result = await world.CreateFamily.HandleAsync(Water(null, Format(null)));

        Assert.True(result.IsSuccess);
        Assert.Equal("Eau minérale", (await AllAsync(world)).Single().Name);
    }

    [Fact]
    public async Task A_family_with_two_formats_needs_a_label_on_each()
    {
        var world = ManagerWorld();

        var result = await world.CreateFamily.HandleAsync(Water(null, Format("1 L"), Format(null)));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task A_family_needs_at_least_one_format()
    {
        var world = ManagerWorld();

        var result = await world.CreateFamily.HandleAsync(new CreateFamilyCommand(FamilyId.New(), "Eau", null, []));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task Adding_a_second_format_to_an_unlabelled_one_asks_for_its_label()
    {
        var world = ManagerWorld();
        var family = Water(null, Format(null, "1"));
        await world.CreateFamily.HandleAsync(family);

        var refused = await world.AddFormat.HandleAsync(new AddFormatCommand(family.Id, Format("1 L", "2"), null));
        var accepted = await world.AddFormat.HandleAsync(new AddFormatCommand(family.Id, Format("1 L", "2"), "50 cl"));

        Assert.Equal(ErrorCodes.RuleViolation, refused.Error!.Code);
        Assert.True(accepted.IsSuccess);
        var names = (await AllAsync(world)).Select(product => product.Name).Order().ToArray();
        Assert.Equal(["Eau minérale · 1 L", "Eau minérale · 50 cl"], names);
    }

    [Fact]
    public async Task A_new_format_cannot_reuse_a_label_or_a_barcode()
    {
        var world = ManagerWorld();
        var family = Water();
        await world.CreateFamily.HandleAsync(family);

        var sameLabel = await world.AddFormat.HandleAsync(new AddFormatCommand(family.Id, Format("1 l", "999"), null));
        var sameBarcode = await world.AddFormat.HandleAsync(new AddFormatCommand(family.Id, Format("2 L", "111"), null));

        Assert.Equal(ErrorCodes.RuleViolation, sameLabel.Error!.Code);
        Assert.Equal(ErrorCodes.BarcodeTaken, sameBarcode.Error!.Code);
    }

    [Fact]
    public async Task A_barcode_is_unique_across_the_whole_shop()
    {
        var world = ManagerWorld();
        world.GivenProduct(barcode: "111");

        var result = await world.CreateFamily.HandleAsync(Water());

        Assert.Equal(ErrorCodes.BarcodeTaken, result.Error!.Code);
        Assert.Single(await AllAsync(world));
    }

    [Fact]
    public async Task Two_formats_of_the_same_request_cannot_share_a_barcode()
    {
        var world = ManagerWorld();

        var result = await world.CreateFamily.HandleAsync(Water(null, Format("1 L", "7"), Format("2 L", "7")));

        Assert.Equal(ErrorCodes.BarcodeTaken, result.Error!.Code);
    }

    // 10. Renommer
    [Fact]
    public async Task Renaming_a_family_renames_every_format()
    {
        var world = ManagerWorld();
        var family = Water();
        await world.CreateFamily.HandleAsync(family);

        var result = await world.UpdateFamily.HandleAsync(new UpdateFamilyCommand(family.Id, "Eau de source", "Eaux"));

        Assert.True(result.IsSuccess);
        var products = await AllAsync(world);
        Assert.All(products, product => Assert.StartsWith("Eau de source · ", product.Name, StringComparison.Ordinal));
        Assert.All(products, product => Assert.Equal("Eaux", product.Category));
    }

    [Fact]
    public async Task Changing_a_label_through_the_product_update_renames_only_that_format()
    {
        var world = ManagerWorld();
        var family = Water();
        await world.CreateFamily.HandleAsync(family);
        var litre = family.Formats[1];

        var result = await world.UpdateProduct.HandleAsync(
            new UpdateProductCommand(litre.Id, "Eau minérale", "222", "Boissons", 210, 320, 5, "1 litre"));

        Assert.True(result.IsSuccess);
        var products = await AllAsync(world);
        Assert.Contains(products, product => product.Id == litre.Id && product.Name == "Eau minérale · 1 litre" && product.SalePrice.Amount == 320);
        Assert.Contains(products, product => product.Name == "Eau minérale · 1,5 L");
    }

    [Fact]
    public async Task A_label_taken_by_a_sibling_cannot_be_used_in_an_update()
    {
        var world = ManagerWorld();
        var family = Water();
        await world.CreateFamily.HandleAsync(family);

        var result = await world.UpdateProduct.HandleAsync(
            new UpdateProductCommand(family.Formats[1].Id, "Eau minérale", "222", "Boissons", 200, 300, 5, "30 CL"));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task A_product_created_the_old_way_gets_its_own_family_and_renames_it_on_update()
    {
        var world = ManagerWorld();
        var id = ProductId.New();
        await world.CreateProduct.HandleAsync(new CreateProductCommand(id, "Savon", null, null, 100, 150, 0, true));

        await world.UpdateProduct.HandleAsync(new UpdateProductCommand(id, "Savon Omo", null, null, 100, 150, 0));

        var product = (await AllAsync(world)).Single();
        Assert.Equal("Savon Omo", product.Name);
        Assert.Equal(new FamilyId(id.Value), product.FamilyId);
        Assert.Equal("Savon Omo", (await world.Families.GetAsync(product.FamilyId, world.Tenant, CancellationToken.None))!.Name);
    }

    // 11. Désactivation
    [Fact]
    public async Task Deactivating_every_format_removes_the_family_from_the_active_catalogue()
    {
        var world = ManagerWorld();
        var family = Water(null, Format("1 L", "1"));
        await world.CreateFamily.HandleAsync(family);

        await world.DeactivateProduct.HandleAsync(new DeactivateProductCommand(family.Formats[0].Id));

        Assert.Empty(await world.ListProducts.HandleAsync(new ListProductsQuery(null, false)));
        Assert.Single(await world.ListProducts.HandleAsync(new ListProductsQuery(null, true)));
    }

    // 10. Regroupement
    [Fact]
    public async Task Existing_products_are_grouped_into_a_family_with_their_labels()
    {
        var world = ManagerWorld();
        var big = world.GivenProduct("Eau 1,5 L", "1", stock: 12);
        var small = world.GivenProduct("Eau 50 cl", "2", stock: 7);
        var family = FamilyId.New();

        var result = await world.GroupIntoFamily.HandleAsync(
            new GroupIntoFamilyCommand(family, "Eau minérale", "Boissons", [new(big.Id, "1,5 L"), new(small.Id, "50 cl")]));

        Assert.True(result.IsSuccess);
        var products = await AllAsync(world);
        Assert.Equal(["Eau minérale · 1,5 L", "Eau minérale · 50 cl"], products.Select(product => product.Name).Order().ToArray());
        Assert.All(products, product => Assert.Equal(family, product.FamilyId));
        Assert.Equal(12, products.Single(product => product.Id == big.Id).QuantityOnHand);
        Assert.Null(await world.Families.GetAsync(new FamilyId(big.Id.Value), world.Tenant, CancellationToken.None));
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.ProductsGrouped);
    }

    [Fact]
    public async Task Grouping_needs_two_products_with_distinct_labels_that_are_not_already_in_a_family()
    {
        var world = ManagerWorld();
        var first = world.GivenProduct("A", "1");
        var second = world.GivenProduct("B", "2");

        var single = await world.GroupIntoFamily.HandleAsync(
            new GroupIntoFamilyCommand(FamilyId.New(), "X", null, [new(first.Id, "1 L")]));
        var same = await world.GroupIntoFamily.HandleAsync(
            new GroupIntoFamilyCommand(FamilyId.New(), "X", null, [new(first.Id, "1 L"), new(second.Id, "1 l")]));
        var unknown = await world.GroupIntoFamily.HandleAsync(
            new GroupIntoFamilyCommand(FamilyId.New(), "X", null, [new(first.Id, "1 L"), new(ProductId.New(), "2 L")]));

        Assert.Equal(ErrorCodes.RuleViolation, single.Error!.Code);
        Assert.Equal(ErrorCodes.RuleViolation, same.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, unknown.Error!.Code);
        Assert.Equal(0, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_product_already_in_a_multi_format_family_cannot_be_grouped_again()
    {
        var world = ManagerWorld();
        var family = Water();
        await world.CreateFamily.HandleAsync(family);
        var other = world.GivenProduct("Autre", "9");

        var result = await world.GroupIntoFamily.HandleAsync(
            new GroupIntoFamilyCommand(FamilyId.New(), "X", null, [new(family.Formats[0].Id, "a"), new(other.Id, "b")]));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    // 14. Droits
    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Only_a_manager_manages_families(Role role)
    {
        var world = new TestWorld();
        world.ActAs(world.Cashier, role);
        var family = Water();

        var create = await world.CreateFamily.HandleAsync(family);
        var add = await world.AddFormat.HandleAsync(new AddFormatCommand(family.Id, Format("2 L"), null));
        var rename = await world.UpdateFamily.HandleAsync(new UpdateFamilyCommand(family.Id, "X", null));
        var group = await world.GroupIntoFamily.HandleAsync(new GroupIntoFamilyCommand(family.Id, "X", null, []));

        Assert.All(new[] { create.Error, add.Error, rename.Error, group.Error }, error => Assert.Equal(ErrorCodes.Forbidden, error!.Code));
        Assert.Equal(0, world.UnitOfWork.Commits);
    }

    // 12. Isolation
    [Fact]
    public async Task Another_shop_cannot_see_or_change_a_family()
    {
        var world = ManagerWorld();
        var family = Water();
        await world.CreateFamily.HandleAsync(family);
        var other = new TestWorld();
        other.ActAs(other.Manager, Role.Manager);
        foreach (var product in await AllAsync(world))
        {
            other.Products.Put(product);
        }

        other.Families.Put(await world.Families.GetAsync(family.Id, world.Tenant, CancellationToken.None) ?? throw new InvalidOperationException());

        var rename = await other.UpdateFamily.HandleAsync(new UpdateFamilyCommand(family.Id, "Volé", null));
        var add = await other.AddFormat.HandleAsync(new AddFormatCommand(family.Id, Format("9 L"), null));

        Assert.Equal(ErrorCodes.NotFound, rename.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, add.Error!.Code);
    }

    // FMT-05 : catalogue
    [Fact]
    public async Task The_catalogue_tells_the_family_the_label_and_the_photo_to_use()
    {
        var world = ManagerWorld();
        var family = Water();
        await world.CreateFamily.HandleAsync(family);
        var jpeg = ImageSamples.Jpeg();
        await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Id.Value, PhotoOwner.Family, jpeg));
        await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Formats[2].Id.Value, PhotoOwner.Format, jpeg));

        var catalogue = await world.ListProducts.HandleAsync(new ListProductsQuery(null, false));

        var familyPhoto = (await world.Families.GetAsync(family.Id, world.Tenant, CancellationToken.None))!.PhotoId;
        var litre = catalogue.Single(view => view.FormatLabel == "1 L");
        var centilitres = catalogue.Single(view => view.FormatLabel == "30 cl");
        Assert.Equal(family.Id.Value, litre.FamilyId);
        Assert.Equal("Eau minérale", litre.FamilyName);
        Assert.Equal(familyPhoto!.Value.Value, litre.PhotoId);
        Assert.NotNull(centilitres.PhotoId);
        Assert.NotEqual(litre.PhotoId, centilitres.PhotoId);
    }

    [Fact]
    public async Task The_catalogue_never_shows_the_purchase_price_to_a_cashier()
    {
        var world = ManagerWorld();
        await world.CreateFamily.HandleAsync(Water());
        world.ActAs(world.Cashier, Role.Cashier);

        var catalogue = await world.ListProducts.HandleAsync(new ListProductsQuery(null, false));

        Assert.All(catalogue, view => Assert.Null(view.PurchasePriceFcfa));
    }

    [Fact]
    public async Task A_product_without_family_row_is_its_own_family_in_the_catalogue()
    {
        var world = ManagerWorld();
        var product = world.GivenProduct("Savon");
        world.Families.Remove(new FamilyId(product.Id.Value));

        var view = (await world.ListProducts.HandleAsync(new ListProductsQuery(null, false))).Single();

        Assert.Equal("Savon", view.FamilyName);
        Assert.Equal(product.FamilyId.Value, view.FamilyId);
        Assert.Null(view.PhotoId);
    }
}
